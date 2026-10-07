using Xunit;

namespace AcerCareLite.Tests;

/// <summary>
/// Phase 7b-0: the schema inspector reads WMI metadata only, and the install script touches only its own folder.
/// </summary>
public class SchemaInspectorGuardTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static List<string> InspectorSources()
    {
        var sep = Path.DirectorySeparatorChar;
        var dir = Path.Combine(Root(), "tools", "AcerSchemaInspector");
        Assert.True(Directory.Exists(dir), dir);
        return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}")).ToList();
    }

    [Fact]
    public void Inspector_cannot_invoke_or_modify_anything()
    {
        var files = InspectorSources();
        Assert.NotEmpty(files);
        string[] forbidden =
        {
            "InvokeMethod", "InvokeMethodAsync", "GetMethodParameters", "ExecMethod", "SetBattery", "BatteryFunctionData",
            ".Put(", ".Delete(", "SetPropertyValue", "CreateInstance(", "DllImport", "LibraryImport", "DeviceIoControl",
            "Process.Start", "Registry"
        };
        var hits = from f in files
                   let text = File.ReadAllText(f)
                   from token in forbidden
                   where text.Contains(token, StringComparison.Ordinal)
                   select $"{Path.GetFileName(f)}: {token}";
        Assert.Empty(hits);
    }

    [Fact]
    public void Inspector_reads_method_metadata_and_stands_alone()
    {
        var text = string.Join("\n", InspectorSources().Select(File.ReadAllText));
        Assert.Contains("cls.Methods", text);
        Assert.Contains("InParameters", text);
        Assert.Contains("OutParameters", text);

        var csproj = File.ReadAllText(Path.Combine(Root(), "tools", "AcerSchemaInspector", "AcerSchemaInspector.csproj"));
        Assert.DoesNotContain("ProjectReference", csproj);
    }

    [Fact]
    public void Install_script_requires_admin_and_only_touches_its_own_folder()
    {
        var path = Path.Combine(Root(), "scripts", "Install-AcerCareLite.ps1");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);

        Assert.Contains("#Requires -RunAsAdministrator", text);
        Assert.Contains("$env:ProgramFiles", text);
        Assert.Contains("$Target   = Join-Path $env:ProgramFiles $AppName", text);

        foreach (var banned in new[] { "ExecutionPolicy", "Invoke-Expression", "iex ", "Set-MpPreference", "Add-MpPreference", "Invoke-WebRequest", "DownloadString", "New-Service", "schtasks", "reg add", "Set-ItemProperty" })
            Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);

        // Every deletion must name the install folder or the temporary staging folder, nothing else.
        foreach (var line in text.Split('\n').Where(l => l.Contains("Remove-Item")))
            Assert.True(line.Contains("$Target") || line.Contains("$Staging"), line.Trim());
    }
}
