using AcerCareLite.Core.Capabilities;

namespace AcerCareLite.Core.Acer;

/// <summary>Why a check ended the way it did. CapabilityStatus is the coarse gate; this is the detail.</summary>
public enum AcerReadOutcome
{
    NotChecked = 0,
    Read,
    InterfaceMissing,
    NoInstance,
    FunctionNotOffered,
    ElevationDeclined,
    ApprovalTimeout,
    SchemaMismatch,
    AmbiguousResult,
    AccessDenied,
    HelperUnavailable,
    InvalidResponse,
    WmiError,
    PeerNotTrusted
}

/// <summary>Health mode as reported by the firmware at <see cref="ReadAt"/>, with the complete raw bytes. Only exists when the read succeeded.</summary>
public sealed record AcerHealthModeState(
    bool Enabled,
    int FunctionList,
    IReadOnlyList<int> Return,
    IReadOnlyList<int> Status,
    string? InstanceName,
    DateTimeOffset ReadAt);

public sealed record AcerBatteryHealthResult(
    CapabilityResult Capability,
    AcerReadOutcome Outcome,
    AcerHealthModeState? State,
    DateTimeOffset? CheckedAt)
{
    public static AcerBatteryHealthResult NotChecked { get; } =
        new(CapabilityResult.Unknown("Not checked yet."), AcerReadOutcome.NotChecked, null, null);
}

public interface IAcerBatteryHealthService
{
    /// <summary>The most recent completed check in this session, or null.</summary>
    AcerBatteryHealthResult? Last { get; }

    /// <summary>Runs one read-only firmware check. Never throws for hardware or helper problems.</summary>
    Task<AcerBatteryHealthResult> CheckAsync(CancellationToken ct = default);
}

public enum AcerHelperRunKind { Response, Declined, TimedOut, Cancelled, LaunchFailed, InvalidResponse, PeerRejected, HelperIncomplete }

public sealed record AcerHelperRunResult(
    AcerHelperRunKind Kind,
    AcerHelperResponse? Response = null,
    string? Detail = null,
    IReadOnlyList<AcerHelperResponse>? Progress = null)
{
    /// <summary>Every message accepted from the helper before the run ended (empty when it never got that far).</summary>
    public IReadOnlyList<AcerHelperResponse> Messages => Progress ?? Array.Empty<AcerHelperResponse>();

    public AcerHelperPhase? LastPhase => Messages.Count == 0 ? null : Messages[Messages.Count - 1].Phase;

    public static AcerHelperRunResult Responded(AcerHelperResponse response, IReadOnlyList<AcerHelperResponse>? messages = null) => new(AcerHelperRunKind.Response, response, null, messages);
    public static AcerHelperRunResult Declined() => new(AcerHelperRunKind.Declined);
    public static AcerHelperRunResult TimedOut(string detail, IReadOnlyList<AcerHelperResponse>? messages = null) => new(AcerHelperRunKind.TimedOut, null, detail, messages);
    public static AcerHelperRunResult Cancelled(IReadOnlyList<AcerHelperResponse>? messages = null) => new(AcerHelperRunKind.Cancelled, null, null, messages);
    public static AcerHelperRunResult LaunchFailed(string detail) => new(AcerHelperRunKind.LaunchFailed, null, detail);
    public static AcerHelperRunResult InvalidResponse(string detail, IReadOnlyList<AcerHelperResponse>? messages = null) => new(AcerHelperRunKind.InvalidResponse, null, detail, messages);
    public static AcerHelperRunResult PeerRejected(string detail) => new(AcerHelperRunKind.PeerRejected, null, detail);
    public static AcerHelperRunResult HelperIncomplete(string detail, IReadOnlyList<AcerHelperResponse>? messages = null) => new(AcerHelperRunKind.HelperIncomplete, null, detail, messages);
}

/// <summary>Launches the elevated read-only helper and returns what it said. Implemented in AcerCareLite.Acer.</summary>
public interface IAcerHelperChannel
{
    Task<AcerHelperRunResult> RunAsync(CancellationToken ct);
}

/// <summary>Launches the elevated helper to change the health mode. Only ever called through the write service, behind the write gate.</summary>
public interface IAcerHelperWriteChannel
{
    Task<AcerHelperRunResult> RunSetAsync(bool enable, CancellationToken ct);
}
