using AcerCareLite.Core.Capabilities;

namespace AcerCareLite.Core.Acer;

public sealed record WriteGateInput(
    bool WriteCompiled,
    HelperLocationStatus Location,
    AcerBatteryHealthResult? LastRead,
    bool Locked,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? ProblemAt,
    DateTimeOffset Now);

public sealed record WriteGateDecision(bool Allowed, string Reason);

/// <summary>
/// Every condition must hold before the app launches the helper to change anything. The helper repeats the location
/// check itself, so a patched app cannot skip it.
/// </summary>
public static class WriteGate
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(10);

    public static WriteGateDecision Evaluate(WriteGateInput input)
    {
        if (!input.WriteCompiled)
            return Deny("Changing the setting is not enabled in this build.");

        if (!input.Location.IsProtected)
            return Deny("The helper is not in a protected location. " + input.Location.Reason);

        if (input.Locked)
            return Deny("Changes are locked for this session after an unexpected result.");

        var read = input.LastRead;
        if (read == null || read.Outcome != AcerReadOutcome.Read || read.Capability.Status != CapabilityStatus.Supported)
            return Deny("Read the current state from the firmware first.");

        if (input.ProblemAt is { } problemAt && (read.CheckedAt is not { } readAt || readAt <= problemAt))
            return Deny("The last change attempt ended unexpectedly. Read the current state again first.");

        if (input.LastAttemptAt is { } last && input.Now - last < MinInterval)
        {
            var wait = (int)Math.Ceiling((MinInterval - (input.Now - last)).TotalSeconds);
            return Deny($"Please wait {wait} s between change attempts.");
        }

        return new WriteGateDecision(true, "");
    }

    private static WriteGateDecision Deny(string reason) => new(false, reason);
}
