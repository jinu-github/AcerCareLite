using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AcerHardwareExplorer;

/// <summary>DISCOVER + READ only. Interpretation happens in a human-reviewed step, not here.</summary>
internal sealed class Discovery
{
    private static readonly Regex AcerStrict = new(@"acer|nitro|predator|wmid|amw|quanta", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Interesting = new(@"acer|nitro|predator|quanta|insyde|gaming|battery|charg|health|thermal|fan|hotkey|kbd|backlight|amw|wmid|wmbh", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SoftwareHint = new(@"acer|quanta|nitro|predator|care ?center", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Reference GUIDs recalled from the Linux acer-wmi driver. Used ONLY to highlight matches in the report.
    // They were written from memory: verify against drivers/platform/x86/acer-wmi.c before relying on them.
    private static readonly HashSet<string> ReferenceGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "6AF4F258-B401-42FD-BE91-3D4AC2D7C0D3",
        "95764E09-FB56-4e83-B31A-37761F60994A",
        "61EF69EA-865C-4BC3-A502-A0DEBA0CB531",
        "7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56",
        "676AA15E-6A47-4D9F-A2CC-1E6D18D14026",
        "67C3371D-95A3-4C37-BB61-DD47B491DAAB"
    };

    // Standard Microsoft battery classes: safe to read by default.
    private static readonly (string Ns, string Class)[] BatteryAllowlist =
    {
        (@"root\cimv2", "Win32_Battery"),
        (@"root\wmi", "BatteryStatus"),
        (@"root\wmi", "BatteryStaticData"),
        (@"root\wmi", "BatteryFullChargedCapacity"),
        (@"root\wmi", "BatteryCycleCount"),
        (@"root\wmi", "BatteryRuntime"),
    };

    private readonly Options _opt;
    private readonly ILogger _log;
    private readonly DiscoveryReport _r = new();

    public Discovery(Options opt, ILogger log) { _opt = opt; _log = log; }

    public DiscoveryReport Run()
    {
        Step("Identity", CollectIdentity);
        Step("Namespaces", EnumerateNamespaces);
        Step("Classes", EnumerateClasses);
        Step("ACPI WMI devices", CollectAcpiWmiDevices);
        Step("Acer services/drivers", CollectAcerSoftware);
        Step("Battery (standard classes)", ReadBattery);
        if (_opt.ReadAcerInstances) Step("Acer instance reads (opt-in)", ReadAcerInstances);
        else _log.LogInformation("Skipping Acer instance reads (use --read-acer-instances to enable).");
        return _r;
    }

    private void Step(string name, Action action)
    {
        _log.LogInformation("→ {Step}", name);
        try { action(); }
        catch (Exception ex) { Fail(name, ex); }
    }

    private void Fail(string where, Exception ex)
    {
        _log.LogWarning("{Where}: {Message}", where, ex.Message);
        _r.Errors.Add(new ProbeError(where, ex.Message));
    }

    private void CollectIdentity()
    {
        _r.OsDescription = $"{RuntimeInformation.OSDescription} (Environment.OSVersion {Environment.OSVersion.Version})";
        _r.IsElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        void Section(string key, string wql, params string[] props)
        {
            try
            {
                var rows = ReadOnlyWmi.Query(@"root\cimv2", wql, props);
                if (rows.Count > 0) _r.Identity[key] = rows[0];
            }
            catch (Exception ex) { Fail($"Identity/{key}", ex); }
        }

        Section("ComputerSystem", "SELECT * FROM Win32_ComputerSystem", "Manufacturer", "Model", "SystemFamily", "SystemSKUNumber", "SystemType");
        Section("BIOS", "SELECT * FROM Win32_BIOS", "Manufacturer", "SMBIOSBIOSVersion", "BIOSVersion", "ReleaseDate", "SMBIOSMajorVersion", "SMBIOSMinorVersion");
        Section("BaseBoard", "SELECT * FROM Win32_BaseBoard", "Manufacturer", "Product", "Version");
    }

    private void EnumerateNamespaces()
    {
        void Walk(string ns, int depth)
        {
            _r.Namespaces.Add(ns);
            if (depth >= 3) return;
            try { foreach (var child in ReadOnlyWmi.ChildNamespaces(ns)) Walk(child, depth + 1); }
            catch (Exception ex) { Fail($"Namespaces/{ns}", ex); }
        }
        Walk("root", 0);
        _r.Namespaces.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private void EnumerateClasses()
    {
        // 1) Any class whose NAME looks Acer-related, in any namespace.
        foreach (var ns in _r.Namespaces)
        {
            try
            {
                foreach (var name in ReadOnlyWmi.ClassNames(ns))
                    if (AcerStrict.IsMatch(name)) _r.AcerNamedClasses.Add(new ClassRef(ns, name, null, 0));
            }
            catch (Exception ex) { Fail($"Classes/{ns}", ex); }
        }

        // 2) root\wmi is where ACPI-WMI (firmware) data blocks surface: inventory every class + GUID.
        var described = new List<ClassInfoDto>();
        foreach (var name in SafeClassNames(@"root\wmi"))
        {
            try
            {
                var dto = ReadOnlyWmi.DescribeClass(@"root\wmi", name);
                described.Add(dto);
                if (dto.Guid != null || dto.Methods.Count > 0)
                    _r.RootWmiGuidInventory.Add(new ClassRef(dto.Namespace, dto.Name, dto.Guid, dto.Methods.Count));
            }
            catch (Exception ex) { Fail($"Describe/root\\wmi:{name}", ex); }
        }

        // 3) Full detail (properties + method signatures) for classes that match, or everything with --inspect-all.
        foreach (var dto in described)
        {
            var why = new List<string>();
            if (Interesting.IsMatch(dto.Name)) why.Add("name");
            if (dto.Description != null && Interesting.IsMatch(dto.Description)) why.Add("description");
            if (dto.Guid != null && ReferenceGuids.Contains(dto.Guid)) why.Add("guid:acer-wmi-reference");
            if (why.Count > 0 || _opt.InspectAll) _r.InspectedClasses.Add(dto with { MatchedBy = why });
        }

        // Acer-named classes found outside root\wmi.
        foreach (var c in _r.AcerNamedClasses.Where(c => !c.Namespace.Equals(@"root\wmi", StringComparison.OrdinalIgnoreCase)))
        {
            try { _r.InspectedClasses.Add(ReadOnlyWmi.DescribeClass(c.Namespace, c.Name) with { MatchedBy = new[] { "name" } }); }
            catch (Exception ex) { Fail($"Describe/{c.Namespace}:{c.Name}", ex); }
        }
    }

    private IEnumerable<string> SafeClassNames(string ns)
    {
        try { return ReadOnlyWmi.ClassNames(ns); }
        catch (Exception ex) { Fail($"Classes/{ns}", ex); return Array.Empty<string>(); }
    }

    private void CollectAcpiWmiDevices()
    {
        var rows = ReadOnlyWmi.Query(@"root\cimv2",
            @"SELECT Name, DeviceID, HardwareID, Manufacturer, Status FROM Win32_PnPEntity WHERE DeviceID LIKE 'ACPI\\%'",
            "Name", "DeviceID", "HardwareID", "Manufacturer", "Status");
        foreach (var row in rows)
        {
            var text = string.Join(" ", row.Values);
            if (text.Contains("PNP0C14", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(text, "WMI", RegexOptions.IgnoreCase))
                _r.AcpiWmiDevices.Add(row);
        }
    }

    private void CollectAcerSoftware()
    {
        foreach (var row in ReadOnlyWmi.Query(@"root\cimv2", "SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_Service",
                     "Name", "DisplayName", "State", "StartMode", "PathName"))
            if (SoftwareHint.IsMatch(string.Join(" ", row.Values))) _r.AcerServices.Add(row);

        foreach (var row in ReadOnlyWmi.Query(@"root\cimv2", "SELECT Name, DisplayName, State, PathName FROM Win32_SystemDriver",
                     "Name", "DisplayName", "State", "PathName"))
            if (SoftwareHint.IsMatch(string.Join(" ", row.Values))) _r.AcerDrivers.Add(row);
    }

    private void ReadBattery()
    {
        foreach (var (ns, cls) in BatteryAllowlist)
        {
            try { _r.BatteryReads.AddRange(ReadOnlyWmi.ReadInstances(ns, cls)); }
            catch (Exception ex) { Fail($"Battery/{ns}:{cls}", ex); }
        }
    }

    /// <summary>
    /// Opt-in. Reading a firmware-backed data block asks the BIOS to run its query method, so it is a
    /// read, but it is executed by vendor code. Only classes whose NAME matches Acer patterns are read.
    /// </summary>
    private void ReadAcerInstances()
    {
        foreach (var c in _r.AcerNamedClasses)
        {
            try { _r.AcerInstanceReads.AddRange(ReadOnlyWmi.ReadInstances(c.Namespace, c.Name)); }
            catch (Exception ex) { Fail($"AcerRead/{c.Namespace}:{c.Name}", ex); }
        }
    }
}
