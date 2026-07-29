using System.Collections.Generic;
using OrbAutomata;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The capture port's only behaviour of its own: saying what a pass managed, without saying it four
/// times a second.
/// </summary>
public sealed class AutomataWorldCapturePortTests
{
    [Fact]
    public void AHealthyPassIsAnnouncedOnceEvenAsTheWorldGrows()
    {
        var announced = new List<string>();
        var port = new AutomataWorldCapturePort(
            new GameWorldCollector(),
            () => 1,
            () => 1,
            r => announced.Add(r.Describe()));
        var frame = new GameWorldCycleFrame();

        port.Collect(frame);
        global::ResourceSO.All.Add(new global::ResourceSO { uuid = System.Guid.NewGuid().ToString() });
        port.Collect(frame);

        try
        {
            var line = Assert.Single(announced);
            Assert.StartsWith("World collection complete", line);
        }
        finally
        {
            global::ResourceSO.All.Clear();
        }
    }

    /// <summary>
    /// The reason this exists: without it a build that renamed one member reaches the operator as a
    /// count of unavailable categories and no member name anywhere.
    /// </summary>
    [Fact]
    public void AShortfallIsAnnouncedWithItsCategoryAndReason()
    {
        var announced = new List<string>();
        var port = new AutomataWorldCapturePort(
            new GameWorldCollector(_ => null),
            () => 1,
            () => 1,
            r => announced.Add(r.Describe()));

        port.Collect(new GameWorldCycleFrame());
        port.Collect(new GameWorldCycleFrame());

        var line = Assert.Single(announced);
        Assert.StartsWith("World collection incomplete", line);
        Assert.Contains("resources", line);
    }
}
