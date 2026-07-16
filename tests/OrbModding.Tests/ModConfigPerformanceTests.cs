using OrbModConfig;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigPerformanceTests
{
    [Fact]
    public void NavigationIntegrityCadenceDoesNotRunEveryFrame()
    {
        var remaining = 5.0f;

        for (var frame = 0; frame < 299; frame++)
            Assert.False(Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));

        Assert.True(Plugin.AdvanceCadence(ref remaining, 1.0f / 60.0f, 5.0f));
        Assert.InRange(remaining, 4.999f, 5.001f);
        Assert.False(Plugin.AdvanceCadence(ref remaining, -10.0f, 5.0f));
    }

    [Fact]
    public void NavigationIntegrityCadenceRecoversAfterLargeFrameGap()
    {
        var remaining = 1.0f;

        Assert.True(Plugin.AdvanceCadence(ref remaining, 3.0f, 5.0f));
        Assert.Equal(5.0f, remaining);
    }
}
