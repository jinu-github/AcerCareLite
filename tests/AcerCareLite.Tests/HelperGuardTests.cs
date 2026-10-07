using System.Text.RegularExpressions;
using Xunit;

namespace AcerCareLite.Tests;

/// <summary>
/// Phase 7a safety net: the Acer helper and its callers are read-only. No setter, no extra firmware methods,
/// exactly one firmware call site, and no administrator manifest on the main application.
/// </summary>
public class HelperGuardTests
{
    private static readonly string[] Forbidden =
    {
        "BatteryFunctionData", ".Put(", ".Delete(", "SetPropertyValue", "CreateInstance(",
        "DeviceIoControl", "Registry"
    };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static List<string> Files(string relative, string pattern = "*.cs")
    {
        var sep = Path.DirectorySeparatorChar;
        var dir = Path.Combine(Root(), relative.Replace('/', sep));
        Assert.True(Directory.Exists(dir), dir);
        return Directory.GetFiles(dir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}")).ToList();
    }

    private static List<string> AcerFiles() =>
        Files("src/AcerCareLite.AcerHelper").Concat(Files("src/AcerCareLite.Acer")).Concat(Files("src/AcerCareLite.Core/Acer")).ToList();

    [Fact]
    public void Acer_sources_contain_no_forbidden_tokens()
    {
        var files = AcerFiles();
        Assert.NotEmpty(files);
        var hits = from f in files
                   let text = File.ReadAllText(f)
                   from token in Forbidden
                   where text.Contains(token, StringComparison.Ordinal)
                   select $"{Path.GetFileName(f)}: {token}";
        Assert.Empty(hits);
    }

    private static readonly string[] AllowedNativeImports =
    {
        "GetNamedPipeClientProcessId", "GetNamedPipeServerProcessId", "OpenProcess", "QueryFullProcessImageNameW", "CloseHandle"
    };

    [Fact]
    public void Native_imports_exist_only_in_PeerProcess_and_only_the_allowed_five()
    {
        var offenders = AcerFiles().Where(f => !f.EndsWith("PeerProcess.cs"))
            .Where(f => { var t = File.ReadAllText(f); return t.Contains("DllImport") || t.Contains("LibraryImport"); })
            .Select(Path.GetFileName);
        Assert.Empty(offenders);

        var peer = AcerFiles().Single(f => f.EndsWith("PeerProcess.cs"));
        var text = File.ReadAllText(peer);
        Assert.DoesNotContain("LibraryImport", text);

        var imported = Regex.Matches(text, @"static extern\s+[\w\.]+\s+(\w+)\s*\(").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        Assert.Equal(AllowedNativeImports.OrderBy(x => x).ToArray(), imported);

        var libraries = Regex.Matches(text, "DllImport\\(\"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(new[] { "kernel32.dll" }, libraries);

        // Query-only rights: no memory or token access flags may appear.
        Assert.Contains("0x1000", text); // PROCESS_QUERY_LIMITED_INFORMATION
        Assert.DoesNotContain("0x0010", text); // PROCESS_VM_READ
        Assert.DoesNotContain("0x0020", text); // PROCESS_VM_WRITE
        Assert.DoesNotContain("0x001F0FFF", text); // PROCESS_ALL_ACCESS
    }

    [Fact]
    public void Development_host_bypass_exists_only_inside_a_debug_block_in_PeerProcess()
    {
        var hits = Files("src").Where(f => File.ReadAllText(f).Contains("#if DEBUG")).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "PeerProcess.cs" }, hits);

        var text = File.ReadAllText(AcerFiles().Single(f => f.EndsWith("PeerProcess.cs")));
        Assert.Matches(@"#if DEBUG\s+private const bool AllowDevelopmentHost = true;\s+#else\s+private const bool AllowDevelopmentHost = false;\s+#endif", text);

        // The app-side check of the helper never gets the development exception.
        Assert.Matches(@"VerifyClient[^;]*?expectedImagePath, false, ""client""", text);
        Assert.DoesNotContain("allowDevelopmentHost: true", string.Join("\n", Files("src").Select(File.ReadAllText)));
    }

    [Fact]
    public void Helper_project_links_only_the_five_shared_files_and_references_nothing_else()
    {
        var csproj = File.ReadAllText(Files("src/AcerCareLite.AcerHelper", "*.csproj").Single());
        var linked = Regex.Matches(csproj, @"<Compile Include=""([^""]+)""").Select(m => Path.GetFileName(m.Groups[1].Value.Replace('\\', '/'))).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "AcerHelperProtocol.cs", "HelperLocationGate.cs", "PeerPolicy.cs", "PeerProcess.cs", "ProtectedLocationPolicy.cs" }, linked);
        Assert.DoesNotContain("ProjectReference", csproj);
    }

    [Fact]
    public void Change_requests_get_the_strict_peer_check_in_every_build_configuration()
    {
        var peer = File.ReadAllText(AcerFiles().Single(f => f.EndsWith("PeerProcess.cs")));
        Assert.Matches(@"VerifyServerStrict[^;]*?expectedImagePath, false, ""server""", peer);

        var program = File.ReadAllText(Files("src/AcerCareLite.AcerHelper").Single(f => f.EndsWith("Program.cs")));
        Assert.Matches(@"operation == AcerHelperOperation\.SetHealthMode\s*\?\s*PeerVerifier\.VerifyServerStrict", program);
    }

    [Fact]
    public void Helper_never_starts_other_programs_or_touches_files()
    {
        var helper = Files("src/AcerCareLite.AcerHelper");
        var hits = from f in helper
                   let text = File.ReadAllText(f)
                   from token in new[] { "Process.Start", "File.Write", "File.Create", "File.Delete", "Directory.Delete", "Assembly.Load" }
                   where text.Contains(token, StringComparison.Ordinal)
                   select $"{Path.GetFileName(f)}: {token}";
        Assert.Empty(hits);
    }

    [Fact]
    public void The_setter_name_exists_only_in_the_writer_file()
    {
        var hits = Files("src").Where(f => File.ReadAllText(f).Contains("SetBattery", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "BatteryHealthWriter.cs" }, hits);
    }

    [Fact]
    public void Process_start_is_only_in_the_launcher()
    {
        var hits = AcerFiles().Where(f => !f.EndsWith("ElevatedHelperChannel.cs"))
            .Where(f => File.ReadAllText(f).Contains("Process.Start", StringComparison.Ordinal))
            .Select(Path.GetFileName);
        Assert.Empty(hits);
    }

    [Fact]
    public void Exactly_two_firmware_call_sites_one_getter_in_the_reader_one_setter_in_the_writer()
    {
        var counts = AcerFiles().ToDictionary(f => Path.GetFileName(f)!, f => Regex.Matches(File.ReadAllText(f), "InvokeMethod").Count);
        Assert.Equal(1, counts["BatteryHealthReader.cs"]);
        Assert.Equal(1, counts["BatteryHealthWriter.cs"]);
        Assert.All(counts.Where(kv => kv.Key is not ("BatteryHealthReader.cs" or "BatteryHealthWriter.cs")), kv => Assert.Equal(0, kv.Value));
    }

    [Fact]
    public void Each_firmware_file_names_only_its_own_method()
    {
        var reader = File.ReadAllText(AcerFiles().First(f => f.EndsWith("BatteryHealthReader.cs")));
        var writer = File.ReadAllText(AcerFiles().First(f => f.EndsWith("BatteryHealthWriter.cs")));
        var pattern = @"const string \w+Method = ""(\w+)""";

        Assert.Equal(new[] { "GetBatteryHealthControlStatus" }, Regex.Matches(reader, pattern).Select(m => m.Groups[1].Value).ToArray());
        Assert.Equal(new[] { "SetBatteryHealthControl" }, Regex.Matches(writer, pattern).Select(m => m.Groups[1].Value).ToArray());
    }

    [Fact]
    public void Main_app_has_no_administrator_manifest()
    {
        var appFiles = Files("src/AcerCareLite.App", "*.*");
        Assert.DoesNotContain(appFiles, f => f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase));
        var hits = appFiles.Where(f => f.EndsWith(".cs") || f.EndsWith(".csproj") || f.EndsWith(".xaml"))
            .Where(f => { var t = File.ReadAllText(f); return t.Contains("requireAdministrator") || t.Contains("ApplicationManifest"); })
            .Select(Path.GetFileName);
        Assert.Empty(hits);
    }

    [Fact]
    public void Only_the_helper_requests_administrator()
    {
        var manifest = Files("src/AcerCareLite.AcerHelper", "app.manifest").Single();
        Assert.Contains("requireAdministrator", File.ReadAllText(manifest));
    }
}
