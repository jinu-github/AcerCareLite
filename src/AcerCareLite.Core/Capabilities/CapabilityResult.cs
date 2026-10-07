namespace AcerCareLite.Core.Capabilities;

public sealed record CapabilityResult(CapabilityStatus Status, string Reason, string? Evidence = null)
{
    /// <summary>The only gate the UI may use before showing a control.</summary>
    public bool CanExposeControls => Status == CapabilityStatus.Supported;

    public static CapabilityResult Unknown(string reason) => new(CapabilityStatus.Unknown, reason);
    public static CapabilityResult Unsupported(string reason, string? evidence = null) => new(CapabilityStatus.Unsupported, reason, evidence);
    public static CapabilityResult Error(string reason, string? evidence = null) => new(CapabilityStatus.Error, reason, evidence);
    public static CapabilityResult Supported(string reason, string evidence) => new(CapabilityStatus.Supported, reason, evidence);
}
