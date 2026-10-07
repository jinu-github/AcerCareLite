using System.Management;
using AcerCareLite.Core.Acer;

namespace AcerCareLite.AcerHelper;

/// <summary>One open connection to the BatteryControl class and its first instance. Disposes everything it owns.</summary>
internal sealed class FirmwareSession : IDisposable
{
    private readonly IDisposable[] _owned;

    public FirmwareSession(ManagementClass cls, ManagementObject instance, string? instanceName, params IDisposable[] owned)
    {
        Class = cls;
        Instance = instance;
        InstanceName = instanceName;
        _owned = owned;
    }

    public ManagementClass Class { get; }
    public ManagementObject Instance { get; }
    public string? InstanceName { get; }

    public void Dispose()
    {
        Instance.Dispose();
        foreach (var o in _owned.Reverse()) o.Dispose();
        Class.Dispose();
    }
}

internal sealed record RawStatus(int? FunctionList, int[] Return, int[] Status);

/// <summary>
/// The only file in this project that reads the Acer WMI interface, and the only place the status getter is invoked.
/// It reaches exactly one method, a documented getter, with fixed arguments, after checking the live signature.
/// HelperGuardTests enforces the single call site and the single method name at source level.
/// </summary>
internal static class BatteryHealthReader
{
    private const string Ns = @"root\wmi";
    private const string ClassName = "BatteryControl";
    private const string GetStatusMethod = "GetBatteryHealthControlStatus";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { GetStatusMethod };

    internal sealed record ParamSpec(string Name, string CimType, bool IsArray);

    private static readonly ParamSpec[] StatusIn =
    {
        new("uBatteryNo", "UInt8", false), new("uFunctionQuery", "UInt8", false), new("uReserved", "UInt8", true)
    };
    private static readonly ParamSpec[] StatusOut =
    {
        new("uFunctionList", "UInt8", false), new("uReturn", "UInt8", true), new("uFunctionStatus", "UInt8", true)
    };

    /// <summary>Opens the class and its first instance. On any problem returns null and fills <paramref name="failure"/>.</summary>
    public static FirmwareSession? Open(string nonce, AcerHelperOperation operation, out AcerHelperResponse? failure)
    {
        failure = null;
        ManagementClass cls;
        try
        {
            var scope = new ManagementScope(Ns, new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Timeout = TimeSpan.FromSeconds(15)
            });
            scope.Connect();
            cls = new ManagementClass(scope, new ManagementPath(ClassName), null);
            cls.Get();
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.NotFound or ManagementStatus.InvalidClass)
        {
            failure = Failure(operation, nonce, AcerHelperOutcome.InterfaceMissing, ex, null);
            return null;
        }
        catch (Exception ex)
        {
            failure = Failure(operation, nonce, Map(ex), ex, null);
            return null;
        }

        ManagementObjectSearcher? searcher = null;
        ManagementObjectCollection? collection = null;
        ManagementObjectCollection.ManagementObjectEnumerator? enumerator = null;
        try
        {
            searcher = new ManagementObjectSearcher(cls.Scope, new ObjectQuery($"SELECT * FROM {ClassName}"));
            collection = searcher.Get();
            enumerator = collection.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                failure = Program.Basic(operation, nonce, AcerHelperOutcome.NoInstance, "BatteryControl has no instance.");
                enumerator.Dispose(); collection.Dispose(); searcher.Dispose(); cls.Dispose();
                return null;
            }

            // The enumerator, collection and searcher stay open for the life of the session, like the original single-read code.
            var instance = (ManagementObject)enumerator.Current;
            var name = instance["InstanceName"]?.ToString();
            return new FirmwareSession(cls, instance, Trim(name), searcher, collection, enumerator);
        }
        catch (Exception ex)
        {
            enumerator?.Dispose(); collection?.Dispose(); searcher?.Dispose(); cls.Dispose();
            failure = Failure(operation, nonce, Map(ex), ex, null);
            return null;
        }
    }

    public static AcerHelperResponse Read(string nonce)
    {
        const AcerHelperOperation op = AcerHelperOperation.ReadHealthStatus;
        using var session = Open(nonce, op, out var failure);
        if (session == null) return failure!;

        try
        {
            var problems = CheckMethodSchema(session.Class, GetStatusMethod, StatusIn, StatusOut);
            if (problems.Count > 0)
            {
                // The live layout is not the documented one: do not call into the firmware.
                return Program.Basic(op, nonce, AcerHelperOutcome.SchemaMismatch, "Signature differs from the documented layout.") with
                {
                    InstanceName = session.InstanceName,
                    SchemaProblems = problems.Take(16).Select(Program.Trim).ToArray()
                };
            }

            var raw = QueryStatus(session);
            return new AcerHelperResponse
            {
                Protocol = AcerHelperProtocol.Version,
                Nonce = nonce,
                Operation = op,
                Phase = AcerHelperPhase.Result,
                Outcome = AcerHelperOutcome.Ok,
                InstanceName = session.InstanceName,
                FunctionList = raw.FunctionList,
                Return = raw.Return,
                FunctionStatus = raw.Status
            };
        }
        catch (Exception ex)
        {
            return Failure(op, nonce, Map(ex), ex, session.InstanceName);
        }
    }

    // Fixed arguments, the same ones our earlier read-only probe used successfully on this machine.
    public static RawStatus QueryStatus(FirmwareSession session)
    {
        if (!Allowed.Contains(GetStatusMethod))
            throw new InvalidOperationException("Method is not on the allowlist.");

        using var args = session.Class.GetMethodParameters(GetStatusMethod);
        args["uBatteryNo"] = (byte)1;
        args["uFunctionQuery"] = (byte)1;
        args["uReserved"] = new byte[2];
        using var output = session.Instance.InvokeMethod(GetStatusMethod, args, null);

        return new RawStatus(
            ToBytes(output["uFunctionList"]).Select(b => (int?)b).FirstOrDefault(),
            ToBytes(output["uReturn"]).Select(b => (int)b).Take(8).ToArray(),
            ToBytes(output["uFunctionStatus"]).Select(b => (int)b).Take(8).ToArray());
    }

    /// <summary>Compares the live in/out parameters of one method with the expected layout. Metadata only; nothing is invoked.</summary>
    public static List<string> CheckMethodSchema(ManagementClass cls, string method, ParamSpec[] expectedIn, ParamSpec[] expectedOut)
    {
        var problems = new List<string>();
        MethodData data;
        try { data = cls.Methods[method]; }
        catch (ManagementException) { problems.Add("method missing: " + method); return problems; }

        Compare("in", Describe(data.InParameters), expectedIn, problems);
        Compare("out", Describe(data.OutParameters), expectedOut, problems);
        return problems;
    }

    private static List<ParamSpec> Describe(ManagementBaseObject? o)
    {
        var list = new List<ParamSpec>();
        if (o == null) return list;
        foreach (PropertyData p in o.Properties)
            if (!p.Name.Equals("ReturnValue", StringComparison.OrdinalIgnoreCase))
                list.Add(new ParamSpec(p.Name, p.Type.ToString(), p.IsArray));
        return list;
    }

    private static void Compare(string dir, List<ParamSpec> actual, ParamSpec[] expected, List<string> problems)
    {
        foreach (var e in expected)
        {
            var a = actual.FirstOrDefault(x => x.Name == e.Name);
            if (a == null) problems.Add($"{dir}: missing {e.Name}");
            else if (a.CimType != e.CimType || a.IsArray != e.IsArray)
                problems.Add($"{dir}: {e.Name} is {a.CimType}{(a.IsArray ? "[]" : "")}, expected {e.CimType}{(e.IsArray ? "[]" : "")}");
        }
        foreach (var a in actual.Where(a => expected.All(e => e.Name != a.Name)))
            problems.Add($"{dir}: unexpected parameter {a.Name}");
    }

    public static AcerHelperOutcome Map(Exception ex) => ex switch
    {
        UnauthorizedAccessException => AcerHelperOutcome.AccessDenied,
        ManagementException { ErrorCode: ManagementStatus.AccessDenied } => AcerHelperOutcome.AccessDenied,
        ManagementException => AcerHelperOutcome.WmiError,
        _ => AcerHelperOutcome.Unexpected
    };

    public static AcerHelperResponse Failure(AcerHelperOperation op, string nonce, AcerHelperOutcome outcome, Exception ex, string? instanceName) =>
        Program.Basic(op, nonce, outcome, ex.GetType().Name + ": " + ex.Message) with { InstanceName = Trim(instanceName) };

    public static string? Trim(string? text) => text == null ? null : Program.Trim(text);

    public static byte[] ToBytes(object? v) => v switch
    {
        null => Array.Empty<byte>(),
        byte[] b => b,
        Array a => a.Cast<object?>().Select(x => Convert.ToByte(x)).ToArray(),
        _ => new[] { Convert.ToByte(v) }
    };
}
