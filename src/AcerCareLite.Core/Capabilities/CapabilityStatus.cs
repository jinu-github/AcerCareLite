namespace AcerCareLite.Core.Capabilities;

/// <summary>Outcome of probing a hardware feature. Anything other than Supported keeps UI controls hidden.</summary>
public enum CapabilityStatus { Unknown = 0, Supported, Unsupported, Error }
