namespace AcerCareLite.Core.Acer;

/// <summary>
/// Decides what a change attempt meant. The rule that matters: if the helper may have sent the change command and we
/// cannot prove what the firmware reports now, the answer is Unknown, never "not applied".
/// </summary>
public static class HealthWriteClassifier
{
    public static AcerWriteResult Classify(AcerHelperRunResult run, bool enable, DateTimeOffset now)
    {
        var sent = run.Messages.Any(m => m.Phase == AcerHelperPhase.WriteSent);
        var before = run.Messages.LastOrDefault(m => m.Phase == AcerHelperPhase.PreReadOk)?.BeforeStatus ?? Array.Empty<int>();
        var last = run.LastPhase;

        AcerWriteResult Make(AcerWriteOutcome outcome, string summary, string evidence, IReadOnlyList<int>? b = null, IReadOnlyList<int>? a = null) =>
            new(outcome, enable, summary, evidence, b ?? before, a ?? Array.Empty<int>(), last, now);

        AcerWriteResult Uncertain(string reason, string evidence) => sent
            ? Make(AcerWriteOutcome.Unknown, "The change command may have been sent and the result is unknown: " + reason + " The setting may have changed. Press Check to read the current state.", evidence)
            : Make(AcerWriteOutcome.NotApplied, reason + " Nothing was changed.", evidence);

        var phaseText = "lastPhase=" + (last?.ToString() ?? "none");

        switch (run.Kind)
        {
            case AcerHelperRunKind.Declined:
                return Make(AcerWriteOutcome.NotApplied, "Administrator approval was declined. Nothing was changed.", phaseText);
            case AcerHelperRunKind.LaunchFailed:
                return Make(AcerWriteOutcome.NotApplied, "The elevated helper could not be run. Nothing was changed.", run.Detail ?? phaseText);
            case AcerHelperRunKind.PeerRejected:
                return Make(AcerWriteOutcome.Untrusted, "An unexpected program answered instead of the helper; its reply was ignored. Changes are locked for this session.", run.Detail ?? phaseText);
            case AcerHelperRunKind.Cancelled:
                return Uncertain("The attempt was cancelled.", phaseText);
            case AcerHelperRunKind.TimedOut:
                return Uncertain("The helper did not respond in time.", run.Detail ?? phaseText);
            case AcerHelperRunKind.InvalidResponse:
                return Uncertain("The helper's reply was not valid.", (run.Detail ?? "") + " " + phaseText);
            case AcerHelperRunKind.HelperIncomplete:
                return Uncertain("The helper stopped before finishing.", (run.Detail ?? "") + " " + phaseText);
            case AcerHelperRunKind.Response when run.Response != null:
                return ClassifyResponse(run, run.Response, enable, now, sent, before, Make, Uncertain);
            default:
                return Uncertain("The helper returned no result.", phaseText);
        }
    }

    private static AcerWriteResult ClassifyResponse(
        AcerHelperRunResult run, AcerHelperResponse r, bool enable, DateTimeOffset now, bool sent, IReadOnlyList<int> beforeFromProgress,
        Func<AcerWriteOutcome, string, string, IReadOnlyList<int>?, IReadOnlyList<int>?, AcerWriteResult> make,
        Func<string, string, AcerWriteResult> uncertain)
    {
        if (r.Operation != AcerHelperOperation.SetHealthMode || r.Phase != AcerHelperPhase.Result || r.RequestedEnabled != enable)
            return uncertain("The helper's reply did not match the request.", $"operation={r.Operation}; requested={r.RequestedEnabled}");

        var before = r.BeforeStatus.Length > 0 ? r.BeforeStatus : beforeFromProgress;
        var evidence = Evidence(enable, before, r);

        switch (r.Outcome)
        {
            case AcerHelperOutcome.NoChangeNeeded:
                return make(AcerWriteOutcome.NoChange, $"Health mode is already {OnOff(enable)}; nothing was sent.", evidence, before, r.FunctionStatus);

            case AcerHelperOutcome.Ok:
            case AcerHelperOutcome.SetterError:
                return Verify(r, enable, before, evidence, make, uncertain);

            default:
                // Anything else is a refusal or failure reported by the helper. If WriteSent was already seen we cannot
                // call it "not applied": the firmware may have been touched.
                var text = Describe(r.Outcome, r.Detail);
                return sent
                    ? uncertain(text, evidence)
                    : make(AcerWriteOutcome.NotApplied, text + " Nothing was changed.", evidence, before, null);
        }
    }

    private static AcerWriteResult Verify(
        AcerHelperResponse r, bool enable, IReadOnlyList<int> before, string evidence,
        Func<AcerWriteOutcome, string, string, IReadOnlyList<int>?, IReadOnlyList<int>?, AcerWriteResult> make,
        Func<string, string, AcerWriteResult> uncertain)
    {
        var after = r.FunctionStatus;
        if (before.Count != 5 || r.BeforeFunctionList is not { } functionsBefore)
            return uncertain("The reply did not include the state from before the change.", evidence);
        if (after.Length == 0 || r.FunctionList is not { } functionsAfter)
            return uncertain("The state could not be read back after the change.", evidence);

        var target = enable ? 1 : 0;

        var otherByteChanged = after.Length != before.Count;
        for (var i = 1; !otherByteChanged && i < after.Length; i++)
            if (after[i] != before[i]) otherByteChanged = true;

        if (functionsAfter != functionsBefore || otherByteChanged)
            return make(AcerWriteOutcome.Anomaly,
                "Unexpected: a byte other than the health-mode byte, or the function list, changed after the request. Changes are locked for this session. Please report the raw bytes.",
                evidence, before, after);

        if (after[0] == target)
        {
            if (before[0] == target)
                return make(AcerWriteOutcome.Anomaly, "Unexpected: the firmware was already in the requested state before the change command. Changes are locked for this session.", evidence, before, after);

            var note = r.Outcome == AcerHelperOutcome.SetterError ? " (the firmware call reported an error, but the readback proves the state)" : "";
            return make(AcerWriteOutcome.Applied, $"Health mode is now {OnOff(enable)}. Verified by reading the firmware back{note}.", evidence, before, after);
        }

        if (after[0] == before[0])
            return make(AcerWriteOutcome.Failed, $"The change command ran, but within the readback window the firmware still reported health mode {OnOff(before[0] == 1)}. The firmware may still be applying the change: wait a moment, then press Check to read the current state.", evidence, before, after);

        return make(AcerWriteOutcome.Anomaly, $"Unexpected: the health-mode byte reads {after[0]} after the request. Changes are locked for this session.", evidence, before, after);
    }

    private static string Describe(AcerHelperOutcome outcome, string? detail) => outcome switch
    {
        AcerHelperOutcome.NotElevated => "The helper was not running as administrator.",
        AcerHelperOutcome.AccessDenied => "Windows denied access to the Acer interface.",
        AcerHelperOutcome.InterfaceMissing => "This PC does not expose the Acer battery control interface.",
        AcerHelperOutcome.NoInstance => "The Acer battery control class has no instance.",
        AcerHelperOutcome.SchemaMismatch => "The Acer interface differs from the expected layout, so it was not touched.",
        AcerHelperOutcome.PreconditionFailed => "A safety check failed: " + (detail ?? "unspecified."),
        AcerHelperOutcome.WriteNotPermitted => "The helper refused: " + (detail ?? "writes are not permitted."),
        AcerHelperOutcome.WmiError or AcerHelperOutcome.Unexpected => "Reading the Acer interface failed" + (detail != null ? ": " + detail : "."),
        _ => "The helper reported an unexpected outcome."
    };

    private static string OnOff(bool on) => on ? "On" : "Off";

    private static string Evidence(bool enable, IReadOnlyList<int> before, AcerHelperResponse r) =>
        $"requested={(enable ? "on" : "off")}; before=[{string.Join(",", before)}] fl={Hex(r.BeforeFunctionList)}; after=[{string.Join(",", r.FunctionStatus)}] fl={Hex(r.FunctionList)}; "
        + $"setterReturn={(r.SetReturn?.ToString() ?? "n/a")}; readbackAttempts={r.ReadbackAttempts}"
        + (string.IsNullOrWhiteSpace(r.Detail) ? "" : "; " + r.Detail);

    private static string Hex(int? v) => v is { } n ? $"0x{n:X2}" : "none";
}
