using OrbAutomata;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyReliabilityTests
{
    [Fact]
    [Trait("Category", "HeadlessE2E")]
    [Trait("Category", "AutoBuyReliability")]
    public void LiveQueueCapacityIncrease_IsConsumedWithoutLifecycleReload()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 4,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure),
            });

        Assert.True(simulation.RunUntil(world => world.QueueCount == 3, maximumFrames: 20));
        var submissionsBeforeIncrease = simulation.World.TotalSubmitted;

        simulation.World.SetQueueCapacity(8);

        Assert.True(simulation.RunUntil(world => world.QueueCount == 7, maximumFrames: 30),
            "Auto Buy did not consume newly available native queue capacity.");
        Assert.Equal(submissionsBeforeIncrease + 4, simulation.World.TotalSubmitted);
        Assert.Equal(7, simulation.World.QueueCount);
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    [Trait("Category", "AutoBuyReliability")]
    public void LiveReserveIncreaseStopsPurchasesAndLaterDecreaseResumesThem()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 12,
            new[]
            {
                new SimulatedCandidateSpec(
                    "structure",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 10.0),
            },
            initialResourceQuantity: 100.0);
        simulation.Config.LeaveQueueSlots.Value = 0;

        Assert.True(simulation.RunUntil(world => world.TotalSubmitted == 3, maximumFrames: 20));
        simulation.Config.AbsoluteReserve.Value = "70";
        simulation.RunFrames(20);

        Assert.Equal(3, simulation.World.TotalSubmitted);
        Assert.Equal(70.0, simulation.World.ResourceQuantity, 6);

        simulation.Config.AbsoluteReserve.Value = "0";

        Assert.True(simulation.RunUntil(world => world.TotalSubmitted > 3, maximumFrames: 20),
            "Auto Buy did not resume after the live reserve was relaxed.");
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
    }
}
