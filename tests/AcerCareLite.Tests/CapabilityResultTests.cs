using AcerCareLite.Core.Capabilities;
using Xunit;

namespace AcerCareLite.Tests;

public class CapabilityResultTests
{
    [Fact]
    public void Only_Supported_exposes_controls()
    {
        Assert.True(CapabilityResult.Supported("ok", "evidence").CanExposeControls);
        Assert.False(CapabilityResult.Unknown("not probed").CanExposeControls);
        Assert.False(CapabilityResult.Unsupported("no interface").CanExposeControls);
        Assert.False(CapabilityResult.Error("probe failed").CanExposeControls);
    }
}
