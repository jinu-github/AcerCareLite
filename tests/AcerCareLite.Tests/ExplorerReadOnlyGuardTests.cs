using Xunit;

namespace AcerCareLite.Tests;

/// <summary>
/// Safety net for Phase 0: AcerHardwareExplorer must stay read-only.
/// Fails if any of its sources contain a write/invoke API.
/// </summary>
public class ExplorerReadOnlyGuardTests
{
    private static readonly string[] Forbidden =
    {
        "InvokeMethod", ".Put(", ".Delete(", "SetPropertyValue", "CreateInstance(",
        "SetValue(", "CreateSubKey", "DeleteValue", "DeleteSubKey",
        "DeviceIoControl", "CreateFile(", "DllImport", "LibraryImport",
        "Process.Start", "sc.exe", "pnputil"
    };

    [Fact]
    public void Explorer_sources_contain_no_write_or_invoke_apis()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var explorer = Path.Combine(dir!.FullName, "tools", "AcerHardwareExplorer");
        var files = Directory.GetFiles(explorer, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);

        var hits = from f in files
                   let text = File.ReadAllText(f)
                   from token in Forbidden
                   where text.Contains(token, StringComparison.Ordinal)
                   select $"{Path.GetFileName(f)}: {token}";
        Assert.Empty(hits);
    }
}
