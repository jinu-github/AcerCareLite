namespace AcerCareLite.Core.Acer;

/// <summary>Runs one check at a time, classifies the helper's answer, and remembers the last result for this session.</summary>
public sealed class AcerBatteryHealthService : IAcerBatteryHealthService
{
    private readonly IAcerHelperChannel _channel;
    private readonly object _lock = new();
    private Task<AcerBatteryHealthResult>? _inflight;
    private AcerBatteryHealthResult? _last;

    public AcerBatteryHealthService(IAcerHelperChannel channel) => _channel = channel;

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

    public AcerBatteryHealthResult? Last => Volatile.Read(ref _last);

    public Task<AcerBatteryHealthResult> CheckAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            // A second caller while a check is running joins it instead of launching another helper.
            return _inflight ??= RunAsync(ct);
        }
    }

    private async Task<AcerBatteryHealthResult> RunAsync(CancellationToken ct)
    {
        await Task.Yield(); // guarantees the cleanup below never runs inside CheckAsync's lock
        try
        {
            AcerHelperRunResult run;
            try { run = await _channel.RunAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { run = AcerHelperRunResult.Cancelled(); }
            catch (Exception ex) { run = AcerHelperRunResult.LaunchFailed(ex.Message); }

            var result = AcerHealthClassifier.Classify(run, Clock());
            Volatile.Write(ref _last, result);
            return result;
        }
        finally
        {
            lock (_lock) _inflight = null;
        }
    }
}
