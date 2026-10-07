#if ACER_WRITE_ENABLED
using System.Management;
using AcerCareLite.Acer;
using AcerCareLite.Core.Acer;

namespace AcerCareLite.AcerHelper;

/// <summary>
/// Compiled only when the build was made with -p:AcerWriteEnabled=true. The ONLY file that names the setter and the only
/// second firmware call site. It can change exactly one thing: the health-mode bit.
/// Order of operations (HelperGuardTests pins it in source order): location check, schemas, pre-read, preconditions,
/// WriteSent marker, the single setter call, bounded readback.
/// </summary>
internal static class BatteryHealthWriter
{
    private const string SetMethod = "SetBatteryHealthControl";

    // The one and only function bit this phase may change: battery health mode (the ~80 % charge limit).
    // Care Center's other function (bit 1 in the function list) is never addressed anywhere in this code base.
    private const int HealthModeMask = 0x01;
    private const int BatteryNumber = 1;

    // Generous on purpose: an earlier observation suggested the firmware may take on the order of ten seconds to apply a change.
    // Only the failure path is slower: a successful change ends about half a second after the firmware reports it.
    private static readonly TimeSpan ReadbackWindow = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    private static readonly BatteryHealthReader.ParamSpec[] SetIn =
    {
        new("uBatteryNo", "UInt8", false), new("uFunctionMask", "UInt8", false),
        new("uFunctionStatus", "UInt8", false), new("uReservedIn", "UInt8", true)
    };
    private static readonly BatteryHealthReader.ParamSpec[] SetOut =
    {
        new("uReturn", "UInt16", false), new("uReservedOut", "UInt16", false)
    };

    private static readonly BatteryHealthReader.ParamSpec[] GetIn =
    {
        new("uBatteryNo", "UInt8", false), new("uFunctionQuery", "UInt8", false), new("uReserved", "UInt8", true)
    };
    private static readonly BatteryHealthReader.ParamSpec[] GetOut =
    {
        new("uFunctionList", "UInt8", false), new("uReturn", "UInt8", true), new("uFunctionStatus", "UInt8", true)
    };

    public static AcerHelperResponse Execute(string nonce, bool enable, Func<AcerHelperResponse, bool> send)
    {
        const AcerHelperOperation op = AcerHelperOperation.SetHealthMode;

        // 1. The helper checks its own location, and the application's, itself. A patched app cannot skip this.
        var self = Environment.ProcessPath ?? "";
        var helperLocation = new HelperLocationGate(self).Current;
        if (!helperLocation.IsProtected)
            return Program.Basic(op, nonce, AcerHelperOutcome.WriteNotPermitted, Program.Trim("Helper location is not protected. " + helperLocation.Reason));
        var appLocation = new HelperLocationGate(Path.Combine(AppContext.BaseDirectory, Program.MainExeFileName)).Current;
        if (!appLocation.IsProtected)
            return Program.Basic(op, nonce, AcerHelperOutcome.WriteNotPermitted, Program.Trim("Application location is not protected. " + appLocation.Reason));

        using var session = BatteryHealthReader.Open(nonce, op, out var failure);
        if (session == null) return failure!;

        try
        {
            // 2. Both live signatures must match exactly what the schema inspector captured from this PC.
            var problems = BatteryHealthReader.CheckMethodSchema(session.Class, "GetBatteryHealthControlStatus", GetIn, GetOut);
            problems.AddRange(BatteryHealthReader.CheckMethodSchema(session.Class, SetMethod, SetIn, SetOut));
            if (problems.Count > 0)
                return Program.Basic(op, nonce, AcerHelperOutcome.SchemaMismatch, "Signature differs from the captured layout.") with
                {
                    InstanceName = session.InstanceName,
                    SchemaProblems = problems.Take(16).Select(Program.Trim).ToArray()
                };

            // 3. Pre-read and strict preconditions.
            RawStatus before;
            try { before = BatteryHealthReader.QueryStatus(session); }
            catch (Exception ex) { return BatteryHealthReader.Failure(op, nonce, BatteryHealthReader.Map(ex), ex, session.InstanceName); }

            var precondition = CheckPrecondition(before);
            if (precondition != null)
                return WithBefore(Program.Basic(op, nonce, AcerHelperOutcome.PreconditionFailed, precondition), before, session.InstanceName);

            var target = enable ? 1 : 0;
            var preRead = AcerHelperProtocol.Progress(nonce, op, AcerHelperPhase.PreReadOk) with
            {
                BeforeFunctionList = before.FunctionList,
                BeforeStatus = before.Status
            };
            if (!send(preRead))
                return Program.Basic(op, nonce, AcerHelperOutcome.Unexpected, "Could not report progress; the setter was not called.");

            if (before.Status[0] == target)
                return WithBefore(Program.Basic(op, nonce, AcerHelperOutcome.NoChangeNeeded, "Already in the requested state; the setter was not called."), before, session.InstanceName)
                    with { FunctionList = before.FunctionList, FunctionStatus = before.Status, Return = before.Return };

            // 4. Announce the write BEFORE making it. If the announcement cannot be delivered, nothing is written.
            if (!send(AcerHelperProtocol.Progress(nonce, op, AcerHelperPhase.WriteSent)))
                return Program.Basic(op, nonce, AcerHelperOutcome.Unexpected, "Could not report progress; the setter was not called.");

            // 5. The single setter call.
            string? setterError = null;
            int? setReturn = null, setReservedOut = null;
            try
            {
                using var args = session.Class.GetMethodParameters(SetMethod);
                args["uBatteryNo"] = (byte)BatteryNumber;
                args["uFunctionMask"] = (byte)HealthModeMask;
                args["uFunctionStatus"] = (byte)target;
                args["uReservedIn"] = new byte[5];
                using var output = session.Instance.InvokeMethod(SetMethod, args, null);
                setReturn = ToInt(output["uReturn"]);
                setReservedOut = ToInt(output["uReservedOut"]);
            }
            catch (Exception ex)
            {
                setterError = Program.Trim(ex.GetType().Name + ": " + ex.Message);
            }

            // 6. Bounded readback. The firmware's answer is the truth; the setter's return value is informational only.
            var (after, attempts) = ReadBack(session, target);

            var response = new AcerHelperResponse
            {
                Protocol = AcerHelperProtocol.Version,
                Nonce = nonce,
                Operation = op,
                Phase = AcerHelperPhase.Result,
                Outcome = setterError == null ? AcerHelperOutcome.Ok : AcerHelperOutcome.SetterError,
                Detail = setterError,
                InstanceName = session.InstanceName,
                BeforeFunctionList = before.FunctionList,
                BeforeStatus = before.Status,
                SetReturn = setReturn,
                SetReservedOut = setReservedOut,
                ReadbackAttempts = attempts
            };
            return after == null
                ? response
                : response with { FunctionList = after.FunctionList, Return = after.Return, FunctionStatus = after.Status };
        }
        catch (Exception ex)
        {
            return BatteryHealthReader.Failure(op, nonce, BatteryHealthReader.Map(ex), ex, session.InstanceName);
        }
    }

    // Strict: this phase writes only when the firmware looks exactly like what we observed on the reference PC.
    private static string? CheckPrecondition(RawStatus before)
    {
        if (before.FunctionList is not { } functions) return "The firmware did not report a function list.";
        if ((functions & HealthModeMask) == 0) return "The firmware does not offer battery health mode.";
        if (before.Status.Length != 5) return $"Expected 5 status bytes, got {before.Status.Length}.";
        if (before.Status[0] is not (0 or 1)) return $"The health-mode byte is {before.Status[0]}, expected 0 or 1.";
        for (var i = 1; i < before.Status.Length; i++)
            if (before.Status[i] != 0) return $"Status byte {i} is {before.Status[i]}, expected 0.";
        return null;
    }

    private static AcerHelperResponse WithBefore(AcerHelperResponse r, RawStatus before, string? instanceName) => r with
    {
        InstanceName = instanceName,
        BeforeFunctionList = before.FunctionList,
        BeforeStatus = before.Status
    };

    private static (RawStatus? After, int Attempts) ReadBack(FirmwareSession session, int target)
    {
        RawStatus? last = null;
        var attempts = 0;
        var deadline = DateTime.UtcNow + ReadbackWindow;
        while (true)
        {
            attempts++;
            try { last = BatteryHealthReader.QueryStatus(session); }
            catch (Exception) { /* keep the previous sample, if any, and try again */ }

            if (last is { Status.Length: > 0 } && last.Status[0] == target)
            {
                // Matched: wait briefly and sample once more, so a change in any other byte is seen too.
                Thread.Sleep(SettleDelay);
                attempts++;
                try { last = BatteryHealthReader.QueryStatus(session); }
                catch (Exception) { /* the matched sample stays */ }
                return (last, attempts);
            }

            if (DateTime.UtcNow >= deadline) return (last, attempts);
            Thread.Sleep(PollInterval);
        }
    }

    private static int? ToInt(object? v) => v == null ? null : Convert.ToInt32(v);
}
#else
using AcerCareLite.Core.Acer;

namespace AcerCareLite.AcerHelper;

/// <summary>Default builds contain no write code at all: the helper refuses every change request.</summary>
internal static class BatteryHealthWriter
{
    public static AcerHelperResponse Execute(string nonce, bool enable, Func<AcerHelperResponse, bool> send) =>
        Program.Basic(AcerHelperOperation.SetHealthMode, nonce, AcerHelperOutcome.WriteNotPermitted, "Write support is not built into this helper.");
}
#endif
