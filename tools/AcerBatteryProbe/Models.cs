namespace AcerBatteryProbe;

internal sealed record ParamSpec(string Name, string CimType, bool IsArray);

internal sealed record SchemaCheck(string Method, bool Ok, string Detail);

internal sealed record StatusResult(byte FunctionList, byte[] Ret, byte[] FunctionStatus, IReadOnlyDictionary<string, string?> RawOut)
{
    public bool HealthModeSupported => (FunctionList & 0x01) != 0;
    public bool CalibrationSupported => (FunctionList & 0x02) != 0;
    public bool? HealthModeEnabled => FunctionStatus.Length > 0 ? FunctionStatus[0] != 0 : null;
    public bool? CalibrationEnabled => FunctionStatus.Length > 1 ? FunctionStatus[1] != 0 : null;
}

// Interpretation per the open-source driver: value is in tenths of a kelvin. Not verified on this BIOS.
internal sealed record TemperatureResult(uint Raw, double Celsius, bool Plausible);

internal sealed class ProbeReport
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public string ToolVersion { get; init; } = "0.1.0";
    public string Note { get; set; } = "";
    public bool IsElevated { get; set; }
    public Dictionary<string, string?> Machine { get; } = new();
    public string? InstanceName { get; set; }
    public List<SchemaCheck> SchemaChecks { get; } = new();
    public bool InvocationsAttempted { get; set; }
    public StatusResult? Status { get; set; }
    public TemperatureResult? Temperature { get; set; }
    public Dictionary<string, Dictionary<string, string?>> BatteryContext { get; } = new();
    public List<string> Decoded { get; } = new();
    public List<string> Errors { get; } = new();
}
