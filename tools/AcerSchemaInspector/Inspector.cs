using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace AcerSchemaInspector;

/// <summary>
/// METADATA ONLY. Reads the WMI class definition of root\wmi:BatteryControl (methods, parameters, qualifiers) and the
/// instance names. It never calls a method, never creates a method-parameter object, and never writes anything except
/// its own report files. The guard tests enforce that at source level.
/// </summary>
internal static class Inspector
{
    private const string Namespace = @"root\wmi";
    private const string ClassName = "BatteryControl";

    private sealed record ParamDto(string Name, string CimType, bool IsArray, Dictionary<string, string> Qualifiers);
    private sealed record MethodDto(string Name, Dictionary<string, string> Qualifiers, List<ParamDto> In, List<ParamDto> Out);
    private sealed record PropertyDto(string Name, string CimType, bool IsArray);
    private sealed record Report(
        string Generated, string Machine, bool Elevated, string Namespace, string Class,
        Dictionary<string, string> ClassQualifiers, List<PropertyDto> Properties, List<string> Instances,
        List<MethodDto> Methods, List<string> Errors);

    public static int Run(string[] args)
    {
        string? outDir = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            else { Console.WriteLine("Usage: AcerSchemaInspector [--out <folder>]   (metadata only; never invokes any method)"); return args[i] is "--help" or "-h" ? 0 : 1; }
        }

        var errors = new List<string>();
        var report = new Report(DateTimeOffset.Now.ToString("O"), Environment.MachineName, IsElevated(), Namespace, ClassName,
            new(), new(), new(), new(), errors);

        try
        {
            var scope = new ManagementScope(Namespace, new ConnectionOptions { Timeout = TimeSpan.FromSeconds(15) });
            scope.Connect();
            using var cls = new ManagementClass(scope, new ManagementPath(ClassName), new ObjectGetOptions(null, TimeSpan.FromSeconds(15), true));
            cls.Get();

            foreach (QualifierData q in cls.Qualifiers) report.ClassQualifiers[q.Name] = Format(q.Value);
            foreach (PropertyData p in cls.Properties) report.Properties.Add(new PropertyDto(p.Name, p.Type.ToString(), p.IsArray));

            foreach (MethodData m in cls.Methods)
            {
                var quals = new Dictionary<string, string>();
                foreach (QualifierData q in m.Qualifiers) quals[q.Name] = Format(q.Value);
                report.Methods.Add(new MethodDto(m.Name, quals, Describe(m.InParameters), Describe(m.OutParameters)));
            }
        }
        catch (ManagementException ex)
        {
            errors.Add($"{ex.ErrorCode}: {ex.Message}" + (ex.ErrorCode == ManagementStatus.AccessDenied ? " (try an elevated terminal)" : ""));
        }
        catch (Exception ex)
        {
            errors.Add($"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(Namespace), new ObjectQuery($"SELECT InstanceName FROM {ClassName}"));
            foreach (ManagementBaseObject o in searcher.Get())
                using (o) report.Instances.Add(o["InstanceName"]?.ToString() ?? "(null)");
        }
        catch (Exception ex)
        {
            errors.Add($"Instance query failed: {ex.GetType().Name}: {ex.Message}");
        }

        var console = Render(report);
        Console.WriteLine(console);

        try
        {
            outDir ??= Path.Combine(AppContext.BaseDirectory, "Reports");
            Directory.CreateDirectory(outDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var jsonPath = Path.Combine(outDir, $"BatteryControlSchema-{stamp}.json");
            var mdPath = Path.Combine(outDir, $"BatteryControlSchema-{stamp}.md");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(mdPath, "```text\n" + console + "\n```\n");
            Console.WriteLine($"Report files:\n  {jsonPath}\n  {mdPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not write report files: " + ex.Message);
        }

        return report.Methods.Count > 0 ? 0 : 2;
    }

    private static List<ParamDto> Describe(ManagementBaseObject? parameters)
    {
        var list = new List<ParamDto>();
        if (parameters == null) return list;
        foreach (PropertyData p in parameters.Properties)
        {
            var quals = new Dictionary<string, string>();
            foreach (QualifierData q in p.Qualifiers) quals[q.Name] = Format(q.Value);
            list.Add(new ParamDto(p.Name, p.Type.ToString(), p.IsArray, quals));
        }
        // Declaration order is carried by the WmiDataId / ID qualifier, not by enumeration order.
        return list.OrderBy(p => OrderKey(p)).ToList();
    }

    private static int OrderKey(ParamDto p)
    {
        foreach (var key in new[] { "WmiDataId", "ID" })
            if (p.Qualifiers.TryGetValue(key, out var v) && int.TryParse(v, out var n)) return n;
        return int.MaxValue;
    }

    private static string Format(object? value) => value switch
    {
        null => "(null)",
        Array a => "[" + string.Join(",", a.Cast<object?>().Select(x => x?.ToString())) + "]",
        _ => value.ToString() ?? ""
    };

    private static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Render(Report r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AcerSchemaInspector (metadata only)  {r.Generated}");
        sb.AppendLine($"Machine: {r.Machine}   Elevated: {r.Elevated}");
        sb.AppendLine($"Class: {r.Namespace}:{r.Class}");
        sb.AppendLine("Class qualifiers: " + (r.ClassQualifiers.Count == 0 ? "(none)" : string.Join("; ", r.ClassQualifiers.Select(kv => $"{kv.Key}={kv.Value}"))));
        sb.AppendLine("Instances: " + (r.Instances.Count == 0 ? "(none)" : string.Join(", ", r.Instances)));
        sb.AppendLine("Properties: " + string.Join(", ", r.Properties.Select(p => $"{p.Name}:{p.CimType}{(p.IsArray ? "[]" : "")}")));
        foreach (var m in r.Methods)
        {
            sb.AppendLine();
            sb.AppendLine($"Method {m.Name}   qualifiers: " + (m.Qualifiers.Count == 0 ? "(none)" : string.Join("; ", m.Qualifiers.Select(kv => $"{kv.Key}={kv.Value}"))));
            AppendParams(sb, "  IN ", m.In);
            AppendParams(sb, "  OUT", m.Out);
        }
        foreach (var e in r.Errors) sb.AppendLine("ERROR: " + e);
        return sb.ToString().TrimEnd();
    }

    private static void AppendParams(StringBuilder sb, string label, List<ParamDto> list)
    {
        if (list.Count == 0) { sb.AppendLine($"{label}: (none)"); return; }
        foreach (var p in list)
        {
            var q = p.Qualifiers.Count == 0 ? "" : "   [" + string.Join("; ", p.Qualifiers.Select(kv => $"{kv.Key}={kv.Value}")) + "]";
            sb.AppendLine($"{label}: {p.Name} : {p.CimType}{(p.IsArray ? "[]" : "")}{q}");
        }
    }
}
