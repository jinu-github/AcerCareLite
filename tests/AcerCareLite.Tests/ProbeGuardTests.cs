using System.Text.RegularExpressions;
using Xunit;

namespace AcerCareLite.Tests;

/// <summary>
/// Phase 0b safety net: AcerBatteryProbe may call exactly one firmware-invoking API, in exactly one file,
/// and may never mention the setter methods or the undocumented function-data methods.
/// </summary>
public class ProbeGuardTests
{
    private static readonly string[] Forbidden =
    {
        "SetBattery", "BatteryFunctionData", ".Put(", ".Delete(", "SetPropertyValue", "CreateInstance(",
        "DllImport", "LibraryImport", "DeviceIoControl", "Process.Start", "Registry"
    };

    private static List<string> ProbeFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var root = Path.Combine(dir!.FullName, "tools", "AcerBatteryProbe");
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}")).ToList();
    }

    [Fact]
    public void No_forbidden_tokens()
    {
        var files = ProbeFiles();
        Assert.NotEmpty(files);
        var hits = from f in files
                   let text = File.ReadAllText(f)
                   from token in Forbidden
                   where text.Contains(token, StringComparison.Ordinal)
                   select $"{Path.GetFileName(f)}: {token}";
        Assert.Empty(hits);
    }

    [Fact]
    public void Single_invoke_call_site_in_reader_only()
    {
        var files = ProbeFiles();
        var counts = files.ToDictionary(f => Path.GetFileName(f)!, f => Regex.Matches(File.ReadAllText(f), "InvokeMethod").Count);
        Assert.Equal(1, counts["BatteryControlReader.cs"]);
        Assert.All(counts.Where(kv => kv.Key != "BatteryControlReader.cs"), kv => Assert.Equal(0, kv.Value));
    }

    [Fact]
    public void Only_two_getter_methods_are_named()
    {
        var text = File.ReadAllText(ProbeFiles().First(f => f.EndsWith("BatteryControlReader.cs")));
        var methodConsts = Regex.Matches(text, @"const string \w+Method = ""(\w+)""").Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "GetBattInfoInterface", "GetBatteryHealthControlStatus" }, methodConsts);
    }
}
