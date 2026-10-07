using AcerCareLite.Core.Capabilities;

namespace AcerCareLite.Core.Acer;

/// <summary>
/// All Supported/Unsupported/Unknown/Error decisions live here, in the unelevated process.
/// Unsupported is only ever produced from affirmative evidence; failures to find out are Unknown or Error.
/// </summary>
public static class AcerHealthClassifier
{
    public const string ExpectedInstance = @"ACPI\PNP0C14\APGe_0";
    private const int HealthModeFunctionBit = 0x01;

    public static AcerBatteryHealthResult Classify(AcerHelperRunResult run, DateTimeOffset now)
    {
        switch (run.Kind)
        {
            case AcerHelperRunKind.Declined:
                return Make(CapabilityResult.Unknown("Administrator approval was declined. Nothing was read."), AcerReadOutcome.ElevationDeclined, now);
            case AcerHelperRunKind.TimedOut:
                return Make(CapabilityResult.Unknown("Timed out waiting for administrator approval. Nothing was read."), AcerReadOutcome.ApprovalTimeout, now);
            case AcerHelperRunKind.Cancelled:
                return Make(CapabilityResult.Unknown("The check was cancelled."), AcerReadOutcome.NotChecked, now);
            case AcerHelperRunKind.LaunchFailed:
                return Make(CapabilityResult.Error("The elevated helper could not be run.", run.Detail), AcerReadOutcome.HelperUnavailable, now);
            case AcerHelperRunKind.InvalidResponse:
                return Make(CapabilityResult.Error("The helper's reply was not valid and was ignored.", run.Detail), AcerReadOutcome.InvalidResponse, now);
            case AcerHelperRunKind.PeerRejected:
                return Make(CapabilityResult.Error("The program that answered was not the expected helper, so its reply was ignored.", run.Detail), AcerReadOutcome.PeerNotTrusted, now);
            case AcerHelperRunKind.HelperIncomplete:
                return Make(CapabilityResult.Error("The elevated helper stopped before finishing its read.", run.Detail), AcerReadOutcome.HelperUnavailable, now);
            case AcerHelperRunKind.Response when run.Response is { Phase: not AcerHelperPhase.Result }:
                return Make(CapabilityResult.Error("The helper sent a progress message instead of a result."), AcerReadOutcome.InvalidResponse, now);
            case AcerHelperRunKind.Response when run.Response != null:
                return ClassifyResponse(run.Response, now);
            default:
                return Make(CapabilityResult.Error("The helper returned no result."), AcerReadOutcome.InvalidResponse, now);
        }
    }

    private static AcerBatteryHealthResult ClassifyResponse(AcerHelperResponse r, DateTimeOffset now)
    {
        switch (r.Outcome)
        {
            case AcerHelperOutcome.NotElevated:
                return Make(CapabilityResult.Error("The helper was not running as administrator.", r.Detail), AcerReadOutcome.AccessDenied, now);
            case AcerHelperOutcome.AccessDenied:
                return Make(CapabilityResult.Error("Windows denied access to the Acer interface (administrator rights are required).", r.Detail), AcerReadOutcome.AccessDenied, now);
            case AcerHelperOutcome.InterfaceMissing:
                return Make(CapabilityResult.Unsupported("This PC does not expose the Acer battery control interface.", r.Detail), AcerReadOutcome.InterfaceMissing, now);
            case AcerHelperOutcome.NoInstance:
                return Make(CapabilityResult.Unsupported("The Acer battery control class exists but has no instance.", r.Detail), AcerReadOutcome.NoInstance, now);
            case AcerHelperOutcome.SchemaMismatch:
                var problems = r.SchemaProblems.Length > 0 ? string.Join("; ", r.SchemaProblems) : r.Detail;
                return Make(UnknownWith("The Acer interface differs from the documented layout, so it was not read.", problems), AcerReadOutcome.SchemaMismatch, now);
            case AcerHelperOutcome.WmiError:
            case AcerHelperOutcome.Unexpected:
                return Make(CapabilityResult.Error("Reading the Acer interface failed.", r.Detail), AcerReadOutcome.WmiError, now);
            case AcerHelperOutcome.Ok:
                return ClassifyRead(r, now);
            default:
                return Make(CapabilityResult.Error("Unrecognised helper outcome."), AcerReadOutcome.InvalidResponse, now);
        }
    }

    private static AcerBatteryHealthResult ClassifyRead(AcerHelperResponse r, DateTimeOffset now)
    {
        var evidence = Evidence(r);

        if (r.FunctionList is not { } functions)
            return Make(UnknownWith("The firmware did not report a function list.", evidence), AcerReadOutcome.AmbiguousResult, now);

        if ((functions & HealthModeFunctionBit) == 0)
            return Make(CapabilityResult.Unsupported("The firmware says battery health mode is not offered on this PC.", evidence), AcerReadOutcome.FunctionNotOffered, now);

        if (r.FunctionStatus.Length == 0)
            return Make(UnknownWith("The firmware offers health mode but returned no state byte.", evidence), AcerReadOutcome.AmbiguousResult, now);

        var status = r.FunctionStatus[0];
        if (status is not (0 or 1))
            return Make(UnknownWith($"The firmware returned an unexpected state value ({status}).", evidence), AcerReadOutcome.AmbiguousResult, now);

        var enabled = status == 1;
        var state = new AcerHealthModeState(enabled, functions, r.Return, r.FunctionStatus, r.InstanceName, now);
        var reason = enabled
            ? "The firmware reports battery health mode is on."
            : "The firmware reports battery health mode is off.";
        return new AcerBatteryHealthResult(CapabilityResult.Supported(reason, evidence), AcerReadOutcome.Read, state, now);
    }

    private static string Evidence(AcerHelperResponse r)
    {
        var parts = new List<string>
        {
            r.FunctionList is { } f ? $"functionList=0x{f:X2}" : "functionList=none",
            "return=[" + string.Join(",", r.Return) + "]",
            "status=[" + string.Join(",", r.FunctionStatus) + "]"
        };
        if (r.InstanceName != null)
        {
            parts.Add("instance=" + r.InstanceName);
            if (!string.Equals(r.InstanceName, ExpectedInstance, StringComparison.OrdinalIgnoreCase))
                parts.Add("(differs from expected " + ExpectedInstance + ")");
        }
        if (!string.IsNullOrWhiteSpace(r.Detail)) parts.Add(r.Detail!);
        return string.Join("; ", parts);
    }

    // CapabilityResult.Unknown(reason) carries no evidence, so build the record directly.
    private static CapabilityResult UnknownWith(string reason, string? evidence) =>
        new(CapabilityStatus.Unknown, reason, evidence);

    private static AcerBatteryHealthResult Make(CapabilityResult capability, AcerReadOutcome outcome, DateTimeOffset now) =>
        new(capability, outcome, null, now);
}
