namespace AcerCareLite.Core.Acer;

/// <summary>What happened to a change request. The wording shown to the user depends on whether the firmware could have been touched.</summary>
public enum AcerWriteOutcome
{
    /// <summary>Read back from the firmware: the health byte equals the request and nothing else changed.</summary>
    Applied,
    /// <summary>The firmware was already in the requested state; nothing was sent.</summary>
    NoChange,
    /// <summary>Certain that nothing was changed (declined, refused, failed before the change command).</summary>
    NotApplied,
    /// <summary>The change command ran but the readback still shows the old state.</summary>
    Failed,
    /// <summary>The change command may have run and the result is unknown (helper died or timed out afterwards).</summary>
    Unknown,
    /// <summary>The readback shows something unexpected (another byte or the function list changed). Writes lock for the session.</summary>
    Anomaly,
    /// <summary>An unexpected program answered instead of our helper. Writes lock for the session.</summary>
    Untrusted,
    /// <summary>The app refused before launching anything (gate, rate limit, audit log).</summary>
    Blocked
}

public sealed record AcerWriteResult(
    AcerWriteOutcome Outcome,
    bool RequestedEnabled,
    string Summary,
    string Evidence,
    IReadOnlyList<int> Before,
    IReadOnlyList<int> After,
    AcerHelperPhase? LastPhase,
    DateTimeOffset At)
{
    public bool LocksWrites => Outcome is AcerWriteOutcome.Anomaly or AcerWriteOutcome.Untrusted;

    /// <summary>After these, another change is refused until a fresh successful read.</summary>
    public bool RequiresFreshRead => Outcome is AcerWriteOutcome.Failed or AcerWriteOutcome.Unknown or AcerWriteOutcome.Anomaly or AcerWriteOutcome.Untrusted;

    /// <summary>The state the firmware was verified to be in, when the outcome proves it.</summary>
    public bool? VerifiedEnabled => Outcome is AcerWriteOutcome.Applied or AcerWriteOutcome.NoChange ? RequestedEnabled : null;

    public static AcerWriteResult Blocked(bool requestedEnabled, string reason, DateTimeOffset now) =>
        new(AcerWriteOutcome.Blocked, requestedEnabled, reason + " Nothing was changed.", "", Array.Empty<int>(), Array.Empty<int>(), null, now);
}

public interface IAcerHealthWriteService
{
    AcerWriteResult? Last { get; }
    bool IsLocked { get; }
    Task<AcerWriteResult> SetAsync(bool enable, CancellationToken ct = default);
}

public sealed record AcerWriteAuditEntry(
    DateTimeOffset At,
    string AttemptId,
    string Kind,
    bool RequestedEnabled,
    string Outcome,
    string? LastPhase,
    int[] Before,
    int[] After,
    string Detail);

public interface IAcerWriteAuditLog
{
    /// <summary>Appends one record. Returns false if it could not be written (the caller then refuses to change anything).</summary>
    bool Append(AcerWriteAuditEntry entry);
}
