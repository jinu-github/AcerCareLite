namespace AcerHardwareExplorer;

internal sealed record PropertyInfoDto(string Name, string CimType, bool IsArray, string? Description);

internal sealed record MethodInfoDto(
    string Name, string? Description,
    IReadOnlyList<PropertyInfoDto> InParameters,
    IReadOnlyList<PropertyInfoDto> OutParameters,
    IReadOnlyDictionary<string, string> Qualifiers);

internal sealed record ClassInfoDto(
    string Namespace, string Name, string? Guid, string? Description,
    IReadOnlyList<string> MatchedBy,
    IReadOnlyList<PropertyInfoDto> Properties,
    IReadOnlyList<MethodInfoDto> Methods);

internal sealed record ClassRef(string Namespace, string Name, string? Guid, int MethodCount);

internal sealed record InstanceDto(string Source, IReadOnlyDictionary<string, string?> Values);

internal sealed record ProbeError(string Where, string Message);

internal sealed record Options(string OutDir, bool ReadAcerInstances, bool InspectAll);

internal sealed class DiscoveryReport
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public string ToolVersion { get; init; } = "0.1.0";
    public bool ReadOnly { get; init; } = true;
    public bool IsElevated { get; set; }
    public string OsDescription { get; set; } = "";
    public Dictionary<string, Dictionary<string, string?>> Identity { get; } = new();
    public List<string> Namespaces { get; } = new();
    public List<ClassRef> AcerNamedClasses { get; } = new();
    public List<ClassRef> RootWmiGuidInventory { get; } = new();
    public List<ClassInfoDto> InspectedClasses { get; } = new();
    public List<Dictionary<string, string?>> AcpiWmiDevices { get; } = new();
    public List<Dictionary<string, string?>> AcerServices { get; } = new();
    public List<Dictionary<string, string?>> AcerDrivers { get; } = new();
    public List<InstanceDto> BatteryReads { get; } = new();
    public List<InstanceDto> AcerInstanceReads { get; } = new();
    public List<ProbeError> Errors { get; } = new();
}
