using System;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

/// <summary>
/// The pair health reaches the diagnostics bridge as three projection entries and is rebuilt from
/// them, so a health kind the reader rejects is a cycle the bridge discards whole.
/// </summary>
public sealed class AutoHarvestServiceProjectionTests
{
    /// <summary>
    /// Every declared kind, not just the ones in use. The reader validates the integer against a
    /// hard-coded upper bound, and a kind added past it fails silently — the bridge simply stops
    /// believing the worker.
    /// </summary>
    [Fact]
    public void EveryHealthKindSurvivesTheRoundTrip()
    {
        foreach (AutoHarvestPairHealthKind kind in Enum.GetValues(typeof(AutoHarvestPairHealthKind)))
        {
            var state = AutoHarvestCycleState.Restore(
                new LifecycleGeneration(1),
                AutoHarvestPair.FruitTree,
                hasPlannedAction: false,
                default,
                new AutoHarvestPairHealth(AutoHarvestPair.FruitTree, selected: true, kind),
                new AutoHarvestPairHealth(AutoHarvestPair.TreasureTree, selected: true, kind),
                default);

            var buffer = new ServiceStateProjectionWriteBuffer(
                ServiceStateProjectionSnapshot.MaximumEntryCount);
            var output = new ServiceStateProjectionBuilder(buffer);
            AutoHarvestServiceProjection.Write(in state, output);
            var projection = output.CaptureSnapshot();

            Assert.True(
                AutoHarvestServiceProjection.TryReadFruitHealth(in projection, out var fruit),
                $"{kind} did not survive the projection.");
            Assert.True(
                AutoHarvestServiceProjection.TryReadTreasureHealth(in projection, out var treasure),
                $"{kind} did not survive the projection.");
            Assert.Equal(kind, fruit.Kind);
            Assert.Equal(kind, treasure.Kind);
        }
    }
}
