using System.Text.Json;
using System.Text.RegularExpressions;

// This file is also compiled into the elevated helper (linked), so it must not depend on any other Core file.
namespace AcerCareLite.Core.Acer;

/// <summary>What the helper is asked to do. Values start at 1 so a missing field is rejected.</summary>
public enum AcerHelperOperation
{
    ReadHealthStatus = 1,
    SetHealthMode = 2
}

/// <summary>
/// Progress marker carried by every message. Order on the wire: Started, PreReadOk, WriteSent, Result (PreReadOk and
/// WriteSent are only used by SetHealthMode). WriteSent is sent BEFORE the firmware is asked to change anything.
/// Values start at 1 so a missing field is rejected.
/// </summary>
public enum AcerHelperPhase
{
    Started = 1,
    Result = 2,
    PreReadOk = 3,
    WriteSent = 4
}

/// <summary>What the helper directly observed. Interpretation (Supported/Unsupported/...) happens in the main process.</summary>
public enum AcerHelperOutcome
{
    Ok = 0,
    NotElevated = 1,
    AccessDenied = 2,
    InterfaceMissing = 3,
    NoInstance = 4,
    SchemaMismatch = 5,
    WmiError = 6,
    Unexpected = 7,
    NoChangeNeeded = 8,
    PreconditionFailed = 9,
    WriteNotPermitted = 10,
    SetterError = 11
}

/// <summary>One line of JSON sent by the helper over the pipe. Progress messages carry no payload.</summary>
public sealed record AcerHelperResponse
{
    public int Protocol { get; init; }
    public string Nonce { get; init; } = "";
    public AcerHelperOperation Operation { get; init; }
    public AcerHelperPhase Phase { get; init; }
    public AcerHelperOutcome Outcome { get; init; }
    public string? Detail { get; init; }
    public string? InstanceName { get; init; }
    public int? FunctionList { get; init; }
    public int[] Return { get; init; } = Array.Empty<int>();
    public int[] FunctionStatus { get; init; } = Array.Empty<int>();
    public string[] SchemaProblems { get; init; } = Array.Empty<string>();

    // Write-operation facts (SetHealthMode only). The helper reports raw facts; the app decides what they mean.
    public bool? RequestedEnabled { get; init; }
    public int? BeforeFunctionList { get; init; }
    public int[] BeforeStatus { get; init; } = Array.Empty<int>();
    public int? SetReturn { get; init; }
    public int? SetReservedOut { get; init; }
    public int ReadbackAttempts { get; init; }
}

public static class AcerHelperProtocol
{
    public const int Version = 3;
    public const string PipeNamePrefix = "AcerCareLite.Acer.";
    public const int MaxLineBytes = 4096;
    public const int MaxResponseBytes = 16 * 1024;
    public const int MaxDetailLength = 300;

    private const int MaxArrayLength = 8;
    private const int MaxProblems = 16;

    private static readonly Regex PipeNameRule = new(@"^AcerCareLite\.Acer\.[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex NonceRule = new(@"^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    public static bool IsValidPipeName(string? value) => value != null && PipeNameRule.IsMatch(value);
    public static bool IsValidNonce(string? value) => value != null && NonceRule.IsMatch(value);

    /// <summary>Command-line spelling of an operation. Strict, lower-case, exact.</summary>
    public static string OperationArg(AcerHelperOperation operation) => operation switch
    {
        AcerHelperOperation.ReadHealthStatus => "read",
        AcerHelperOperation.SetHealthMode => "set",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    public static bool TryParseOperationArg(string? value, out AcerHelperOperation operation)
    {
        if (string.Equals(value, "read", StringComparison.Ordinal)) { operation = AcerHelperOperation.ReadHealthStatus; return true; }
        if (string.Equals(value, "set", StringComparison.Ordinal)) { operation = AcerHelperOperation.SetHealthMode; return true; }
        operation = default;
        return false;
    }

    /// <summary>
    /// Read:  --op read --pipe NAME --nonce HEX
    /// Set:   --op set --state on|off --pipe NAME --nonce HEX
    /// The requested state travels on the command line (validated, fixed vocabulary), never over the pipe.
    /// </summary>
    public static string[] BuildArguments(AcerHelperOperation operation, bool? enable, string pipeName, string nonce)
    {
        if (operation == AcerHelperOperation.SetHealthMode)
        {
            if (enable is null) throw new ArgumentException("A set operation needs a target state.", nameof(enable));
            return new[] { "--op", "set", "--state", enable.Value ? "on" : "off", "--pipe", pipeName, "--nonce", nonce };
        }
        return new[] { "--op", OperationArg(operation), "--pipe", pipeName, "--nonce", nonce };
    }

    /// <summary>Strict parser for the helper's command line. Anything not exactly one of the two shapes is rejected.</summary>
    public static bool TryParseArguments(string[]? args, out AcerHelperOperation operation, out bool enable, out string pipeName, out string nonce)
    {
        operation = default;
        enable = false;
        pipeName = nonce = "";
        if (args == null) return false;

        string pipe, nonceText;
        if (args.Length == 6 && args[0] == "--op" && args[1] == "read" && args[2] == "--pipe" && args[4] == "--nonce")
        {
            operation = AcerHelperOperation.ReadHealthStatus;
            pipe = args[3];
            nonceText = args[5];
        }
        else if (args.Length == 8 && args[0] == "--op" && args[1] == "set" && args[2] == "--state" && (args[3] == "on" || args[3] == "off")
                 && args[4] == "--pipe" && args[6] == "--nonce")
        {
            operation = AcerHelperOperation.SetHealthMode;
            enable = args[3] == "on";
            pipe = args[5];
            nonceText = args[7];
        }
        else return false;

        if (!IsValidPipeName(pipe) || !IsValidNonce(nonceText)) { operation = default; enable = false; return false; }
        pipeName = pipe;
        nonce = nonceText;
        return true;
    }

    public static AcerHelperResponse Progress(string nonce, AcerHelperOperation operation, AcerHelperPhase phase) => new()
    {
        Protocol = Version,
        Nonce = nonce,
        Operation = operation,
        Phase = phase,
        Outcome = AcerHelperOutcome.Ok
    };

    public static string Serialize(AcerHelperResponse response) => JsonSerializer.Serialize(response, Options);

    /// <summary>Strict validation of one message: anything unexpected is rejected rather than repaired.</summary>
    public static bool TryParse(string? json, string expectedNonce, out AcerHelperResponse? response, out string error)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "Empty message."; return false; }
        if (json.Length > MaxLineBytes) { error = "Message too large."; return false; }

        AcerHelperResponse? parsed;
        try { parsed = JsonSerializer.Deserialize<AcerHelperResponse>(json, Options); }
        catch (JsonException ex) { error = "Malformed message: " + ex.Message; return false; }
        catch (NotSupportedException ex) { error = "Malformed message: " + ex.Message; return false; }

        if (parsed == null) { error = "Empty message."; return false; }
        if (parsed.Protocol != Version) { error = $"Unsupported protocol version {parsed.Protocol}."; return false; }
        if (!string.Equals(parsed.Nonce, expectedNonce, StringComparison.Ordinal)) { error = "Nonce mismatch."; return false; }
        if (!Enum.IsDefined(typeof(AcerHelperOperation), parsed.Operation)) { error = "Unknown operation."; return false; }
        if (!Enum.IsDefined(typeof(AcerHelperPhase), parsed.Phase)) { error = "Unknown phase."; return false; }
        if (!Enum.IsDefined(typeof(AcerHelperOutcome), parsed.Outcome)) { error = "Unknown outcome."; return false; }
        if (parsed.Detail is { Length: > MaxDetailLength } || parsed.InstanceName is { Length: > MaxDetailLength }) { error = "Text field too long."; return false; }
        if (parsed.FunctionList is < 0 or > 255) { error = "FunctionList out of range."; return false; }
        if (!ByteArrayOk(parsed.Return) || !ByteArrayOk(parsed.FunctionStatus)) { error = "Byte array invalid."; return false; }
        if (parsed.SchemaProblems == null || parsed.SchemaProblems.Length > MaxProblems
            || parsed.SchemaProblems.Any(p => p == null || p.Length > MaxDetailLength)) { error = "Schema problem list invalid."; return false; }
        if (parsed.BeforeFunctionList is < 0 or > 255) { error = "BeforeFunctionList out of range."; return false; }
        if (!ByteArrayOk(parsed.BeforeStatus)) { error = "Before-state array invalid."; return false; }
        if (parsed.SetReturn is < 0 or > 65535 || parsed.SetReservedOut is < 0 or > 65535) { error = "Setter value out of range."; return false; }
        if (parsed.ReadbackAttempts is < 0 or > 100) { error = "Readback count out of range."; return false; }

        switch (parsed.Phase)
        {
            case AcerHelperPhase.Started:
            case AcerHelperPhase.WriteSent:
                if (HasPayload(parsed, allowBeforeState: false)) { error = "A progress message must not carry a payload."; return false; }
                break;
            case AcerHelperPhase.PreReadOk:
                if (parsed.Operation != AcerHelperOperation.SetHealthMode) { error = "PreReadOk belongs to the set operation."; return false; }
                if (HasPayload(parsed, allowBeforeState: true)) { error = "PreReadOk may only carry the before-state."; return false; }
                break;
            case AcerHelperPhase.Result:
                if (parsed.Operation == AcerHelperOperation.ReadHealthStatus && HasWriteFields(parsed)) { error = "A read result must not carry write fields."; return false; }
                break;
        }

        response = parsed;
        error = "";
        return true;
    }

    private static bool HasWriteFields(AcerHelperResponse r) =>
        r.RequestedEnabled != null || r.BeforeFunctionList != null || r.BeforeStatus.Length > 0
        || r.SetReturn != null || r.SetReservedOut != null || r.ReadbackAttempts != 0;

    private static bool HasPayload(AcerHelperResponse r, bool allowBeforeState)
    {
        var other = r.Outcome != AcerHelperOutcome.Ok || r.Detail != null || r.InstanceName != null || r.FunctionList != null
            || r.Return.Length > 0 || r.FunctionStatus.Length > 0 || r.SchemaProblems.Length > 0
            || r.RequestedEnabled != null || r.SetReturn != null || r.SetReservedOut != null || r.ReadbackAttempts != 0;
        if (other) return true;
        return !allowBeforeState && (r.BeforeFunctionList != null || r.BeforeStatus.Length > 0);
    }

    private static bool ByteArrayOk(int[]? values) =>
        values != null && values.Length <= MaxArrayLength && values.All(v => v is >= 0 and <= 255);
}
