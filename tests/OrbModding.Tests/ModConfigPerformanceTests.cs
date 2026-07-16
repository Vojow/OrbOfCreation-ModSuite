using OrbModConfig;
using System.Collections.Generic;
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

    [Fact]
    public void DeadUiReferencesArePrunedInPlaceAndDetachExactlyOnce()
    {
        var alive = new FakeReference(true, "alive");
        var deadA = new FakeReference(false, "dead-a");
        var deadB = new FakeReference(false, "dead-b");
        var references = new List<FakeReference> { deadA, alive, deadB };
        var detached = new List<string>();

        var removed = ModConfigUiShell.PruneDead(references, item => item.Alive, item => detached.Add(item.Name));

        Assert.Equal(2, removed);
        Assert.Same(alive, Assert.Single(references));
        Assert.Equal(new[] { "dead-b", "dead-a" }, detached);
        Assert.Equal(0, ModConfigUiShell.PruneDead(references, item => item.Alive, item => detached.Add(item.Name)));
        Assert.Equal(2, detached.Count);
    }

    private sealed record FakeReference(bool Alive, string Name);
}
