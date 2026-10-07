using AcerCareLite.Core.Acer;
using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Capabilities;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Presentation;
using Xunit;

namespace AcerCareLite.Tests;

public class Phase7aTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string V2 = "{\"protocol\":3,\"nonce\":\"" + Nonce + "\",\"operation\":1,\"phase\":2,";

    private static AcerHelperResponse Ok(int? functionList = 0x03, int[]? status = null, string? instance = AcerHealthClassifier.ExpectedInstance) => new()
    {
        Protocol = AcerHelperProtocol.Version,
        Nonce = Nonce,
        Operation = AcerHelperOperation.ReadHealthStatus,
        Phase = AcerHelperPhase.Result,
        Outcome = AcerHelperOutcome.Ok,
        InstanceName = instance,
        FunctionList = functionList,
        Return = new[] { 0 },
        FunctionStatus = status ?? new[] { 1, 0 }
    };

    private static AcerHelperResponse With(AcerHelperOutcome outcome, string? detail = null) => new()
    {
        Protocol = AcerHelperProtocol.Version, Nonce = Nonce, Operation = AcerHelperOperation.ReadHealthStatus,
        Phase = AcerHelperPhase.Result, Outcome = outcome, Detail = detail
    };

    private static AcerBatteryHealthResult Classify(AcerHelperRunResult run) => AcerHealthClassifier.Classify(run, Now);
    private static AcerBatteryHealthResult Classify(AcerHelperResponse r) => Classify(AcerHelperRunResult.Responded(r));

    // ---- Classifier ----

    [Fact]
    public void Flag_on_is_supported_and_enabled()
    {
        var r = Classify(Ok(status: new[] { 1, 0 }));
        Assert.Equal(CapabilityStatus.Supported, r.Capability.Status);
        Assert.Equal(AcerReadOutcome.Read, r.Outcome);
        Assert.True(r.State!.Enabled);
        Assert.Equal(Now, r.State.ReadAt);
        Assert.Equal(0x03, r.State.FunctionList);
        Assert.Equal(new[] { 0 }, r.State.Return);
        Assert.Equal(new[] { 1, 0 }, r.State.Status);
    }

    [Fact]
    public void Flag_off_is_supported_and_disabled()
    {
        var r = Classify(Ok(status: new[] { 0, 0 }));
        Assert.Equal(CapabilityStatus.Supported, r.Capability.Status);
        Assert.False(r.State!.Enabled);
    }

    [Fact]
    public void Function_bit_clear_is_unsupported()
    {
        var r = Classify(Ok(functionList: 0x02));
        Assert.Equal(CapabilityStatus.Unsupported, r.Capability.Status);
        Assert.Equal(AcerReadOutcome.FunctionNotOffered, r.Outcome);
        Assert.Null(r.State);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 255, 0 })]
    public void Missing_or_odd_status_byte_is_unknown(int[] status)
    {
        var r = Classify(Ok(status: status));
        Assert.Equal(CapabilityStatus.Unknown, r.Capability.Status);
        Assert.Equal(AcerReadOutcome.AmbiguousResult, r.Outcome);
        Assert.Null(r.State);
    }

    [Fact]
    public void Missing_function_list_is_unknown()
    {
        var r = Classify(Ok(functionList: null));
        Assert.Equal(CapabilityStatus.Unknown, r.Capability.Status);
        Assert.Equal(AcerReadOutcome.AmbiguousResult, r.Outcome);
    }

    [Fact]
    public void Different_instance_name_is_still_read_but_noted()
    {
        var r = Classify(Ok(instance: @"ACPI\PNP0C14\Other_0"));
        Assert.Equal(AcerReadOutcome.Read, r.Outcome);
        Assert.Contains("differs from expected", r.Capability.Evidence);
    }

    [Theory]
    [InlineData(AcerHelperOutcome.InterfaceMissing, CapabilityStatus.Unsupported, AcerReadOutcome.InterfaceMissing)]
    [InlineData(AcerHelperOutcome.NoInstance, CapabilityStatus.Unsupported, AcerReadOutcome.NoInstance)]
    [InlineData(AcerHelperOutcome.SchemaMismatch, CapabilityStatus.Unknown, AcerReadOutcome.SchemaMismatch)]
    [InlineData(AcerHelperOutcome.AccessDenied, CapabilityStatus.Error, AcerReadOutcome.AccessDenied)]
    [InlineData(AcerHelperOutcome.NotElevated, CapabilityStatus.Error, AcerReadOutcome.AccessDenied)]
    [InlineData(AcerHelperOutcome.WmiError, CapabilityStatus.Error, AcerReadOutcome.WmiError)]
    [InlineData(AcerHelperOutcome.Unexpected, CapabilityStatus.Error, AcerReadOutcome.WmiError)]
    public void Helper_outcomes_map_to_the_documented_status(AcerHelperOutcome outcome, CapabilityStatus status, AcerReadOutcome detail)
    {
        var r = Classify(With(outcome, "detail"));
        Assert.Equal(status, r.Capability.Status);
        Assert.Equal(detail, r.Outcome);
        Assert.Null(r.State);
    }

    [Fact]
    public void Access_denied_is_distinguishable_from_missing_interface()
    {
        var denied = Classify(With(AcerHelperOutcome.AccessDenied));
        var missing = Classify(With(AcerHelperOutcome.InterfaceMissing));
        Assert.NotEqual(denied.Capability.Status, missing.Capability.Status);
        Assert.NotEqual(denied.Outcome, missing.Outcome);
    }

    [Fact]
    public void Schema_mismatch_lists_the_problems()
    {
        var r = Classify(With(AcerHelperOutcome.SchemaMismatch) with { SchemaProblems = new[] { "in: missing uBatteryNo" } });
        Assert.Contains("uBatteryNo", r.Capability.Evidence);
    }

    [Fact]
    public void Run_failures_map_to_unknown_or_error_never_unsupported()
    {
        var declined = Classify(AcerHelperRunResult.Declined());
        Assert.Equal(CapabilityStatus.Unknown, declined.Capability.Status);
        Assert.Equal(AcerReadOutcome.ElevationDeclined, declined.Outcome);

        var timeout = Classify(AcerHelperRunResult.TimedOut("x"));
        Assert.Equal(CapabilityStatus.Unknown, timeout.Capability.Status);
        Assert.Equal(AcerReadOutcome.ApprovalTimeout, timeout.Outcome);

        var cancelled = Classify(AcerHelperRunResult.Cancelled());
        Assert.Equal(CapabilityStatus.Unknown, cancelled.Capability.Status);

        var launch = Classify(AcerHelperRunResult.LaunchFailed("missing"));
        Assert.Equal(CapabilityStatus.Error, launch.Capability.Status);
        Assert.Equal(AcerReadOutcome.HelperUnavailable, launch.Outcome);

        var invalid = Classify(AcerHelperRunResult.InvalidResponse("bad"));
        Assert.Equal(CapabilityStatus.Error, invalid.Capability.Status);
        Assert.Equal(AcerReadOutcome.InvalidResponse, invalid.Outcome);

        var empty = Classify(new AcerHelperRunResult(AcerHelperRunKind.Response, null));
        Assert.Equal(CapabilityStatus.Error, empty.Capability.Status);
    }

    // ---- Protocol ----

    [Fact]
    public void Protocol_round_trip()
    {
        var json = AcerHelperProtocol.Serialize(Ok());
        Assert.True(AcerHelperProtocol.TryParse(json, Nonce, out var parsed, out var error), error);
        Assert.Equal(0x03, parsed!.FunctionList);
        Assert.Equal(new[] { 1, 0 }, parsed.FunctionStatus);
        Assert.Equal(AcerHealthClassifier.ExpectedInstance, parsed.InstanceName);
    }

    [Fact]
    public void Protocol_rejects_wrong_nonce_and_version()
    {
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Ok()), "ffffffffffffffffffffffffffffffff", out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Ok() with { Protocol = AcerHelperProtocol.Version + 1 }), Nonce, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(V2 + "\"outcome\":0")] // truncated
    [InlineData("not json")]
    [InlineData(V2 + "\"outcome\":99}")] // undefined outcome
    [InlineData(V2 + "\"outcome\":0,\"extra\":1}")] // unknown member
    [InlineData(V2 + "\"outcome\":0,\"functionList\":300}")] // out of range
    [InlineData(V2 + "\"outcome\":0,\"functionStatus\":[1,2,3,4,5,6,7,8,9]}")] // too long
    [InlineData(V2 + "\"outcome\":0,\"return\":[256]}")] // not a byte
    public void Protocol_rejects_bad_messages(string json)
    {
        Assert.False(AcerHelperProtocol.TryParse(json, Nonce, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Protocol_rejects_oversized_message()
    {
        var big = AcerHelperProtocol.Serialize(Ok()) + new string(' ', AcerHelperProtocol.MaxResponseBytes);
        Assert.False(AcerHelperProtocol.TryParse(big, Nonce, out _, out _));
    }

    [Fact]
    public void Protocol_validates_pipe_name_and_nonce()
    {
        Assert.True(AcerHelperProtocol.IsValidPipeName(AcerHelperProtocol.PipeNamePrefix + Nonce));
        Assert.False(AcerHelperProtocol.IsValidPipeName(@"..\evil"));
        Assert.False(AcerHelperProtocol.IsValidPipeName(AcerHelperProtocol.PipeNamePrefix + "XYZ"));
        Assert.False(AcerHelperProtocol.IsValidPipeName(null));
        Assert.True(AcerHelperProtocol.IsValidNonce(Nonce));
        Assert.False(AcerHelperProtocol.IsValidNonce(Nonce + "0"));
        Assert.False(AcerHelperProtocol.IsValidNonce("--x"));
    }

    // ---- Service ----

    private sealed class FakeChannel : IAcerHelperChannel
    {
        public int Calls;
        public TaskCompletionSource<AcerHelperRunResult> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Task<AcerHelperRunResult>>? Override;

        public Task<AcerHelperRunResult> RunAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Override != null ? Override() : Gate.Task;
        }
    }

    [Fact]
    public async Task Service_caches_last_result()
    {
        var channel = new FakeChannel();
        channel.Gate.SetResult(AcerHelperRunResult.Responded(Ok()));
        var service = new AcerBatteryHealthService(channel) { Clock = () => Now };

        Assert.Null(service.Last);
        var result = await service.CheckAsync();
        Assert.Same(result, service.Last);
        Assert.Equal(AcerReadOutcome.Read, result.Outcome);
    }

    [Fact]
    public async Task Concurrent_checks_launch_the_helper_once()
    {
        var channel = new FakeChannel();
        var service = new AcerBatteryHealthService(channel) { Clock = () => Now };

        var a = service.CheckAsync();
        var b = service.CheckAsync();
        channel.Gate.SetResult(AcerHelperRunResult.Responded(Ok()));
        await Task.WhenAll(a, b);

        Assert.Equal(1, channel.Calls);
        Assert.Same(await a, await b);

        // After completion a new check is allowed again.
        channel.Override = () => Task.FromResult(AcerHelperRunResult.Declined());
        var again = await service.CheckAsync();
        Assert.Equal(2, channel.Calls);
        Assert.Equal(AcerReadOutcome.ElevationDeclined, again.Outcome);
    }

    [Fact]
    public async Task Channel_exception_becomes_error_not_a_throw()
    {
        var channel = new FakeChannel { Override = () => throw new InvalidOperationException("boom") };
        var service = new AcerBatteryHealthService(channel) { Clock = () => Now };

        var result = await service.CheckAsync();
        Assert.Equal(CapabilityStatus.Error, result.Capability.Status);
        Assert.Equal(AcerReadOutcome.HelperUnavailable, result.Outcome);
    }

    [Fact]
    public async Task Cancellation_becomes_unknown()
    {
        var channel = new FakeChannel { Override = () => throw new OperationCanceledException() };
        var service = new AcerBatteryHealthService(channel) { Clock = () => Now };

        var result = await service.CheckAsync();
        Assert.Equal(CapabilityStatus.Unknown, result.Capability.Status);
    }

    // ---- Card view model ----

    private sealed class FakeService : IAcerBatteryHealthService
    {
        public AcerBatteryHealthResult? Last { get; set; }
        public Func<Task<AcerBatteryHealthResult>> Next { get; set; } = () => Task.FromResult(AcerBatteryHealthResult.NotChecked);

        public async Task<AcerBatteryHealthResult> CheckAsync(CancellationToken ct = default)
        {
            var r = await Next();
            Last = r;
            return r;
        }
    }

    [Fact]
    public void Card_starts_as_not_checked_and_shows_no_state()
    {
        var card = new AcerHealthCardViewModel(new FakeService());
        Assert.Equal("Not checked", card.StatusText);
        Assert.DoesNotContain("On", card.StatusText);
        Assert.DoesNotContain("Off", card.StatusText);
    }

    [Theory]
    [InlineData(true, "Health mode: On")]
    [InlineData(false, "Health mode: Off")]
    public async Task Card_shows_state_only_from_a_firmware_read(bool enabled, string expected)
    {
        var service = new FakeService { Next = () => Task.FromResult(Classify(Ok(status: new[] { enabled ? 1 : 0, 0 }))) };
        var card = new AcerHealthCardViewModel(service);

        await card.CheckCommand.ExecuteAsync(null);

        Assert.Equal(expected, card.StatusText);
        Assert.Contains("firmware", card.CheckedAtText);
        Assert.False(card.IsChecking);
    }

    [Theory]
    [InlineData(AcerHelperOutcome.InterfaceMissing, "Not supported")]
    [InlineData(AcerHelperOutcome.AccessDenied, "Error")]
    [InlineData(AcerHelperOutcome.SchemaMismatch, "Could not determine")]
    public async Task Card_never_shows_on_or_off_when_the_read_did_not_succeed(AcerHelperOutcome outcome, string expected)
    {
        var service = new FakeService { Next = () => Task.FromResult(Classify(With(outcome))) };
        var card = new AcerHealthCardViewModel(service);

        await card.CheckCommand.ExecuteAsync(null);

        Assert.Equal(expected, card.StatusText);
        Assert.DoesNotContain("Health mode", card.StatusText);
    }

    [Fact]
    public async Task Card_declined_prompt_is_could_not_determine()
    {
        var service = new FakeService { Next = () => Task.FromResult(Classify(AcerHelperRunResult.Declined())) };
        var card = new AcerHealthCardViewModel(service);
        await card.CheckCommand.ExecuteAsync(null);
        Assert.Equal("Could not determine", card.StatusText);
        Assert.Contains("declined", card.ReasonText);
    }

    [Fact]
    public async Task Card_command_is_disabled_while_checking()
    {
        var gate = new TaskCompletionSource<AcerBatteryHealthResult>();
        var service = new FakeService { Next = () => gate.Task };
        var card = new AcerHealthCardViewModel(service);

        var running = card.CheckCommand.ExecuteAsync(null);
        Assert.True(card.IsChecking);
        Assert.False(card.CheckCommand.CanExecute(null));

        gate.SetResult(Classify(Ok()));
        await running;
        Assert.False(card.IsChecking);
        Assert.True(card.CheckCommand.CanExecute(null));
        Assert.Equal("Check again", card.ButtonText);
    }

    [Fact]
    public async Task Card_survives_a_throwing_service()
    {
        var service = new FakeService { Next = () => throw new InvalidOperationException("boom") };
        var card = new AcerHealthCardViewModel(service);
        await card.CheckCommand.ExecuteAsync(null);
        Assert.Equal("Error", card.StatusText);
        Assert.False(card.IsChecking);
    }

    [Fact]
    public void Card_restores_the_last_result_from_the_service()
    {
        var service = new FakeService { Last = Classify(Ok(status: new[] { 1, 0 })) };
        var card = new AcerHealthCardViewModel(service);
        Assert.Equal("Health mode: On", card.StatusText);
    }

    // ---- BatteryViewModel keeps working with and without the card ----

    private sealed class NullReader : IBatteryReader { public BatteryInfo? Read() => null; }
    private sealed class NullStore : IBatteryHistoryStore
    {
        public void Add(BatterySample sample) { }
        public IReadOnlyList<BatterySample> Query(DateTimeOffset from) => Array.Empty<BatterySample>();
        public void Prune(DateTimeOffset olderThan) { }
    }
    private sealed class NullTimer : IPollingTimer
    {
        public event EventHandler? Tick { add { } remove { } }
        public void Start(TimeSpan interval) { }
        public void Stop() { }
    }

    [Fact]
    public void Battery_view_model_works_without_the_acer_card()
    {
        var vm = new BatteryViewModel(new NullReader(), new NullStore(), new NullTimer());
        Assert.Null(vm.Acer);
    }

    [Fact]
    public void Battery_view_model_exposes_the_acer_card()
    {
        var card = new AcerHealthCardViewModel(new FakeService());
        var vm = new BatteryViewModel(new NullReader(), new NullStore(), new NullTimer(), card);
        Assert.Same(card, vm.Acer);
    }
}
