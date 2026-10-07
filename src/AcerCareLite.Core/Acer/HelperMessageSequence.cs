namespace AcerCareLite.Core.Acer;

/// <summary>
/// Accepts the helper's messages for one operation in strict order: Started, then (set only, each optional)
/// PreReadOk and WriteSent, then Result, which ends the conversation. WriteSent must directly follow PreReadOk.
/// </summary>
public sealed class HelperMessageSequence
{
    private readonly AcerHelperOperation _operation;

    public HelperMessageSequence(AcerHelperOperation operation) => _operation = operation;

    public AcerHelperPhase? LastPhase { get; private set; }
    public AcerHelperResponse? Result { get; private set; }
    public bool IsComplete => Result != null;

    private static int Order(AcerHelperPhase phase) => phase switch
    {
        AcerHelperPhase.Started => 0,
        AcerHelperPhase.PreReadOk => 1,
        AcerHelperPhase.WriteSent => 2,
        AcerHelperPhase.Result => 3,
        _ => -1
    };

    public bool Accept(AcerHelperResponse message, out string error)
    {
        error = "";
        if (Result != null) { error = "Message after the result."; return false; }
        if (message.Operation != _operation) { error = "Unexpected operation."; return false; }

        var phase = message.Phase;
        if (Order(phase) < 0) { error = "Unknown phase."; return false; }
        if (_operation == AcerHelperOperation.ReadHealthStatus && phase is AcerHelperPhase.PreReadOk or AcerHelperPhase.WriteSent)
        {
            error = $"Phase {phase} does not belong to a read.";
            return false;
        }

        if (LastPhase == null)
        {
            if (phase != AcerHelperPhase.Started) { error = $"Expected phase Started, got {phase}."; return false; }
        }
        else
        {
            if (Order(phase) <= Order(LastPhase.Value)) { error = $"Phase {phase} is out of order after {LastPhase}."; return false; }
            if (phase == AcerHelperPhase.PreReadOk && LastPhase != AcerHelperPhase.Started) { error = "PreReadOk must directly follow Started."; return false; }
            if (phase == AcerHelperPhase.WriteSent && LastPhase != AcerHelperPhase.PreReadOk) { error = "WriteSent must directly follow PreReadOk."; return false; }
        }

        LastPhase = phase;
        if (phase == AcerHelperPhase.Result) Result = message;
        return true;
    }
}
