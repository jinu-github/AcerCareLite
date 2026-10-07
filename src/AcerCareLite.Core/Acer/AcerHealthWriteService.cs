namespace AcerCareLite.Core.Acer;

/// <summary>
/// Orchestrates one change attempt: gate, audit record, helper run, classification, audit result. One attempt at a time.
/// No audit record, no change: if the "requested" record cannot be written, the helper is never launched.
/// </summary>
public sealed class AcerHealthWriteService : IAcerHealthWriteService
{
    private readonly IAcerHelperWriteChannel _channel;
    private readonly IAcerBatteryHealthService _reads;
    private readonly IHelperLocationGate _location;
    private readonly IAcerWriteAuditLog _audit;
    private readonly bool _writeCompiled;
    private readonly object _lock = new();

    private Task<AcerWriteResult>? _inflight;
    private AcerWriteResult? _last;
    private bool _locked;
    private DateTimeOffset? _lastAttemptAt;
    private DateTimeOffset? _problemAt;

    public AcerHealthWriteService(
        IAcerHelperWriteChannel channel, IAcerBatteryHealthService reads, IHelperLocationGate location,
        IAcerWriteAuditLog audit, bool? writeCompiled = null)
    {
        _channel = channel;
        _reads = reads;
        _location = location;
        _audit = audit;
        _writeCompiled = writeCompiled ?? WriteBuild.Compiled;
    }

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

    public AcerWriteResult? Last { get { lock (_lock) return _last; } }
    public bool IsLocked { get { lock (_lock) return _locked; } }

    public Task<AcerWriteResult> SetAsync(bool enable, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_inflight != null)
                return Task.FromResult(AcerWriteResult.Blocked(enable, "A change is already in progress.", Clock()));
            return _inflight = RunAsync(enable, ct);
        }
    }

    private async Task<AcerWriteResult> RunAsync(bool enable, CancellationToken ct)
    {
        await Task.Yield(); // the cleanup below must never run inside SetAsync's lock
        try
        {
            var now = Clock();
            var attemptId = Guid.NewGuid().ToString("N");

            WriteGateDecision decision;
            lock (_lock)
            {
                decision = WriteGate.Evaluate(new WriteGateInput(_writeCompiled, _location.Current, _reads.Last, _locked, _lastAttemptAt, _problemAt, now));
            }
            if (!decision.Allowed)
                return Finish(AcerWriteResult.Blocked(enable, decision.Reason, now), attemptId, "blocked");

            // Record the attempt BEFORE anything is launched. If it cannot be recorded, nothing is changed.
            var requested = new AcerWriteAuditEntry(now, attemptId, "requested", enable, "requested", null, Array.Empty<int>(), Array.Empty<int>(), "");
            if (!_audit.Append(requested))
                return Finish(AcerWriteResult.Blocked(enable, "The audit log could not be written.", now), attemptId, null);

            lock (_lock) _lastAttemptAt = now;

            AcerHelperRunResult run;
            try { run = await _channel.RunSetAsync(enable, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { run = AcerHelperRunResult.Cancelled(); }
            catch (Exception ex) { run = AcerHelperRunResult.LaunchFailed(ex.Message); }

            var result = HealthWriteClassifier.Classify(run, enable, Clock());
            return Finish(result, attemptId, "completed");
        }
        finally
        {
            lock (_lock) _inflight = null;
        }
    }

    private AcerWriteResult Finish(AcerWriteResult result, string attemptId, string? auditKind)
    {
        lock (_lock)
        {
            _last = result;
            if (result.LocksWrites) _locked = true;
            if (result.RequiresFreshRead) _problemAt = result.At;
        }

        if (auditKind != null)
        {
            _audit.Append(new AcerWriteAuditEntry(
                result.At, attemptId, auditKind, result.RequestedEnabled, result.Outcome.ToString(), result.LastPhase?.ToString(),
                result.Before.ToArray(), result.After.ToArray(), result.Summary + (string.IsNullOrEmpty(result.Evidence) ? "" : " | " + result.Evidence)));
        }
        return result;
    }
}
