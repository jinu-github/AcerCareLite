using System.Text.RegularExpressions;
using Xunit;

namespace AcerCareLite.Tests;

/// <summary>
/// Phase 7b-1 source guards around the change path: where the write flag may appear, that the default build has no write
/// support, that the writer can change one bit only, in a fixed order, once, and that confirmation and audit cannot be skipped.
/// </summary>
public class WriteGuardTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static List<string> Files(string relative, string pattern)
    {
        var sep = Path.DirectorySeparatorChar;
        var dir = Path.Combine(Root(), relative.Replace('/', sep));
        Assert.True(Directory.Exists(dir), dir);
        return Directory.GetFiles(dir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}")).ToList();
    }

    private static string Text(string relative)
    {
        var path = Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), path);
        return File.ReadAllText(path);
    }

    private static int Count(string text, string token) => text.Split(new[] { token }, StringSplitOptions.None).Length - 1;

    private const string Writer = "src/AcerCareLite.AcerHelper/BatteryHealthWriter.cs";

    [Fact]
    public void Default_build_contains_no_write_support()
    {
        Assert.False(AcerCareLite.Core.Acer.WriteBuild.Compiled);
    }

    [Fact]
    public void Write_symbol_appears_only_in_the_build_switch_and_the_writer()
    {
        var hits = Files("src", "*.cs").Where(f => File.ReadAllText(f).Contains("ACER_WRITE_ENABLED")).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "BatteryHealthWriter.cs", "WriteBuild.cs" }, hits);
    }

    [Fact]
    public void Write_symbol_is_defined_only_by_an_explicit_property_and_never_by_a_project_file()
    {
        var props = Text("Directory.Build.props");
        Assert.Contains("Condition=\"'$(AcerWriteEnabled)' == 'true'\"", props);
        Assert.Equal(1, Count(props, "ACER_WRITE_ENABLED"));
        Assert.DoesNotContain("<AcerWriteEnabled>", props);

        foreach (var dir in new[] { "src", "tests", "tools" })
            foreach (var csproj in Files(dir, "*.csproj"))
            {
                var text = File.ReadAllText(csproj);
                Assert.DoesNotContain("ACER_WRITE_ENABLED", text);
                Assert.DoesNotContain("AcerWriteEnabled", text);
            }
    }

    [Fact]
    public void Writer_does_its_steps_in_the_safe_order()
    {
        var text = Text(Writer);
        string[] steps =
        {
            "new HelperLocationGate(self)",
            "new HelperLocationGate(Path.Combine(AppContext.BaseDirectory",
            "CheckMethodSchema(session.Class, \"GetBatteryHealthControlStatus\"",
            "BatteryHealthReader.QueryStatus(session)",
            "CheckPrecondition(before)",
            "AcerHelperPhase.PreReadOk",
            "AcerHelperPhase.WriteSent",
            "InvokeMethod(SetMethod"
        };
        var last = -1;
        foreach (var step in steps)
        {
            var index = text.IndexOf(step, StringComparison.Ordinal);
            Assert.True(index >= 0, "missing: " + step);
            Assert.True(index > last, "out of order: " + step);
            last = index;
        }
    }

    [Fact]
    public void Writer_can_change_only_the_health_bit_and_calls_the_setter_once_without_a_loop()
    {
        var text = Text(Writer);

        Assert.Equal(1, Count(text, "const int HealthModeMask = 0x01;"));
        Assert.Single(Regex.Matches(text, @"const int \w*Mask\b"));
        Assert.Equal(1, Count(text, "args[\"uFunctionMask\"] = (byte)HealthModeMask;"));
        Assert.Equal(2, Count(text, "\"uFunctionMask\""));               // the schema entry and the single assignment
        Assert.Equal(1, Count(text, "args[\"uFunctionStatus\"] = (byte)target;"));
        Assert.Equal(1, Count(text, "args[\"uBatteryNo\"] = (byte)BatteryNumber;"));
        Assert.Equal(1, Count(text, "args[\"uReservedIn\"] = new byte[5];"));
        Assert.Equal(1, Count(text, "InvokeMethod(SetMethod, args, null)"));

        // No other mask value and no other function is ever named in this file.
        Assert.DoesNotContain("0x02", text);
        Assert.DoesNotContain("0x03", text);
        Assert.False(text.Contains("alibrat", StringComparison.OrdinalIgnoreCase));

        // Between the write marker and the readback there is exactly the one call, and no loop or retry.
        var from = text.IndexOf("// 5. The single setter call.", StringComparison.Ordinal);
        var to = text.IndexOf("// 6. Bounded readback.", StringComparison.Ordinal);
        Assert.True(from > 0 && to > from);
        var section = text.Substring(from, to - from);
        foreach (var banned in new[] { "while", "for (", "foreach", "goto", "retry", "Retry" })
            Assert.DoesNotContain(banned, section);
    }

    [Fact]
    public void Timeouts_are_ordered_readback_then_helper_watchdog_then_app_wait()
    {
        static int Seconds(string text, string name)
        {
            var m = Regex.Match(text, name + @"\s*=\s*TimeSpan\.FromSeconds\((\d+)\)");
            Assert.True(m.Success, name);
            return int.Parse(m.Groups[1].Value);
        }

        var readback = Seconds(Text(Writer), "ReadbackWindow");
        var watchdog = Seconds(Text("src/AcerCareLite.AcerHelper/Program.cs"), "Watchdog");
        var appWait = Seconds(Text("src/AcerCareLite.Acer/ElevatedHelperChannel.cs"), "ResultTimeout");

        Assert.True(readback >= 15, "the readback window must be long enough to cover slow firmware");
        Assert.True(watchdog >= readback + 15, "the helper's watchdog must leave room for the readback plus the other steps");
        Assert.True(appWait >= watchdog + 3, "the app must wait longer than the helper's own watchdog");
    }

    [Fact]
    public void Helper_only_changes_state_after_telling_the_app_and_never_without_the_strict_checks()
    {
        var program = Text("src/AcerCareLite.AcerHelper/Program.cs");
        // Started is announced before any firmware work, for both operations.
        Assert.True(program.IndexOf("AcerHelperPhase.Started", StringComparison.Ordinal) < program.IndexOf("CollectChange(nonce", StringComparison.Ordinal));
        // The change path checks elevation before it reaches the writer.
        Assert.True(program.IndexOf("IsElevated()", program.IndexOf("CollectChange(string", StringComparison.Ordinal), StringComparison.Ordinal)
                    < program.IndexOf("BatteryHealthWriter.Execute", StringComparison.Ordinal));
        // Every change result echoes the request.
        Assert.Contains("RequestedEnabled = enable", program);
    }

    [Fact]
    public void Card_asks_for_confirmation_before_the_write_service_and_calls_it_from_one_place()
    {
        var card = Text("src/AcerCareLite.Core/Presentation/AcerHealthCardViewModel.cs");
        Assert.Equal(1, Count(card, "_write.SetAsync("));
        Assert.True(card.IndexOf("_confirm.Confirm(", StringComparison.Ordinal) >= 0);
        Assert.True(card.IndexOf("_confirm.Confirm(", StringComparison.Ordinal) < card.IndexOf("_write.SetAsync(", StringComparison.Ordinal));
        Assert.Contains("write != null && confirm != null", card);
    }

    [Fact]
    public void Nothing_but_the_card_calls_the_write_service()
    {
        var hits = Files("src", "*.cs")
            .Where(f => !f.EndsWith("AcerHealthCardViewModel.cs") && !f.EndsWith("AcerHealthWriteService.cs") && !f.EndsWith("AcerWrite.cs"))
            .Where(f => { var t = File.ReadAllText(f); return t.Contains("IAcerHealthWriteService") && t.Contains(".SetAsync("); })
            .Select(Path.GetFileName);
        Assert.Empty(hits);
    }

    [Fact]
    public void Service_checks_the_gate_then_records_the_attempt_and_only_then_launches_the_helper()
    {
        var service = Text("src/AcerCareLite.Core/Acer/AcerHealthWriteService.cs");
        var gate = service.IndexOf("WriteGate.Evaluate(", StringComparison.Ordinal);
        var audit = service.IndexOf("_audit.Append(requested)", StringComparison.Ordinal);
        var launch = service.IndexOf("_channel.RunSetAsync(", StringComparison.Ordinal);
        Assert.True(gate >= 0 && audit > gate && launch > audit);
        Assert.Equal(1, Count(service, "_channel.RunSetAsync("));
    }

    [Fact]
    public void Only_the_install_switch_can_turn_write_support_on()
    {
        var script = Text("scripts/Install-AcerCareLite.ps1");
        Assert.Equal(2, Count(script, "-p:AcerWriteEnabled=$WriteFlag"));
        Assert.DoesNotContain("AcerWriteEnabled=true", script);
        Assert.Contains("$WriteFlag = if ($EnableWrite) { 'true' } else { 'false' }", script);
        Assert.Contains("Read-Host", script);
        Assert.Contains("-cne 'ENABLE'", script);
    }

    [Fact]
    public void Main_app_still_has_no_administrator_manifest_and_the_write_audit_lives_in_the_unelevated_app()
    {
        Assert.DoesNotContain(Files("src/AcerCareLite.App", "*.manifest"), _ => true);
        var helperFiles = Files("src/AcerCareLite.AcerHelper", "*.cs");
        Assert.DoesNotContain(helperFiles, f => File.ReadAllText(f).Contains("acer-write-audit"));
    }
}
