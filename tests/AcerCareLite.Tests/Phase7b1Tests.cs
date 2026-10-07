using System.Text;
using System.Text.Json;
using AcerCareLite.Core.Acer;
using AcerCareLite.Core.Capabilities;
using AcerCareLite.Core.Presentation;
using AcerCareLite.Core.Utilities;
using Xunit;

namespace AcerCareLite.Tests;

/// <summary>Phase 7b-1: protocol v3, helper message reading, write classification, write gate, write service, audit log, card. All against fakes.</summary>
public class Phase7b1Tests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string Pipe = "AcerCareLite.Acer.0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private const AcerHelperOperation Set = AcerHelperOperation.SetHealthMode;
    private static readonly int[] Off = { 0, 0, 0, 0, 0 };
    private static readonly int[] On = { 1, 0, 0, 0, 0 };

    // ---------- builders ----------

    private static AcerHelperResponse Msg(AcerHelperPhase phase, AcerHelperOperation op = Set) => AcerHelperProtocol.Progress(Nonce, op, phase);

    private static AcerHelperResponse SetResult(
        AcerHelperOutcome outcome, bool enable, int[]? before = null, int[]? after = null,
        int? flBefore = 3, int? flAfter = 3, string? detail = null) => new()
    {
        Protocol = AcerHelperProtocol.Version, Nonce = Nonce, Operation = Set, Phase = AcerHelperPhase.Result,
        Outcome = outcome, RequestedEnabled = enable, Detail = detail,
        BeforeFunctionList = flBefore, BeforeStatus = before ?? Off,
        FunctionList = flAfter, FunctionStatus = after ?? Array.Empty<int>(),
        Return = new[] { 0, 0 }, SetReturn = 0, ReadbackAttempts = 2
    };

    private static AcerHelperRunResult Run(AcerHelperResponse result, params AcerHelperPhase[] progress)
    {
        var messages = progress.Select(p => p == AcerHelperPhase.PreReadOk
            ? Msg(p) with { BeforeFunctionList = result.BeforeFunctionList, BeforeStatus = result.BeforeStatus }
            : Msg(p)).ToList();
        messages.Add(result);
        return AcerHelperRunResult.Responded(result, messages);
    }

    private static readonly AcerHelperPhase[] Full = { AcerHelperPhase.Started, AcerHelperPhase.PreReadOk, AcerHelperPhase.WriteSent };
    private static readonly AcerHelperPhase[] NoWrite = { AcerHelperPhase.Started, AcerHelperPhase.PreReadOk };

    private static AcerWriteResult Classify(AcerHelperRunResult run, bool enable = true) => HealthWriteClassifier.Classify(run, enable, Now);

    // ---------- protocol v3: arguments ----------

    [Fact]
    public void Arguments_round_trip_for_read_and_set()
    {
        var read = AcerHelperProtocol.BuildArguments(AcerHelperOperation.ReadHealthStatus, null, Pipe, Nonce);
        Assert.Equal(new[] { "--op", "read", "--pipe", Pipe, "--nonce", Nonce }, read);
        Assert.True(AcerHelperProtocol.TryParseArguments(read, out var op, out _, out var pipe, out var nonce));
        Assert.Equal(AcerHelperOperation.ReadHealthStatus, op);
        Assert.Equal(Pipe, pipe);
        Assert.Equal(Nonce, nonce);

        foreach (var enable in new[] { true, false })
        {
            var args = AcerHelperProtocol.BuildArguments(Set, enable, Pipe, Nonce);
            Assert.Equal(new[] { "--op", "set", "--state", enable ? "on" : "off", "--pipe", Pipe, "--nonce", Nonce }, args);
            Assert.True(AcerHelperProtocol.TryParseArguments(args, out op, out var parsedEnable, out _, out _));
            Assert.Equal(Set, op);
            Assert.Equal(enable, parsedEnable);
        }
    }

    [Fact]
    public void Set_without_a_target_state_cannot_be_built()
    {
        Assert.Throws<ArgumentException>(() => AcerHelperProtocol.BuildArguments(Set, null, Pipe, Nonce));
    }

    public static IEnumerable<object[]> BadArguments()
    {
        yield return new object[] { new string[0] };
        yield return new object[] { new[] { "--op", "set", "--pipe", Pipe, "--nonce", Nonce } };                                            // set without --state
        yield return new object[] { new[] { "--op", "set", "--state", "ON", "--pipe", Pipe, "--nonce", Nonce } };                           // wrong case
        yield return new object[] { new[] { "--op", "set", "--state", "1", "--pipe", Pipe, "--nonce", Nonce } };
        yield return new object[] { new[] { "--op", "set", "--state", "toggle", "--pipe", Pipe, "--nonce", Nonce } };
        yield return new object[] { new[] { "--op", "set", "--state", "on", "--nonce", Nonce, "--pipe", Pipe } };                           // wrong order
        yield return new object[] { new[] { "--op", "read", "--state", "on", "--pipe", Pipe, "--nonce", Nonce } };                          // read with a state
        yield return new object[] { new[] { "--op", "read", "--pipe", Pipe, "--nonce", Nonce, "--extra", "x" } };
        yield return new object[] { new[] { "--op", "write", "--pipe", Pipe, "--nonce", Nonce } };
        yield return new object[] { new[] { "--op", "read", "--pipe", @"..\evil", "--nonce", Nonce } };
        yield return new object[] { new[] { "--op", "set", "--state", "on", "--pipe", Pipe, "--nonce", "xyz" } };
        yield return new object[] { new[] { "--op", "set", "--state", "on", "--pipe", Pipe, "--nonce", Nonce, "--mask", "2" } };            // no way to pass a mask
        yield return new object[] { new[] { "--op", "read", "--pipe", Pipe } };
    }

    [Theory]
    [MemberData(nameof(BadArguments))]
    public void Bad_arguments_are_rejected(string[] args)
    {
        Assert.False(AcerHelperProtocol.TryParseArguments(args, out var op, out var enable, out var pipe, out var nonce));
        Assert.Equal("", pipe);
        Assert.Equal("", nonce);
        Assert.False(enable);
        Assert.Equal(default(AcerHelperOperation), op);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.False(AcerHelperProtocol.TryParseArguments(null, out _, out _, out _, out _));
    }

    // ---------- protocol v3: messages ----------

    [Fact]
    public void Pre_read_message_may_carry_only_the_before_state()
    {
        var ok = Msg(AcerHelperPhase.PreReadOk) with { BeforeFunctionList = 3, BeforeStatus = On };
        Assert.True(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(ok), Nonce, out var parsed, out var error), error);
        Assert.Equal(On, parsed!.BeforeStatus);

        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(ok with { FunctionStatus = On }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(ok with { Detail = "x" }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(ok with { RequestedEnabled = true }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(ok with { SetReturn = 0 }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(ok with { ReadbackAttempts = 1 }), Nonce, out _, out _));
    }

    [Fact]
    public void Pre_read_message_belongs_to_the_set_operation_only()
    {
        var msg = Msg(AcerHelperPhase.PreReadOk, AcerHelperOperation.ReadHealthStatus);
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(msg), Nonce, out _, out _));
    }

    [Fact]
    public void Write_sent_and_started_messages_carry_nothing()
    {
        foreach (var phase in new[] { AcerHelperPhase.Started, AcerHelperPhase.WriteSent })
        {
            Assert.True(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Msg(phase)), Nonce, out _, out _));
            Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Msg(phase) with { BeforeStatus = On }), Nonce, out _, out _));
            Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Msg(phase) with { BeforeFunctionList = 3 }), Nonce, out _, out _));
            Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Msg(phase) with { RequestedEnabled = false }), Nonce, out _, out _));
            Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Msg(phase) with { FunctionList = 3 }), Nonce, out _, out _));
        }
    }

    [Fact]
    public void A_read_result_may_not_carry_write_fields()
    {
        var read = new AcerHelperResponse
        {
            Protocol = AcerHelperProtocol.Version, Nonce = Nonce, Operation = AcerHelperOperation.ReadHealthStatus,
            Phase = AcerHelperPhase.Result, Outcome = AcerHelperOutcome.Ok, FunctionList = 3, FunctionStatus = On
        };
        Assert.True(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(read), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(read with { RequestedEnabled = true }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(read with { BeforeStatus = On }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(read with { SetReturn = 1 }), Nonce, out _, out _));
    }

    [Fact]
    public void Set_result_round_trips_with_all_write_fields()
    {
        var result = SetResult(AcerHelperOutcome.Ok, true, Off, On);
        Assert.True(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(result), Nonce, out var parsed, out var error), error);
        Assert.Equal(true, parsed!.RequestedEnabled);
        Assert.Equal(Off, parsed.BeforeStatus);
        Assert.Equal(On, parsed.FunctionStatus);
        Assert.Equal(0, parsed.SetReturn);
        Assert.Equal(2, parsed.ReadbackAttempts);
    }

    [Theory]
    [InlineData(AcerHelperOutcome.NoChangeNeeded)]
    [InlineData(AcerHelperOutcome.PreconditionFailed)]
    [InlineData(AcerHelperOutcome.WriteNotPermitted)]
    [InlineData(AcerHelperOutcome.SetterError)]
    public void New_outcomes_parse(AcerHelperOutcome outcome)
    {
        Assert.True(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(SetResult(outcome, true, Off, On)), Nonce, out _, out _));
    }

    [Fact]
    public void Write_field_ranges_are_enforced()
    {
        var good = SetResult(AcerHelperOutcome.Ok, true, Off, On);
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(good with { SetReturn = 70000 }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(good with { SetReservedOut = -1 }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(good with { ReadbackAttempts = 101 }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(good with { BeforeFunctionList = 300 }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(good with { BeforeStatus = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 } }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(good with { BeforeStatus = new[] { 256 } }), Nonce, out _, out _));
    }

    // ---------- message sequence for set ----------

    private static bool Accepts(AcerHelperOperation op, params AcerHelperPhase[] phases)
    {
        var seq = new HelperMessageSequence(op);
        foreach (var phase in phases)
            if (!seq.Accept(Msg(phase, op), out _)) return false;
        return true;
    }

    [Fact]
    public void Set_sequence_accepts_the_three_valid_shapes()
    {
        Assert.True(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.PreReadOk, AcerHelperPhase.WriteSent, AcerHelperPhase.Result));
        Assert.True(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.PreReadOk, AcerHelperPhase.Result));
        Assert.True(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.Result));
    }

    [Fact]
    public void Set_sequence_rejects_every_other_order()
    {
        Assert.False(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.WriteSent));                                       // no pre-read
        Assert.False(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.WriteSent, AcerHelperPhase.Result));
        Assert.False(Accepts(Set, AcerHelperPhase.PreReadOk));
        Assert.False(Accepts(Set, AcerHelperPhase.WriteSent));
        Assert.False(Accepts(Set, AcerHelperPhase.Result));
        Assert.False(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.PreReadOk, AcerHelperPhase.PreReadOk));
        Assert.False(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.PreReadOk, AcerHelperPhase.WriteSent, AcerHelperPhase.WriteSent));
        Assert.False(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.PreReadOk, AcerHelperPhase.Result, AcerHelperPhase.WriteSent));
        Assert.False(Accepts(Set, AcerHelperPhase.Started, AcerHelperPhase.PreReadOk, AcerHelperPhase.WriteSent, AcerHelperPhase.Result, AcerHelperPhase.Result));
    }

    [Fact]
    public void Read_sequence_rejects_write_phases()
    {
        var read = AcerHelperOperation.ReadHealthStatus;
        Assert.True(Accepts(read, AcerHelperPhase.Started, AcerHelperPhase.Result));
        Assert.False(Accepts(read, AcerHelperPhase.Started, AcerHelperPhase.PreReadOk));
        Assert.False(Accepts(read, AcerHelperPhase.Started, AcerHelperPhase.WriteSent));
    }

    // ---------- helper message reader ----------

    private static MemoryStream Lines(params AcerHelperResponse[] messages)
    {
        var sb = new StringBuilder();
        foreach (var m in messages) sb.Append(AcerHelperProtocol.Serialize(m)).Append('\n');
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static Task<AcerHelperRunResult> ReadAsync(Stream s, AcerHelperOperation op = Set, int timeoutMs = 5000, CancellationToken ct = default) =>
        HelperMessageReader.ReadAsync(s, Nonce, op, TimeSpan.FromMilliseconds(timeoutMs), ct);

    [Fact]
    public async Task Reader_returns_the_result_and_every_message()
    {
        var result = SetResult(AcerHelperOutcome.Ok, true, Off, On);
        var run = await ReadAsync(Lines(Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk), Msg(AcerHelperPhase.WriteSent), result));
        Assert.Equal(AcerHelperRunKind.Response, run.Kind);
        Assert.Equal(4, run.Messages.Count);
        Assert.Equal(AcerHelperPhase.Result, run.LastPhase);
        Assert.Equal(AcerHelperOutcome.Ok, run.Response!.Outcome);
    }

    [Fact]
    public async Task Reader_reads_a_read_conversation()
    {
        var result = new AcerHelperResponse
        {
            Protocol = AcerHelperProtocol.Version, Nonce = Nonce, Operation = AcerHelperOperation.ReadHealthStatus,
            Phase = AcerHelperPhase.Result, Outcome = AcerHelperOutcome.Ok, FunctionList = 3, FunctionStatus = On
        };
        var run = await ReadAsync(Lines(Msg(AcerHelperPhase.Started, AcerHelperOperation.ReadHealthStatus), result), AcerHelperOperation.ReadHealthStatus);
        Assert.Equal(AcerHelperRunKind.Response, run.Kind);
    }

    [Fact]
    public async Task Reader_detects_a_helper_that_died_before_saying_anything()
    {
        var run = await ReadAsync(new MemoryStream());
        Assert.Equal(AcerHelperRunKind.HelperIncomplete, run.Kind);
        Assert.Empty(run.Messages);
        Assert.Null(run.LastPhase);
    }

    [Theory]
    [InlineData(1, AcerHelperPhase.Started)]
    [InlineData(2, AcerHelperPhase.PreReadOk)]
    [InlineData(3, AcerHelperPhase.WriteSent)]
    public async Task Reader_reports_the_last_phase_seen_when_the_helper_dies(int count, AcerHelperPhase expectedLast)
    {
        var all = new[] { Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk), Msg(AcerHelperPhase.WriteSent) };
        var run = await ReadAsync(Lines(all.Take(count).ToArray()));
        Assert.Equal(AcerHelperRunKind.HelperIncomplete, run.Kind);
        Assert.Equal(count, run.Messages.Count);
        Assert.Equal(expectedLast, run.LastPhase);
    }

    [Fact]
    public async Task Reader_treats_a_half_written_line_as_incomplete()
    {
        var full = AcerHelperProtocol.Serialize(Msg(AcerHelperPhase.Started)) + "\n" + AcerHelperProtocol.Serialize(Msg(AcerHelperPhase.PreReadOk));
        var run = await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(full)));
        Assert.Equal(AcerHelperRunKind.HelperIncomplete, run.Kind);
        Assert.Equal(AcerHelperPhase.Started, run.LastPhase);
    }

    [Fact]
    public async Task Reader_rejects_garbage_wrong_nonce_and_bad_order()
    {
        var garbage = await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("not json\n")));
        Assert.Equal(AcerHelperRunKind.InvalidResponse, garbage.Kind);

        var wrongNonce = await ReadAsync(Lines(AcerHelperProtocol.Progress("ffffffffffffffffffffffffffffffff", Set, AcerHelperPhase.Started)));
        Assert.Equal(AcerHelperRunKind.InvalidResponse, wrongNonce.Kind);

        var resultFirst = await ReadAsync(Lines(SetResult(AcerHelperOutcome.Ok, true, Off, On)));
        Assert.Equal(AcerHelperRunKind.InvalidResponse, resultFirst.Kind);

        var skipped = await ReadAsync(Lines(Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.WriteSent)));
        Assert.Equal(AcerHelperRunKind.InvalidResponse, skipped.Kind);
        Assert.Single(skipped.Messages);
    }

    [Fact]
    public async Task Reader_keeps_the_messages_seen_before_an_invalid_one()
    {
        var text = AcerHelperProtocol.Serialize(Msg(AcerHelperPhase.Started)) + "\n"
                   + AcerHelperProtocol.Serialize(Msg(AcerHelperPhase.PreReadOk)) + "\n"
                   + AcerHelperProtocol.Serialize(Msg(AcerHelperPhase.WriteSent)) + "\n"
                   + "garbage\n";
        var run = await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(AcerHelperRunKind.InvalidResponse, run.Kind);
        Assert.Equal(AcerHelperPhase.WriteSent, run.LastPhase);
    }

    [Fact]
    public async Task Reader_rejects_an_oversized_line()
    {
        var run = await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', AcerHelperProtocol.MaxLineBytes + 10))));
        Assert.Equal(AcerHelperRunKind.InvalidResponse, run.Kind);
    }

    [Fact]
    public async Task Reader_rejects_data_after_the_result()
    {
        var result = SetResult(AcerHelperOutcome.NoChangeNeeded, true, On, On);
        var run = await ReadAsync(Lines(Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk), result, Msg(AcerHelperPhase.Started)));
        Assert.Equal(AcerHelperRunKind.InvalidResponse, run.Kind);
    }

    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("pipe broken");
    }

    [Fact]
    public async Task Reader_times_out_when_the_helper_goes_silent()
    {
        var run = await ReadAsync(new SilentStream(), timeoutMs: 150);
        Assert.Equal(AcerHelperRunKind.HelperIncomplete, run.Kind);
        Assert.Contains("No result within", run.Detail);
    }

    [Fact]
    public async Task Reader_reports_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);
        var run = await ReadAsync(new SilentStream(), timeoutMs: 10_000, ct: cts.Token);
        Assert.Equal(AcerHelperRunKind.Cancelled, run.Kind);
    }

    [Fact]
    public async Task Reader_treats_a_broken_pipe_as_incomplete()
    {
        var run = await ReadAsync(new BrokenStream());
        Assert.Equal(AcerHelperRunKind.HelperIncomplete, run.Kind);
    }

    // ---------- write classifier ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verified_change_is_applied(bool enable)
    {
        var before = enable ? Off : On;
        var after = enable ? On : Off;
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, enable, before, after), Full), enable);
        Assert.Equal(AcerWriteOutcome.Applied, r.Outcome);
        Assert.Equal(enable, r.VerifiedEnabled);
        Assert.False(r.LocksWrites);
        Assert.False(r.RequiresFreshRead);
        Assert.Contains("before=[", r.Evidence);
        Assert.Contains("after=[", r.Evidence);
        Assert.Equal(after, r.After);
    }

    [Fact]
    public void Setter_error_with_matching_readback_is_still_verified()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.SetterError, true, Off, On, detail: "ManagementException: x"), Full));
        Assert.Equal(AcerWriteOutcome.Applied, r.Outcome);
        Assert.Contains("reported an error", r.Summary);
    }

    [Fact]
    public void Setter_error_with_unchanged_readback_is_failed()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.SetterError, true, Off, Off, detail: "x"), Full));
        Assert.Equal(AcerWriteOutcome.Failed, r.Outcome);
    }

    [Fact]
    public void Unchanged_readback_is_failed_and_requires_a_fresh_read()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, Off), Full));
        Assert.Equal(AcerWriteOutcome.Failed, r.Outcome);
        Assert.True(r.RequiresFreshRead);
        Assert.False(r.LocksWrites);
        Assert.Null(r.VerifiedEnabled);
    }

    [Theory]
    [InlineData(new[] { 1, 1, 0, 0, 0 })]   // calibration-position byte changed alongside
    [InlineData(new[] { 1, 0, 1, 0, 0 })]
    [InlineData(new[] { 1, 0, 0, 0, 7 })]
    [InlineData(new[] { 0, 1, 0, 0, 0 })]   // target not reached and another byte changed
    public void Any_other_byte_changing_is_an_anomaly_that_locks_writes(int[] after)
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, after), Full));
        Assert.Equal(AcerWriteOutcome.Anomaly, r.Outcome);
        Assert.True(r.LocksWrites);
        Assert.True(r.RequiresFreshRead);
        Assert.Null(r.VerifiedEnabled);
    }

    [Fact]
    public void Function_list_change_is_an_anomaly()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, On, flBefore: 3, flAfter: 1), Full));
        Assert.Equal(AcerWriteOutcome.Anomaly, r.Outcome);
        Assert.True(r.LocksWrites);
    }

    [Fact]
    public void Status_array_length_change_is_an_anomaly()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, new[] { 1, 0, 0, 0 }), Full));
        Assert.Equal(AcerWriteOutcome.Anomaly, r.Outcome);
    }

    [Fact]
    public void Unexpected_health_byte_value_is_an_anomaly()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, new[] { 2, 0, 0, 0, 0 }), Full));
        Assert.Equal(AcerWriteOutcome.Anomaly, r.Outcome);
    }

    [Fact]
    public void Already_in_target_state_before_the_command_yet_the_setter_ran_is_an_anomaly()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, On, On), Full));
        Assert.Equal(AcerWriteOutcome.Anomaly, r.Outcome);
    }

    [Fact]
    public void No_readback_after_the_command_is_unknown_never_not_applied()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, Array.Empty<int>(), flAfter: null), Full));
        Assert.Equal(AcerWriteOutcome.Unknown, r.Outcome);
        Assert.True(r.RequiresFreshRead);
    }

    [Fact]
    public void Missing_before_state_after_a_command_is_unknown()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, new int[0], On, flBefore: null), Full));
        Assert.Equal(AcerWriteOutcome.Unknown, r.Outcome);
    }

    [Fact]
    public void Already_in_the_requested_state_is_no_change()
    {
        var r = Classify(Run(SetResult(AcerHelperOutcome.NoChangeNeeded, true, On, On), NoWrite));
        Assert.Equal(AcerWriteOutcome.NoChange, r.Outcome);
        Assert.Equal(true, r.VerifiedEnabled);
        Assert.False(r.RequiresFreshRead);
    }

    [Theory]
    [InlineData(AcerHelperOutcome.PreconditionFailed)]
    [InlineData(AcerHelperOutcome.WriteNotPermitted)]
    [InlineData(AcerHelperOutcome.NotElevated)]
    [InlineData(AcerHelperOutcome.AccessDenied)]
    [InlineData(AcerHelperOutcome.InterfaceMissing)]
    [InlineData(AcerHelperOutcome.NoInstance)]
    [InlineData(AcerHelperOutcome.SchemaMismatch)]
    [InlineData(AcerHelperOutcome.WmiError)]
    [InlineData(AcerHelperOutcome.Unexpected)]
    public void Refusals_before_the_write_marker_are_certainly_not_applied(AcerHelperOutcome outcome)
    {
        var r = Classify(Run(SetResult(outcome, true, Off, Array.Empty<int>(), detail: "why"), AcerHelperPhase.Started));
        Assert.Equal(AcerWriteOutcome.NotApplied, r.Outcome);
        Assert.Contains("Nothing was changed", r.Summary);
        Assert.False(r.RequiresFreshRead);
        Assert.False(r.LocksWrites);
    }

    [Theory]
    [InlineData(AcerHelperOutcome.PreconditionFailed)]
    [InlineData(AcerHelperOutcome.WmiError)]
    [InlineData(AcerHelperOutcome.Unexpected)]
    [InlineData(AcerHelperOutcome.AccessDenied)]
    public void The_same_failures_after_the_write_marker_are_unknown(AcerHelperOutcome outcome)
    {
        var r = Classify(Run(SetResult(outcome, true, Off, Array.Empty<int>(), detail: "why"), Full));
        Assert.Equal(AcerWriteOutcome.Unknown, r.Outcome);
        Assert.True(r.RequiresFreshRead);
    }

    [Fact]
    public void Reply_that_does_not_match_the_request_is_not_trusted()
    {
        var wrongTarget = Classify(Run(SetResult(AcerHelperOutcome.Ok, false, Off, On), Full), enable: true);
        Assert.Equal(AcerWriteOutcome.Unknown, wrongTarget.Outcome);

        var noEcho = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, On) with { RequestedEnabled = null }, Full));
        Assert.Equal(AcerWriteOutcome.Unknown, noEcho.Outcome);

        var wrongOp = Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, On) with { Operation = AcerHelperOperation.ReadHealthStatus }, AcerHelperPhase.Started));
        Assert.Equal(AcerWriteOutcome.NotApplied, wrongOp.Outcome);
    }

    [Fact]
    public void Run_failures_are_not_applied_when_no_write_marker_was_seen()
    {
        Assert.Equal(AcerWriteOutcome.NotApplied, Classify(AcerHelperRunResult.Declined()).Outcome);
        Assert.Equal(AcerWriteOutcome.NotApplied, Classify(AcerHelperRunResult.LaunchFailed("missing")).Outcome);
        Assert.Equal(AcerWriteOutcome.NotApplied, Classify(AcerHelperRunResult.TimedOut("no connection")).Outcome);
        Assert.Equal(AcerWriteOutcome.NotApplied, Classify(AcerHelperRunResult.Cancelled()).Outcome);
        Assert.Equal(AcerWriteOutcome.NotApplied, Classify(AcerHelperRunResult.HelperIncomplete("died", new[] { Msg(AcerHelperPhase.Started) })).Outcome);
        Assert.Equal(AcerWriteOutcome.NotApplied, Classify(AcerHelperRunResult.InvalidResponse("bad", new[] { Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk) })).Outcome);
    }

    [Fact]
    public void Run_failures_after_the_write_marker_are_unknown()
    {
        var seen = new[] { Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk) with { BeforeFunctionList = 3, BeforeStatus = Off }, Msg(AcerHelperPhase.WriteSent) };

        foreach (var run in new[]
        {
            AcerHelperRunResult.HelperIncomplete("died", seen),
            AcerHelperRunResult.InvalidResponse("bad", seen),
            AcerHelperRunResult.TimedOut("slow", seen),
            AcerHelperRunResult.Cancelled(seen)
        })
        {
            var r = Classify(run);
            Assert.Equal(AcerWriteOutcome.Unknown, r.Outcome);
            Assert.True(r.RequiresFreshRead);
            Assert.Equal(AcerHelperPhase.WriteSent, r.LastPhase);
            Assert.Equal(Off, r.Before);
            Assert.Contains("may have changed", r.Summary);
        }
    }

    [Fact]
    public void An_unexpected_peer_locks_writes()
    {
        var r = Classify(AcerHelperRunResult.PeerRejected("pid 4: C:\\evil.exe"));
        Assert.Equal(AcerWriteOutcome.Untrusted, r.Outcome);
        Assert.True(r.LocksWrites);
        Assert.True(r.RequiresFreshRead);
    }

    [Fact]
    public void A_response_run_without_a_response_is_not_applied()
    {
        var r = Classify(new AcerHelperRunResult(AcerHelperRunKind.Response, null));
        Assert.Equal(AcerWriteOutcome.NotApplied, r.Outcome);
    }

    // ---------- write gate ----------

    private static AcerBatteryHealthResult ReadResult(DateTimeOffset at, bool enabled = false)
    {
        var response = new AcerHelperResponse
        {
            Protocol = AcerHelperProtocol.Version, Nonce = Nonce, Operation = AcerHelperOperation.ReadHealthStatus,
            Phase = AcerHelperPhase.Result, Outcome = AcerHelperOutcome.Ok, FunctionList = 3, FunctionStatus = enabled ? On : Off, Return = new[] { 0, 0 }
        };
        return AcerHealthClassifier.Classify(AcerHelperRunResult.Responded(response), at);
    }

    private static readonly HelperLocationStatus Protected = HelperLocationStatus.Protected("ok");
    private static readonly HelperLocationStatus Unprotected = HelperLocationStatus.NotProtected("dev build");

    private static WriteGateInput Input(
        bool compiled = true, HelperLocationStatus? location = null, AcerBatteryHealthResult? read = null, bool useNullRead = false,
        bool locked = false, DateTimeOffset? lastAttempt = null, DateTimeOffset? problemAt = null) =>
        new(compiled, location ?? Protected, useNullRead ? null : read ?? ReadResult(Now.AddMinutes(-1)), locked, lastAttempt, problemAt, Now);

    [Fact]
    public void Gate_allows_when_every_condition_holds()
    {
        var d = WriteGate.Evaluate(Input());
        Assert.True(d.Allowed, d.Reason);
    }

    [Fact]
    public void Gate_refuses_when_write_support_is_not_compiled_in()
    {
        var d = WriteGate.Evaluate(Input(compiled: false));
        Assert.False(d.Allowed);
        Assert.Contains("not enabled in this build", d.Reason);
    }

    [Fact]
    public void Gate_refuses_an_unprotected_location()
    {
        var d = WriteGate.Evaluate(Input(location: Unprotected));
        Assert.False(d.Allowed);
        Assert.Contains("dev build", d.Reason);
    }

    [Fact]
    public void Gate_refuses_when_locked()
    {
        Assert.False(WriteGate.Evaluate(Input(locked: true)).Allowed);
    }

    [Fact]
    public void Gate_refuses_without_a_supported_read()
    {
        Assert.False(WriteGate.Evaluate(Input(useNullRead: true)).Allowed);

        var unsupported = AcerHealthClassifier.Classify(AcerHelperRunResult.Responded(new AcerHelperResponse
        {
            Protocol = AcerHelperProtocol.Version, Nonce = Nonce, Operation = AcerHelperOperation.ReadHealthStatus,
            Phase = AcerHelperPhase.Result, Outcome = AcerHelperOutcome.InterfaceMissing
        }), Now);
        Assert.False(WriteGate.Evaluate(Input(read: unsupported)).Allowed);

        var declined = AcerHealthClassifier.Classify(AcerHelperRunResult.Declined(), Now);
        Assert.False(WriteGate.Evaluate(Input(read: declined)).Allowed);

        Assert.False(WriteGate.Evaluate(Input(read: AcerBatteryHealthResult.NotChecked)).Allowed);
    }

    [Fact]
    public void Gate_requires_a_read_newer_than_the_last_problem()
    {
        var readAt = Now.AddMinutes(-1);
        Assert.False(WriteGate.Evaluate(Input(read: ReadResult(readAt), problemAt: readAt.AddSeconds(1))).Allowed);
        Assert.False(WriteGate.Evaluate(Input(read: ReadResult(readAt), problemAt: readAt)).Allowed);
        Assert.True(WriteGate.Evaluate(Input(read: ReadResult(readAt), problemAt: readAt.AddSeconds(-1))).Allowed);
    }

    [Fact]
    public void Gate_enforces_the_ten_second_interval()
    {
        var tooSoon = WriteGate.Evaluate(Input(lastAttempt: Now.AddSeconds(-4)));
        Assert.False(tooSoon.Allowed);
        Assert.Contains("6 s", tooSoon.Reason);
        Assert.False(WriteGate.Evaluate(Input(lastAttempt: Now.AddSeconds(-9.9))).Allowed);
        Assert.True(WriteGate.Evaluate(Input(lastAttempt: Now.AddSeconds(-10))).Allowed);
        Assert.True(WriteGate.Evaluate(Input(lastAttempt: Now.AddMinutes(-5))).Allowed);
    }

    [Fact]
    public void Gate_checks_the_build_first()
    {
        var d = WriteGate.Evaluate(Input(compiled: false, location: Unprotected, locked: true, useNullRead: true));
        Assert.Contains("not enabled in this build", d.Reason);
    }

    // ---------- write service ----------

    private sealed class FakeWriteChannel : IAcerHelperWriteChannel
    {
        public List<bool> Calls { get; } = new();
        public Func<bool, Task<AcerHelperRunResult>> Handler { get; set; } = _ => Task.FromResult(AcerHelperRunResult.Declined());
        public Task<AcerHelperRunResult> RunSetAsync(bool enable, CancellationToken ct)
        {
            Calls.Add(enable);
            return Handler(enable);
        }
    }

    private sealed class FakeReads : IAcerBatteryHealthService
    {
        public AcerBatteryHealthResult? Last { get; set; }
        public Task<AcerBatteryHealthResult> CheckAsync(CancellationToken ct = default) => Task.FromResult(Last ?? AcerBatteryHealthResult.NotChecked);
    }

    private sealed class FakeLocation : IHelperLocationGate
    {
        public HelperLocationStatus Current { get; set; } = Protected;
    }

    private sealed class FakeAudit : IAcerWriteAuditLog
    {
        public List<AcerWriteAuditEntry> Entries { get; } = new();
        public bool Fail { get; set; }
        public bool Append(AcerWriteAuditEntry entry)
        {
            if (Fail) return false;
            Entries.Add(entry);
            return true;
        }
    }

    private sealed class Rig
    {
        public FakeWriteChannel Channel { get; } = new();
        public FakeReads Reads { get; } = new() { Last = ReadResult(Now.AddMinutes(-1)) };
        public FakeLocation Location { get; } = new();
        public FakeAudit Audit { get; } = new();
        public DateTimeOffset Time { get; set; } = Now;
        public AcerHealthWriteService Service { get; }

        public Rig(bool compiled = true)
        {
            Service = new AcerHealthWriteService(Channel, Reads, Location, Audit, compiled) { Clock = () => Time };
            Channel.Handler = enable => Task.FromResult(Run(
                SetResult(AcerHelperOutcome.Ok, enable, enable ? Off : On, enable ? On : Off), Full));
        }
    }

    [Fact]
    public async Task Service_runs_the_helper_once_and_audits_before_and_after()
    {
        var rig = new Rig();
        var result = await rig.Service.SetAsync(true);

        Assert.Equal(AcerWriteOutcome.Applied, result.Outcome);
        Assert.Equal(new[] { true }, rig.Channel.Calls);
        Assert.Same(result, rig.Service.Last);
        Assert.Equal(new[] { "requested", "completed" }, rig.Audit.Entries.Select(e => e.Kind).ToArray());
        Assert.Equal(rig.Audit.Entries[0].AttemptId, rig.Audit.Entries[1].AttemptId);
        Assert.Equal("Applied", rig.Audit.Entries[1].Outcome);
        Assert.Equal(Off, rig.Audit.Entries[1].Before);
        Assert.Equal(On, rig.Audit.Entries[1].After);
    }

    [Fact]
    public async Task Service_never_launches_anything_in_a_build_without_write_support()
    {
        var rig = new Rig(compiled: false);
        var result = await rig.Service.SetAsync(true);
        Assert.Equal(AcerWriteOutcome.Blocked, result.Outcome);
        Assert.Empty(rig.Channel.Calls);
        Assert.Contains("not enabled in this build", result.Summary);
        Assert.Equal("blocked", rig.Audit.Entries.Single().Kind);
    }

    [Fact]
    public async Task Service_refuses_an_unprotected_location()
    {
        var rig = new Rig();
        rig.Location.Current = Unprotected;
        var result = await rig.Service.SetAsync(false);
        Assert.Equal(AcerWriteOutcome.Blocked, result.Outcome);
        Assert.Empty(rig.Channel.Calls);
    }

    [Fact]
    public async Task Service_changes_nothing_when_the_audit_log_cannot_be_written()
    {
        var rig = new Rig();
        rig.Audit.Fail = true;
        var result = await rig.Service.SetAsync(true);
        Assert.Equal(AcerWriteOutcome.Blocked, result.Outcome);
        Assert.Contains("audit log", result.Summary);
        Assert.Empty(rig.Channel.Calls);
    }

    [Fact]
    public async Task Service_refuses_without_a_read_first()
    {
        var rig = new Rig();
        rig.Reads.Last = null;
        Assert.Equal(AcerWriteOutcome.Blocked, (await rig.Service.SetAsync(true)).Outcome);
        Assert.Empty(rig.Channel.Calls);
    }

    [Fact]
    public async Task Service_locks_for_the_session_after_an_anomaly()
    {
        var rig = new Rig();
        rig.Channel.Handler = enable => Task.FromResult(Run(SetResult(AcerHelperOutcome.Ok, enable, Off, new[] { 1, 1, 0, 0, 0 }), Full));

        var first = await rig.Service.SetAsync(true);
        Assert.Equal(AcerWriteOutcome.Anomaly, first.Outcome);
        Assert.True(rig.Service.IsLocked);

        // Even with a fresh read and plenty of time, the lock holds.
        rig.Time = Now.AddHours(1);
        rig.Reads.Last = ReadResult(rig.Time);
        var second = await rig.Service.SetAsync(true);
        Assert.Equal(AcerWriteOutcome.Blocked, second.Outcome);
        Assert.Single(rig.Channel.Calls);
    }

    [Fact]
    public async Task Service_locks_after_an_untrusted_peer()
    {
        var rig = new Rig();
        rig.Channel.Handler = _ => Task.FromResult(AcerHelperRunResult.PeerRejected("pid 9"));
        Assert.Equal(AcerWriteOutcome.Untrusted, (await rig.Service.SetAsync(true)).Outcome);
        Assert.True(rig.Service.IsLocked);
    }

    [Fact]
    public async Task Service_needs_a_fresh_read_after_an_unknown_result()
    {
        var rig = new Rig();
        var seen = new[] { Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk) with { BeforeFunctionList = 3, BeforeStatus = Off }, Msg(AcerHelperPhase.WriteSent) };
        rig.Channel.Handler = _ => Task.FromResult(AcerHelperRunResult.HelperIncomplete("died", seen));

        Assert.Equal(AcerWriteOutcome.Unknown, (await rig.Service.SetAsync(true)).Outcome);
        Assert.False(rig.Service.IsLocked);

        // Time passes, but the last read is older than the problem: refused.
        rig.Time = Now.AddMinutes(5);
        var refused = await rig.Service.SetAsync(true);
        Assert.Equal(AcerWriteOutcome.Blocked, refused.Outcome);
        Assert.Contains("Read the current state again", refused.Summary);

        // A fresh successful read unblocks it.
        rig.Reads.Last = ReadResult(rig.Time.AddSeconds(1));
        rig.Time = rig.Time.AddSeconds(2);
        rig.Channel.Handler = enable => Task.FromResult(Run(SetResult(AcerHelperOutcome.Ok, enable, Off, On), Full));
        Assert.Equal(AcerWriteOutcome.Applied, (await rig.Service.SetAsync(true)).Outcome);
    }

    [Fact]
    public async Task Service_enforces_the_interval_between_attempts()
    {
        var rig = new Rig();
        Assert.Equal(AcerWriteOutcome.Applied, (await rig.Service.SetAsync(true)).Outcome);

        rig.Time = Now.AddSeconds(3);
        var tooSoon = await rig.Service.SetAsync(false);
        Assert.Equal(AcerWriteOutcome.Blocked, tooSoon.Outcome);
        Assert.Single(rig.Channel.Calls);

        rig.Time = Now.AddSeconds(11);
        Assert.Equal(AcerWriteOutcome.Applied, (await rig.Service.SetAsync(false)).Outcome);
        Assert.Equal(new[] { true, false }, rig.Channel.Calls.ToArray());
    }

    [Fact]
    public async Task Service_blocks_a_second_request_while_one_is_running()
    {
        var rig = new Rig();
        var gate = new TaskCompletionSource<AcerHelperRunResult>();
        rig.Channel.Handler = _ => gate.Task;

        var first = rig.Service.SetAsync(true);
        // Let the first request reach the channel.
        for (var i = 0; i < 100 && rig.Channel.Calls.Count == 0; i++) await Task.Delay(10);

        var second = await rig.Service.SetAsync(false);
        Assert.Equal(AcerWriteOutcome.Blocked, second.Outcome);
        Assert.Contains("already in progress", second.Summary);

        gate.SetResult(Run(SetResult(AcerHelperOutcome.Ok, true, Off, On), Full));
        Assert.Equal(AcerWriteOutcome.Applied, (await first).Outcome);
        Assert.Single(rig.Channel.Calls);
    }

    [Fact]
    public async Task Service_turns_channel_exceptions_into_results()
    {
        var rig = new Rig();
        rig.Channel.Handler = _ => throw new InvalidOperationException("boom");
        var r = await rig.Service.SetAsync(true);
        Assert.Equal(AcerWriteOutcome.NotApplied, r.Outcome);

        var rig2 = new Rig();
        rig2.Channel.Handler = _ => throw new OperationCanceledException();
        Assert.Equal(AcerWriteOutcome.NotApplied, (await rig2.Service.SetAsync(true)).Outcome);
    }

    [Fact]
    public async Task Service_audits_the_last_phase_for_a_helper_that_died_after_the_write_marker()
    {
        var rig = new Rig();
        var seen = new[] { Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk) with { BeforeFunctionList = 3, BeforeStatus = Off }, Msg(AcerHelperPhase.WriteSent) };
        rig.Channel.Handler = _ => Task.FromResult(AcerHelperRunResult.HelperIncomplete("died", seen));
        await rig.Service.SetAsync(true);

        var completed = rig.Audit.Entries.Last();
        Assert.Equal("completed", completed.Kind);
        Assert.Equal("Unknown", completed.Outcome);
        Assert.Equal("WriteSent", completed.LastPhase);
    }

    // ---------- audit log file ----------

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AcerCareLiteTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static AcerWriteAuditEntry Entry(string kind = "requested") =>
        new(Now, "abc123", kind, true, "Applied", "Result", Off, On, "detail");

    [Fact]
    public void Audit_log_appends_json_lines()
    {
        var dir = TempDir();
        try
        {
            var log = new FileAcerWriteAuditLog(Path.Combine(dir, "sub", "audit.log"));
            Assert.True(log.Append(Entry("requested")));
            Assert.True(log.Append(Entry("completed")));

            var lines = File.ReadAllLines(Path.Combine(dir, "sub", "audit.log"));
            Assert.Equal(2, lines.Length);
            using var doc = JsonDocument.Parse(lines[1]);
            Assert.Equal("completed", doc.RootElement.GetProperty("Kind").GetString());
            Assert.Equal("abc123", doc.RootElement.GetProperty("AttemptId").GetString());
            Assert.Equal(5, doc.RootElement.GetProperty("After").GetArrayLength());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Audit_log_rotates_at_about_one_megabyte()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "audit.log");
            File.WriteAllText(path, new string('x', (int)FileAcerWriteAuditLog.MaxBytes + 1));
            var log = new FileAcerWriteAuditLog(path);
            Assert.True(log.Append(Entry()));

            Assert.True(File.Exists(path + ".1"));
            Assert.True(new FileInfo(path + ".1").Length > FileAcerWriteAuditLog.MaxBytes);
            Assert.True(new FileInfo(path).Length < 2000);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Audit_log_reports_failure_instead_of_throwing()
    {
        var dir = TempDir();
        try
        {
            var blocker = Path.Combine(dir, "afile");
            File.WriteAllText(blocker, "x");
            var log = new FileAcerWriteAuditLog(Path.Combine(blocker, "audit.log")); // a file where a directory is needed
            Assert.False(log.Append(Entry()));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------- card view model ----------

    private sealed class ReadService : IAcerBatteryHealthService
    {
        public AcerBatteryHealthResult? Last { get; set; }
        public AcerBatteryHealthResult Next { get; set; } = ReadResult(Now);
        public Task<AcerBatteryHealthResult> CheckAsync(CancellationToken ct = default)
        {
            Last = Next;
            return Task.FromResult(Next);
        }
    }

    private sealed class FakeWriteService : IAcerHealthWriteService
    {
        public List<bool> Calls { get; } = new();
        public AcerWriteResult? Last => null;
        public bool IsLocked => false;
        public Func<bool, Task<AcerWriteResult>> Handler { get; set; } =
            enable => Task.FromResult(Classify(Run(SetResult(AcerHelperOutcome.Ok, enable, enable ? Off : On, enable ? On : Off), Full), enable));
        public Task<AcerWriteResult> SetAsync(bool enable, CancellationToken ct = default)
        {
            Calls.Add(enable);
            return Handler(enable);
        }
    }

    private sealed class FakeConfirm : IConfirmationService
    {
        public bool Answer { get; set; } = true;
        public int Calls { get; private set; }
        public string? LastMessage { get; private set; }
        public bool Confirm(string title, string message)
        {
            Calls++;
            LastMessage = message;
            return Answer;
        }
    }

    private static AcerHealthCardViewModel Card(ReadService reads, FakeWriteService? write, FakeConfirm? confirm, bool? available = true) =>
        new(reads, null, write, confirm, available);

    [Fact]
    public void Default_build_has_no_change_button()
    {
        Assert.False(WriteBuild.Compiled); // the default build contains no write support
        var card = new AcerHealthCardViewModel(new ReadService(), null, new FakeWriteService(), new FakeConfirm());
        Assert.False(card.IsWriteAvailable);
        Assert.StartsWith("Read-only", card.FooterText);
    }

    [Fact]
    public void Change_button_needs_the_write_service_the_confirmation_and_the_flag()
    {
        Assert.False(Card(new ReadService(), null, new FakeConfirm()).IsWriteAvailable);
        Assert.False(Card(new ReadService(), new FakeWriteService(), null).IsWriteAvailable);
        Assert.False(Card(new ReadService(), new FakeWriteService(), new FakeConfirm(), available: false).IsWriteAvailable);
        Assert.True(Card(new ReadService(), new FakeWriteService(), new FakeConfirm()).IsWriteAvailable);
    }

    [Fact]
    public async Task Change_is_disabled_until_a_state_has_been_read()
    {
        var reads = new ReadService();
        var card = Card(reads, new FakeWriteService(), new FakeConfirm());
        Assert.False(card.SetCommand.CanExecute(null));

        await card.CheckCommand.ExecuteAsync(null);
        Assert.True(card.SetCommand.CanExecute(null));
        Assert.Equal("Turn health mode On…", card.SetButtonText);   // read result is Off
    }

    [Fact]
    public async Task Declining_the_confirmation_changes_nothing()
    {
        var write = new FakeWriteService();
        var confirm = new FakeConfirm { Answer = false };
        var card = Card(new ReadService(), write, confirm);
        await card.CheckCommand.ExecuteAsync(null);

        await card.SetCommand.ExecuteAsync(null);

        Assert.Equal(1, confirm.Calls);
        Assert.Empty(write.Calls);
        Assert.Equal("Cancelled", card.WriteStatusText);
        Assert.Equal("Health mode: Off", card.StatusText);
    }

    [Fact]
    public async Task Confirmation_names_the_states_and_says_calibration_is_untouched()
    {
        var confirm = new FakeConfirm { Answer = false };
        var card = Card(new ReadService(), new FakeWriteService(), confirm);
        await card.CheckCommand.ExecuteAsync(null);
        await card.SetCommand.ExecuteAsync(null);

        Assert.Contains("from Off to On", confirm.LastMessage);
        Assert.Contains("does not change battery calibration", confirm.LastMessage);
        Assert.Contains("administrator approval", confirm.LastMessage);
    }

    [Fact]
    public async Task Confirmed_change_asks_for_the_opposite_of_the_current_state_and_shows_the_verified_result()
    {
        var write = new FakeWriteService();
        var card = Card(new ReadService(), write, new FakeConfirm());
        await card.CheckCommand.ExecuteAsync(null);

        await card.SetCommand.ExecuteAsync(null);

        Assert.Equal(new[] { true }, write.Calls.ToArray());
        Assert.Equal("Applied", card.WriteStatusText);
        Assert.Equal("Health mode: On", card.StatusText);
        Assert.Contains("Verified", card.CheckedAtText);
        Assert.Equal("Turn health mode Off…", card.SetButtonText);
        Assert.Contains("before=[0,0,0,0,0]", card.WriteEvidenceText);
        Assert.False(card.IsWriting);
    }

    [Fact]
    public async Task Not_applied_leaves_the_known_state_and_the_button_alone()
    {
        var write = new FakeWriteService { Handler = e => Task.FromResult(Classify(AcerHelperRunResult.Declined(), e)) };
        var card = Card(new ReadService(), write, new FakeConfirm());
        await card.CheckCommand.ExecuteAsync(null);

        await card.SetCommand.ExecuteAsync(null);

        Assert.Equal("Not applied", card.WriteStatusText);
        Assert.Equal("Health mode: Off", card.StatusText);
        Assert.True(card.SetCommand.CanExecute(null));
    }

    [Fact]
    public async Task Unknown_result_forgets_the_state_and_disables_the_button_until_a_fresh_check()
    {
        var seen = new[] { Msg(AcerHelperPhase.Started), Msg(AcerHelperPhase.PreReadOk), Msg(AcerHelperPhase.WriteSent) };
        var write = new FakeWriteService { Handler = e => Task.FromResult(Classify(AcerHelperRunResult.HelperIncomplete("died", seen), e)) };
        var reads = new ReadService();
        var card = Card(reads, write, new FakeConfirm());
        await card.CheckCommand.ExecuteAsync(null);

        await card.SetCommand.ExecuteAsync(null);

        Assert.Equal("Result unknown", card.WriteStatusText);
        Assert.Equal("State unknown", card.StatusText);
        Assert.False(card.SetCommand.CanExecute(null));

        await card.CheckCommand.ExecuteAsync(null);
        Assert.Equal("Health mode: Off", card.StatusText);
        Assert.True(card.SetCommand.CanExecute(null));
    }

    [Fact]
    public async Task Blocked_and_anomaly_results_are_reported_plainly()
    {
        var blocked = new FakeWriteService { Handler = e => Task.FromResult(AcerWriteResult.Blocked(e, "No.", Now)) };
        var card = Card(new ReadService(), blocked, new FakeConfirm());
        await card.CheckCommand.ExecuteAsync(null);
        await card.SetCommand.ExecuteAsync(null);
        Assert.Equal("Not allowed", card.WriteStatusText);
        Assert.Contains("Nothing was changed", card.WriteDetailText);

        var anomaly = new FakeWriteService { Handler = e => Task.FromResult(Classify(Run(SetResult(AcerHelperOutcome.Ok, e, Off, new[] { 1, 1, 0, 0, 0 }), Full), e)) };
        var card2 = Card(new ReadService(), anomaly, new FakeConfirm());
        await card2.CheckCommand.ExecuteAsync(null);
        await card2.SetCommand.ExecuteAsync(null);
        Assert.Equal("Unexpected result", card2.WriteStatusText);
        Assert.Equal("State unknown", card2.StatusText);
    }

    [Fact]
    public async Task A_throwing_write_service_is_reported_as_unknown()
    {
        var write = new FakeWriteService { Handler = _ => throw new InvalidOperationException("boom") };
        var card = Card(new ReadService(), write, new FakeConfirm());
        await card.CheckCommand.ExecuteAsync(null);
        await card.SetCommand.ExecuteAsync(null);
        Assert.Equal("Result unknown", card.WriteStatusText);
        Assert.False(card.SetCommand.CanExecute(null));
        Assert.False(card.IsWriting);
    }

    [Fact]
    public async Task Buttons_are_disabled_while_a_change_is_running()
    {
        var gate = new TaskCompletionSource<AcerWriteResult>();
        var write = new FakeWriteService { Handler = _ => gate.Task };
        var card = Card(new ReadService(), write, new FakeConfirm());
        await card.CheckCommand.ExecuteAsync(null);

        var running = card.SetCommand.ExecuteAsync(null);
        Assert.True(card.IsWriting);
        Assert.False(card.SetCommand.CanExecute(null));
        Assert.False(card.CheckCommand.CanExecute(null));

        gate.SetResult(Classify(Run(SetResult(AcerHelperOutcome.Ok, true, Off, On), Full)));
        await running;
        Assert.False(card.IsWriting);
        Assert.True(card.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task Without_a_confirmation_service_the_write_service_is_never_called()
    {
        var write = new FakeWriteService();
        var card = Card(new ReadService(), write, null);
        await card.CheckCommand.ExecuteAsync(null);
        Assert.False(card.SetCommand.CanExecute(null));
        await card.SetCommand.ExecuteAsync(null);
        Assert.Empty(write.Calls);
    }
}
