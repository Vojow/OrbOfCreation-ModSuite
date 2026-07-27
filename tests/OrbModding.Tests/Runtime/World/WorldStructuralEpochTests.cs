using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Authored content is read once per lifecycle epoch, not once per cycle.
/// </summary>
/// <remarks>
/// <para>
/// What a plot's author wrote and what completing an action applies cannot change while the game is
/// running, so reading them four times a second is four hundred walks a minute for an answer that
/// could not have moved. The epoch is exactly the fact that says when it can have.
/// </para>
/// <para>
/// Every case here drives the real collector across two passes, and proves a pass was skipped by
/// emptying the registries between them: rows that survive an empty registry are rows nobody re-read.
/// The played-state categories fed by the same registries are the control — they follow the emptying,
/// which is what makes the surviving rows a skip rather than a stuck collector.
/// </para>
/// </remarks>
public sealed class WorldStructuralEpochTests : IDisposable
{
    private static readonly Guid PlotId = new("3f1c1b7e-0000-4000-8000-00000000ce01");
    private static readonly Guid ActionId = new("3f1c1b7e-0000-4000-8000-00000000ce02");

    public WorldStructuralEpochTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void AuthoredContentIsReadOnTheFirstPass()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };

        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// The point of the whole thing: within one run of the game the native walks do not happen again.
    /// </summary>
    [Fact]
    public void AuthoredContentIsNotReadAgainWithinOneEpoch()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);

        ClearRegistries();
        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// The control for the case above. The same registries feed categories that are not authored
    /// content, and those do follow the emptying — so the surviving rows are a skipped read rather
    /// than a collector that stopped reading anything.
    /// </summary>
    [Fact]
    public void PlayedStateIsStillReadOnASkippedPass()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);
        Assert.Equal(1, GameWorldFrameDeriver.Build(frame).PlotNodes.Count);

        ClearRegistries();
        collector.Collect(frame);

        Assert.Equal(0, GameWorldFrameDeriver.Build(frame).PlotNodes.Count);
    }

    /// <summary>
    /// A lifecycle boundary is the one thing that can change authored content, and it is the one thing
    /// that makes the collector look again.
    /// </summary>
    [Fact]
    public void AuthoredContentIsReadAgainWhenTheEpochMoves()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(frame);

        ClearRegistries();
        frame.CollectedAtEpoch = 6;
        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(0, world.PlotAuthoring.Count);
        Assert.Equal(0, world.PlotPhaseDescriptors.Count);
        Assert.Equal(0, world.EffectBlocks.Count);
        Assert.Equal(0, world.EntityRequirements.Count);
    }

    /// <summary>
    /// A skip is decided on the frame as well as the epoch. Two frames under one epoch are two sets of
    /// buffers and only one of them was filled; skipping the other on the epoch alone would hand it an
    /// empty table and call it collected.
    /// </summary>
    [Fact]
    public void AuthoredContentIsReadIntoEveryFrameThatAsksForIt()
    {
        Author();
        var collector = new GameWorldCollector();
        collector.Collect(new GameWorldCycleFrame { CollectedAtEpoch = 5 });

        var second = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        collector.Collect(second);
        var world = GameWorldFrameDeriver.Build(second);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// A frame re-read within one epoch is reset rather than appended to, exactly as it is on the pass
    /// that first filled it. Skipping means skipping the read, and never means letting two reads
    /// stack.
    /// </summary>
    [Fact]
    public void AFrameReadTwiceHoldsOneCopyOfTheAuthoredContent()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };

        collector.Collect(frame);
        frame.CollectedAtEpoch = 6;
        collector.Collect(frame);
        var world = GameWorldFrameDeriver.Build(frame);

        Assert.Equal(1, world.PlotAuthoring.Count);
        Assert.Equal(3, world.PlotPhaseDescriptors.Count);
        Assert.Equal(1, world.EffectBlocks.Count);
        Assert.Equal(1, world.EntityRequirements.Count);
    }

    /// <summary>
    /// The report says what the buffer holds, not what this pass did. A skipped category reporting
    /// nothing would read to an operator as content the build could not reach.
    /// </summary>
    [Fact]
    public void ASkippedPassStillReportsTheAuthoredContentItKept()
    {
        Author();
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 5 };
        var first = collector.Collect(frame);

        ClearRegistries();
        var second = collector.Collect(frame);

        Assert.Equal(first.For("plot authoring").Sampled, second.For("plot authoring").Sampled);
        Assert.Equal(first.For("effect blocks").Sampled, second.For("effect blocks").Sampled);
        Assert.Equal(
            first.For("entity requirements").Sampled, second.For("entity requirements").Sampled);
        Assert.Equal(
            WorldCategoryOutcome.Collected, second.For("plot authoring").Outcome);
        Assert.Equal(
            WorldCategoryOutcome.Collected, second.For("effect blocks").Outcome);
        Assert.Equal(
            WorldCategoryOutcome.Collected, second.For("entity requirements").Outcome);
    }

    /// <summary>
    /// One plot authoring the three phases the game ships, and one action whose completion applies one
    /// block. Enough shape for both structural readers to produce rows; the terms themselves are the
    /// safety audit's business, not this file's.
    /// </summary>
    private static void Author()
    {
        var upgrade = new global::UpgradeSO { maxLevel = 1 };
        var research = new global::ResearchSO();
        upgrade.prerequisitesPerLevel.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = research,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 6d },
        });
        global::UpgradeSO.All.Add(upgrade);
        global::ResearchSO.All.Add(research);

        var action = new global::PlotNodeActionSO();
        action.SetGuid(ActionId);
        action.completeEffects.Add(new global::InstantEffectBlock());

        var plot = new global::PlotNodeSO();
        plot.SetGuid(PlotId);
        plot.phaseInfos = new List<global::PlotNodePhaseInfo>
        {
            new global::PlotNodePhaseInfo { phase = global::PlotNodePhases.Idle },
            new global::PlotNodePhaseInfo { phase = global::PlotNodePhases.Growing },
            new global::PlotNodePhaseInfo { phase = global::PlotNodePhases.Resting },
        };

        global::PlotNodeSO.All.Add(plot);
        global::PlotNodeActionSO.All.Add(action);
    }

    private static void ClearRegistries()
    {
        global::PlotNodeSO.All.Clear();
        global::PlotNodeActionSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::ResearchSO.All.Clear();
    }
}
