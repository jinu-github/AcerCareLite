using System.Text;
using System.Text.Json;

namespace AcerHardwareExplorer;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static (string JsonPath, string MdPath) Write(DiscoveryReport r, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var stamp = r.GeneratedAt.ToString("yyyyMMdd-HHmmss");
        var jsonPath = Path.Combine(outDir, $"AcerHardwareExplorer-{stamp}.json");
        var mdPath = Path.Combine(outDir, $"AcerHardwareExplorer-{stamp}.md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(r, Json));
        File.WriteAllText(mdPath, ToMarkdown(r));
        return (jsonPath, mdPath);
    }

    private static string ToMarkdown(DiscoveryReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AcerHardwareExplorer report");
        sb.AppendLine($"Generated {r.GeneratedAt:u} | tool {r.ToolVersion} | read-only | elevated: {r.IsElevated}");
        sb.AppendLine($"OS: {r.OsDescription}").AppendLine();

        sb.AppendLine("## Identity");
        foreach (var (section, values) in r.Identity)
        {
            sb.AppendLine($"**{section}**");
            foreach (var (k, v) in values) sb.AppendLine($"- {k}: {v}");
        }

        sb.AppendLine().AppendLine($"## WMI namespaces ({r.Namespaces.Count})");
        foreach (var ns in r.Namespaces) sb.AppendLine($"- {ns}");

        sb.AppendLine().AppendLine("## ACPI WMI devices (PNP0C14)");
        Rows(sb, r.AcpiWmiDevices);

        sb.AppendLine().AppendLine("## Acer-named classes (all namespaces)");
        if (r.AcerNamedClasses.Count == 0) sb.AppendLine("_none found_");
        foreach (var c in r.AcerNamedClasses) sb.AppendLine($"- {c.Namespace}:{c.Name}");

        sb.AppendLine().AppendLine("## Acer-related services");
        Rows(sb, r.AcerServices);
        sb.AppendLine().AppendLine("## Acer-related drivers");
        Rows(sb, r.AcerDrivers);

        sb.AppendLine().AppendLine($"## root\\wmi classes with a GUID or methods ({r.RootWmiGuidInventory.Count})");
        foreach (var c in r.RootWmiGuidInventory) sb.AppendLine($"- {c.Name} | guid={c.Guid ?? "-"} | methods={c.MethodCount}");

        sb.AppendLine().AppendLine($"## Inspected classes ({r.InspectedClasses.Count})");
        foreach (var c in r.InspectedClasses)
        {
            sb.AppendLine($"### {c.Namespace}:{c.Name}");
            sb.AppendLine($"matched by: {string.Join(", ", c.MatchedBy)} | guid: {c.Guid ?? "-"} | {c.Description}");
            sb.AppendLine($"properties: {string.Join(", ", c.Properties.Select(p => $"{p.Name}:{p.CimType}{(p.IsArray ? "[]" : "")}"))}");
            foreach (var m in c.Methods)
                sb.AppendLine($"- method `{m.Name}` in({string.Join(", ", m.InParameters.Select(p => $"{p.Name}:{p.CimType}{(p.IsArray ? "[]" : "")}"))}) " +
                              $"out({string.Join(", ", m.OutParameters.Select(p => $"{p.Name}:{p.CimType}{(p.IsArray ? "[]" : "")}"))})  [not invoked]");
        }

        sb.AppendLine().AppendLine("## Standard battery reads");
        Instances(sb, r.BatteryReads);
        sb.AppendLine().AppendLine("## Acer instance reads (opt-in)");
        Instances(sb, r.AcerInstanceReads);

        sb.AppendLine().AppendLine($"## Errors ({r.Errors.Count})");
        foreach (var e in r.Errors) sb.AppendLine($"- {e.Where}: {e.Message}");
        return sb.ToString();
    }

    private static void Rows(StringBuilder sb, List<Dictionary<string, string?>> rows)
    {
        if (rows.Count == 0) { sb.AppendLine("_none found_"); return; }
        foreach (var row in rows) sb.AppendLine("- " + string.Join(" | ", row.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    private static void Instances(StringBuilder sb, List<InstanceDto> items)
    {
        if (items.Count == 0) { sb.AppendLine("_none_"); return; }
        foreach (var i in items)
        {
            sb.AppendLine($"**{i.Source}**");
            foreach (var (k, v) in i.Values) sb.AppendLine($"- {k}: {v}");
        }
    }
}
