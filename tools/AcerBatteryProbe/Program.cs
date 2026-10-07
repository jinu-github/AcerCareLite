using System.Management;
using System.Security.Principal;
using AcerBatteryProbe;
using Microsoft.Extensions.Logging;

if (args.Contains("--help") || args.Contains("-h")) { PrintHelp(); return 0; }
if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows only."); return 2; }

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var outDir = ArgValue("--out") ?? Path.Combine(AppContext.BaseDirectory, "Reports");
var checkOnly = args.Contains("--check-only");

using var lf = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .SetMinimumLevel(LogLevel.Information));
var log = lf.CreateLogger("AcerBatteryProbe");

var report = new ProbeReport { Note = ArgValue("--note") ?? "" };
report.IsElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

try
{
    var scope = new ManagementScope(@"root\cimv2"); scope.Connect();
    foreach (var (cls, prop) in new[] { ("Win32_ComputerSystem", "Model"), ("Win32_BIOS", "SMBIOSBIOSVersion") })
    {
        using var s = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT {prop} FROM {cls}"));
        foreach (ManagementBaseObject o in s.Get()) using (o) { report.Machine[$"{cls}.{prop}"] = o[prop]?.ToString(); break; }
    }
}
catch (Exception ex) { report.Errors.Add($"Machine: {ex.Message}"); }

log.LogInformation("Model: {M} | BIOS: {B}", report.Machine.GetValueOrDefault("Win32_ComputerSystem.Model"), report.Machine.GetValueOrDefault("Win32_BIOS.SMBIOSBIOSVersion"));

BatteryContext.Capture(report);

try
{
    var reader = new BatteryControlReader();
    report.InstanceName = reader.FindInstanceName();
    report.SchemaChecks.AddRange(reader.CheckSchemas());
    foreach (var c in report.SchemaChecks) log.LogInformation("Signature {M}: {R}", c.Method, c.Ok ? "OK" : "MISMATCH - " + c.Detail);

    if (report.SchemaChecks.Any(c => !c.Ok))
        report.Errors.Add("Signature mismatch: firmware reads were NOT attempted. Share this report before going further.");
    else if (checkOnly)
        log.LogInformation("--check-only: skipping firmware reads.");
    else
    {
        report.InvocationsAttempted = true;
        try { report.Status = reader.ReadHealthControlStatus(); }
        catch (Exception ex) { report.Errors.Add($"ReadHealthControlStatus: {ex.Message}"); log.LogWarning("Status read failed: {E}", ex.Message); }
        try { report.Temperature = reader.ReadBatteryTemperature(); }
        catch (Exception ex) { report.Errors.Add($"ReadBatteryTemperature: {ex.Message}"); log.LogWarning("Temperature read failed: {E}", ex.Message); }
    }
}
catch (Exception ex) { report.Errors.Add($"BatteryControl: {ex.Message}"); log.LogWarning("BatteryControl: {E}", ex.Message); }

if (report.Status is { } st)
{
    report.Decoded.Add($"Health mode supported: {st.HealthModeSupported}");
    report.Decoded.Add($"Calibration mode supported: {st.CalibrationSupported}");
    report.Decoded.Add($"Health mode (80% limit) enabled: {st.HealthModeEnabled?.ToString() ?? "unknown"}");
    report.Decoded.Add($"Calibration mode enabled: {st.CalibrationEnabled?.ToString() ?? "unknown"}");
    if (st.CalibrationEnabled == true) report.Decoded.Add("Calibration appears active. This tool never changes it.");
    if (st.Ret.Length != 2 || st.FunctionStatus.Length != 5)
        report.Decoded.Add($"Unexpected output sizes (uReturn={st.Ret.Length}, uFunctionStatus={st.FunctionStatus.Length}; documented 2 and 5). Do not trust the decoded flags.");
    if (st.Ret.Any(b => b != 0)) report.Decoded.Add("uReturn is non-zero; its meaning is undocumented, so treat these values with suspicion.");
    if (!st.HealthModeSupported && st.FunctionList == 0) report.Decoded.Add("function list is 0: the firmware reports no health-control functions on this BIOS.");
}

var (jsonPath, mdPath) = ReportWriter.Write(report, outDir);
log.LogInformation("Report written:\n  {Md}\n  {Json}", mdPath, jsonPath);
return 0;

static void PrintHelp() => Console.WriteLine("""
AcerBatteryProbe - read-only battery health-control probe (Phase 0b)

  --note "<text>"   Label saved in the report, e.g. "care-center-limit-off". Use it every run.
  --out <dir>       Report folder (default: .\Reports next to the exe)
  --check-only      Compare method signatures only; do not call into firmware
  -h, --help

Calls exactly two getter methods with fixed arguments. It cannot call any setter.
If a normal terminal gets "Access denied", re-run from an elevated terminal.
""");
