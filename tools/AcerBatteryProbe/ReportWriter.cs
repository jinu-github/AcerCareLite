using System.Text;
using System.Text.Json;

namespace AcerBatteryProbe;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static (string JsonPath, string MdPath) Write(ProbeReport r, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var stamp = r.GeneratedAt.ToString("yyyyMMdd-HHmmss");
        var slug = string.Concat(r.Note.Where(char.IsLetterOrDigit).Take(24));
        var name = $"AcerBatteryProbe-{stamp}{(slug.Length > 0 ? "-" + slug : "")}";
        var jsonPath = Path.Combine(outDir, name + ".json");
        var mdPath = Path.Combine(outDir, name + ".md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(r, Json));
        File.WriteAllText(mdPath, ToMarkdown(r));
        return (jsonPath, mdPath);
    }

    private static string ToMarkdown(ProbeReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AcerBatteryProbe report");
        sb.AppendLine($"Generated {r.GeneratedAt:u} | tool {r.ToolVersion} | elevated: {r.IsElevated}");
        sb.AppendLine($"Note (set by you): {(r.Note.Length > 0 ? r.Note : "-")}").AppendLine();

        sb.AppendLine("## Machine");
        foreach (var (k, v) in r.Machine) sb.AppendLine($"- {k}: {v}");
        sb.AppendLine($"- BatteryControl instance: {r.InstanceName ?? "not found"}").AppendLine();

        sb.AppendLine("## Signature check (no invocation)");
        foreach (var c in r.SchemaChecks) sb.AppendLine($"- {c.Method}: {(c.Ok ? "OK" : "MISMATCH")} - {c.Detail}");

        sb.AppendLine().AppendLine($"## Firmware reads (attempted: {r.InvocationsAttempted})");
        if (r.Status is { } s)
        {
            sb.AppendLine($"- uFunctionList: 0x{s.FunctionList:X2}");
            sb.AppendLine($"- uReturn: [{string.Join(", ", s.Ret)}]");
            sb.AppendLine($"- uFunctionStatus: [{string.Join(", ", s.FunctionStatus)}]");
        }
        else sb.AppendLine("- health control status: not read");
        if (r.Temperature is { } t)
            sb.AppendLine($"- battery temperature raw {t.Raw} -> {t.Celsius:F1} C (assumes tenths of kelvin; plausible: {t.Plausible})");
        else sb.AppendLine("- battery temperature: not read");

        sb.AppendLine().AppendLine("## Decoded (per open-source documentation, unverified on this BIOS)");
        foreach (var d in r.Decoded) sb.AppendLine($"- {d}");

        sb.AppendLine().AppendLine("## Windows battery numbers at the same moment");
        foreach (var (cls, row) in r.BatteryContext)
        {
            sb.AppendLine($"**{cls}**");
            foreach (var (k, v) in row) sb.AppendLine($"- {k}: {v}");
        }

        sb.AppendLine().AppendLine($"## Errors ({r.Errors.Count})");
        foreach (var e in r.Errors) sb.AppendLine($"- {e}");
        return sb.ToString();
    }
}
