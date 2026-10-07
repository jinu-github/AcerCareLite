using System.Management;

namespace AcerBatteryProbe;

/// <summary>
/// The only place in this tool that calls into firmware-backed WMI methods.
/// Exactly two methods are reachable, both documented as getters, each with fixed arguments.
/// The method allowlist is enforced here at runtime and by ProbeGuardTests at source level.
/// </summary>
internal sealed class BatteryControlReader
{
    private const string Ns = @"root\wmi";
    private const string ClassName = "BatteryControl";
    private const string GetStatusMethod = "GetBatteryHealthControlStatus";
    private const string GetInfoMethod = "GetBattInfoInterface";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { GetStatusMethod, GetInfoMethod };

    private static readonly ParamSpec[] StatusIn =
    {
        new("uBatteryNo", "UInt8", false), new("uFunctionQuery", "UInt8", false), new("uReserved", "UInt8", true)
    };
    private static readonly ParamSpec[] StatusOut =
    {
        new("uFunctionList", "UInt8", false), new("uReturn", "UInt8", true), new("uFunctionStatus", "UInt8", true)
    };
    private static readonly ParamSpec[] InfoIn =
    {
        new("uBatteryInfoIndex", "UInt32", false), new("uBatteryNo", "UInt32", false)
    };
    private static readonly ParamSpec[] InfoOut = { new("uReturn", "UInt32", false) };

    private readonly ManagementScope _scope;
    private readonly ManagementClass _class;

    public BatteryControlReader()
    {
        _scope = new ManagementScope(Ns, new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Timeout = TimeSpan.FromSeconds(15)
        });
        _scope.Connect();
        _class = new ManagementClass(_scope, new ManagementPath(ClassName), null);
        _class.Get();
    }

    public string? FindInstanceName()
    {
        using var s = new ManagementObjectSearcher(_scope, new ObjectQuery($"SELECT InstanceName FROM {ClassName}"));
        foreach (ManagementBaseObject o in s.Get())
            using (o) return o["InstanceName"]?.ToString();
        return null;
    }

    /// <summary>Compares the live method signatures with what the open-source driver documents. No invocation.</summary>
    public IReadOnlyList<SchemaCheck> CheckSchemas() => new[]
    {
        CheckSchema(GetStatusMethod, StatusIn, StatusOut),
        CheckSchema(GetInfoMethod, InfoIn, InfoOut)
    };

    private SchemaCheck CheckSchema(string method, ParamSpec[] expectedIn, ParamSpec[] expectedOut)
    {
        try
        {
            var md = _class.Methods[method];
            var problems = new List<string>();
            Compare("in", Describe(md.InParameters), expectedIn, problems);
            Compare("out", Describe(md.OutParameters), expectedOut, problems);
            return new SchemaCheck(method, problems.Count == 0, problems.Count == 0 ? "matches documented signature" : string.Join("; ", problems));
        }
        catch (Exception ex)
        {
            return new SchemaCheck(method, false, ex.Message);
        }
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

    // Fixed arguments, taken from what the open-source driver says Acer's own software sends.
    public StatusResult ReadHealthControlStatus()
    {
        using var o = Invoke(GetStatusMethod, p =>
        {
            p["uBatteryNo"] = (byte)1;
            p["uFunctionQuery"] = (byte)1;
            p["uReserved"] = new byte[2];
        });
        var raw = new Dictionary<string, string?>();
        foreach (PropertyData pd in o.Properties) raw[pd.Name] = Format(pd.Value);
        return new StatusResult(
            ToBytes(o["uFunctionList"]).FirstOrDefault(),
            ToBytes(o["uReturn"]),
            ToBytes(o["uFunctionStatus"]),
            raw);
    }

    public TemperatureResult ReadBatteryTemperature()
    {
        using var o = Invoke(GetInfoMethod, p =>
        {
            p["uBatteryInfoIndex"] = (uint)8;
            p["uBatteryNo"] = (uint)1;
        });
        var raw = Convert.ToUInt32(o["uReturn"]);
        var celsius = (raw - 2731) / 10.0; // same conversion as the reference driver (tenths of kelvin)
        return new TemperatureResult(raw, celsius, celsius is > -20 and < 90);
    }

    private ManagementBaseObject Invoke(string method, Action<ManagementBaseObject> fillArguments)
    {
        if (!Allowed.Contains(method))
            throw new InvalidOperationException($"Method is not on the allowlist: {method}");

        using var s = new ManagementObjectSearcher(_scope, new ObjectQuery($"SELECT * FROM {ClassName}"));
        foreach (ManagementObject inst in s.Get())
        {
            using (inst)
            {
                var inParams = _class.GetMethodParameters(method);
                fillArguments(inParams);
                return inst.InvokeMethod(method, inParams, null);
            }
        }
        throw new InvalidOperationException("No BatteryControl instance found.");
    }

    private static byte[] ToBytes(object? v) => v switch
    {
        null => Array.Empty<byte>(),
        byte[] b => b,
        Array a => a.Cast<object?>().Select(x => Convert.ToByte(x)).ToArray(),
        _ => new[] { Convert.ToByte(v) }
    };

    private static string? Format(object? v) => v switch
    {
        null => null,
        Array a => "[" + string.Join(", ", a.Cast<object?>().Select(Format)) + "]",
        _ => v.ToString()
    };
}
