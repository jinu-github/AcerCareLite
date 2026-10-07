using AcerHardwareExplorer;
using Microsoft.Extensions.Logging;

if (args.Contains("--help") || args.Contains("-h")) { PrintHelp(); return 0; }
if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("AcerHardwareExplorer only runs on Windows."); return 2; }

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var options = new Options(
    OutDir: ArgValue("--out") ?? Path.Combine(AppContext.BaseDirectory, "Reports"),
    ReadAcerInstances: args.Contains("--read-acer-instances"),
    InspectAll: args.Contains("--inspect-all"));

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("AcerHardwareExplorer");

logger.LogInformation("READ-ONLY discovery. Nothing is written to WMI, registry, EC or firmware.");
var report = new Discovery(options, logger).Run();
var (jsonPath, mdPath) = ReportWriter.Write(report, options.OutDir);

logger.LogInformation("Report written:\n  {Md}\n  {Json}", mdPath, jsonPath);
return 0;

static void PrintHelp() => Console.WriteLine("""
AcerHardwareExplorer - read-only WMI discovery for Acer laptops

  --out <dir>              Report folder (default: .\Reports next to the exe)
  --read-acer-instances    Also read instances of classes whose NAME looks Acer-related.
                           Still read-only, but the BIOS executes its own query code.
  --inspect-all            Include full property/method detail for every root\wmi class
  -h, --help               Show this help

Run from a normal (non-admin) terminal first. Some root\wmi data may be blank or access-denied;
those cases are listed under "Errors" in the report.
""");
