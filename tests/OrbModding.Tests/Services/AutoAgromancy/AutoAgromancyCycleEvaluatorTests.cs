using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoAgromancy;

public sealed class AutoAgromancyCycleEvaluatorTests
{
    [Fact]
    public void DirectIncreasePlansOneExactPairAndSurvivesARejectedAttempt()
    {
        var ids = PairIds.Create();
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));

        Assert.Empty(Plan(World(ids, level: 1), ref state));

        var first = Assert.Single(Plan(World(ids, level: 2), ref state));
        Assert.Equal(ids.Action, first.ActionId);
        Assert.Equal(ids.Element, first.ElementId);
        Assert.Equal(2, first.ObservedLevel);
        Assert.Equal(3, first.TargetLevel);

        var retry = Assert.Single(Plan(World(ids, level: 2), ref state));
        Assert.Equal(first.Fingerprint, retry.Fingerprint);
    }

    [Fact]
    public void AcceptedLevelAndRemovalRefreshTheObservationBaseline()
    {
        var ids = PairIds.Create();
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(World(ids, level: 1), ref state));
        Assert.Single(Plan(World(ids, level: 2), ref state));

        Assert.Empty(Plan(World(ids, level: 3), ref state));
        Assert.Empty(Plan(World(ids, level: 0), ref state));
        Assert.Single(Plan(World(ids, level: 1), ref state));
    }

    [Fact]
    public void PlotAndVerifiedHarvestEpochsStartDeterministicBoundedSweeps()
    {
        var first = PairIds.Create();
        var second = PairIds.Create();
        if (Compare(second, first) < 0) (first, second) = (second, first);
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        var baseline = World(first, second, plotEpoch: 0, harvestEpoch: 0);
        Assert.Empty(Plan(baseline, ref state));

        var plotAction = Assert.Single(
            Plan(World(first, second, plotEpoch: 1, harvestEpoch: 0), ref state));
        var nextAction = Assert.Single(
            Plan(
                World(
                    new[] { (first, plotAction.TargetLevel), (second, 1) },
                    plotEpoch: 1,
                    harvestEpoch: 0),
                ref state));
        Assert.Equal(first.Action, plotAction.ActionId);
        Assert.Equal(second.Action, nextAction.ActionId);
        Assert.Empty(
            Plan(
                World(
                    new[]
                    {
                        (first, plotAction.TargetLevel),
                        (second, nextAction.TargetLevel),
                    },
                    plotEpoch: 1,
                    harvestEpoch: 0),
                ref state));

        var harvestAction = Assert.Single(
            Plan(
                World(
                    new[]
                    {
                        (first, 1),
                        (second, nextAction.TargetLevel),
                    },
                    plotEpoch: 1,
                    harvestEpoch: 1),
                ref state));
        Assert.Equal(first.Action, harvestAction.ActionId);
    }

    [Fact]
    public void TriggerSweepRetriesTheSamePairUntilItsPlannedTargetIsObserved()
    {
        var first = PairIds.Create();
        var second = PairIds.Create();
        if (Compare(second, first) < 0) (first, second) = (second, first);
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(World(first, second), ref state));

        var planned = Assert.Single(
            Plan(World(first, second, plotEpoch: 1), ref state));
        var retry = Assert.Single(
            Plan(World(first, second, plotEpoch: 1), ref state));

        Assert.Equal(first.Action, planned.ActionId);
        Assert.Equal(first.Action, retry.ActionId);
        Assert.Equal(planned.Fingerprint, retry.Fingerprint);
    }

    [Fact]
    public void RepeatedEpochsCoalesceAndPairDisappearanceDoesNotReplayWork()
    {
        var first = PairIds.Create();
        var second = PairIds.Create();
        if (Compare(second, first) < 0) (first, second) = (second, first);
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(World(first, second), ref state));

        var initial = Assert.Single(
            Plan(World(first, second, plotEpoch: 1), ref state));
        var coalesced = Assert.Single(
            Plan(
                World(
                    new[] { (first, initial.TargetLevel), (second, 1) },
                    plotEpoch: 2,
                    harvestEpoch: 0),
                ref state));
        Assert.Equal(first.Action, initial.ActionId);
        Assert.Equal(second.Action, coalesced.ActionId);
        Assert.Empty(Plan(World(), ref state));
        Assert.Empty(Plan(World(), ref state));
    }

    [Fact]
    public void PairInsertedDuringSweepIsVisitedWithoutRestartingTheCursor()
    {
        var first = new PairIds(
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            Guid.NewGuid());
        var inserted = new PairIds(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            Guid.NewGuid());
        var last = new PairIds(
            Guid.Parse("f0000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            Guid.NewGuid());
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(World(first, last), ref state));

        var initial = Assert.Single(
            Plan(World(first, last, plotEpoch: 1), ref state));
        var changedWorld = World(
            new[] { (first, initial.TargetLevel), (inserted, 1), (last, 1) },
            plotEpoch: 1,
            harvestEpoch: 0);
        var added = Assert.Single(Plan(changedWorld, ref state));

        Assert.Equal(first.Action, initial.ActionId);
        Assert.Equal(inserted.Action, added.ActionId);
    }

    [Fact]
    public void SameCountReplacementRestartsSweepBeforeTheNewLeadingPair()
    {
        var first = new PairIds(
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            Guid.NewGuid());
        var replacement = new PairIds(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            Guid.NewGuid());
        var removed = new PairIds(
            Guid.Parse("f0000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            Guid.NewGuid());
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(World(first, removed), ref state));

        var initial = Assert.Single(
            Plan(World(first, removed, plotEpoch: 1), ref state));
        var replacedWorld = World(
            new[] { (replacement, 1), (first, initial.TargetLevel) },
            plotEpoch: 1,
            harvestEpoch: 0);
        var next = Assert.Single(Plan(replacedWorld, ref state));

        Assert.Equal(first.Action, initial.ActionId);
        Assert.Equal(replacement.Action, next.ActionId);
    }

    [Fact]
    public void DisabledOrUnavailableCaptureNeverPlans()
    {
        var ids = PairIds.Create();
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        var disabled = Configuration() with
        {
            AutoAgromancy = new AutoAgromancyConfiguration(),
        };
        Assert.Empty(Plan(World(ids, level: 2), ref state, disabled));

        var unavailable = World(ids, level: 3) with
        {
            HarvestActionCaptureState =
                WorldHarvestActionCaptureState.ContractUnavailable,
        };
        Assert.Empty(Plan(unavailable, ref state));
    }

    [Fact]
    public void ZeroCostPairSelectsItsMaximumLevel()
    {
        var ids = PairIds.Create();
        var state = AutoAgromancyCycleState.Create(new LifecycleGeneration(1));
        var baseline = World(ids, level: 1) with
        {
            HarvestActionCosts = PublicationTable<WorldHarvestActionCost>.Empty,
            Resources = PublicationTable<WorldResource>.Empty,
        };
        Assert.Empty(Plan(baseline, ref state));

        var increased = baseline with
        {
            HarvestActions = PublicationTable<WorldHarvestAction>.Create(new[]
            {
                new WorldHarvestAction(
                    ids.Action,
                    ids.Element,
                    2,
                    5,
                    true,
                    new BigDouble(100),
                    new BigDouble(100),
                    false),
            }),
        };
        var action = Assert.Single(Plan(increased, ref state));

        Assert.Equal(5, action.TargetLevel);
    }

    private static IReadOnlyList<AutoAgromancyCycleAction> Plan(
        GameWorldState world,
        ref AutoAgromancyCycleState state,
        SuiteRuntimeConfiguration? configuration = null)
    {
        var store = new ReusableActionStore<AutoAgromancyCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoAgromancyCycleAction>(store);
        AutoAgromancyCycleEvaluator.Evaluate(
            world,
            configuration ?? Configuration(),
            ref state,
            writer,
            out _);

        var actions = new List<AutoAgromancyCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        return actions;
    }

    private static SuiteRuntimeConfiguration Configuration() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoAgromancy = new AutoAgromancyConfiguration
        {
            Mode = AutoAgromancyOperationMode.Active,
        },
    };

    private static GameWorldState World(
        PairIds pair,
        int level = 1,
        long plotEpoch = 0,
        long harvestEpoch = 0) =>
        World(new[] { (pair, level) }, plotEpoch, harvestEpoch);

    private static GameWorldState World(
        PairIds first,
        PairIds second,
        long plotEpoch = 0,
        long harvestEpoch = 0) =>
        World(
            new[] { (first, 1), (second, 1) },
            plotEpoch,
            harvestEpoch);

    private static GameWorldState World() =>
        World(Array.Empty<(PairIds Pair, int Level)>(), 0, 0);

    private static GameWorldState World(
        (PairIds Pair, int Level)[] pairs,
        long plotEpoch,
        long harvestEpoch)
    {
        Array.Sort(
            pairs,
            static (left, right) => Compare(left.Pair, right.Pair));
        var actions = new WorldHarvestAction[pairs.Length];
        var costs = new WorldHarvestActionCost[pairs.Length];
        var elements = new WorldHarvestElement[pairs.Length];
        var resources = new WorldResource[pairs.Length];
        for (var index = 0; index < pairs.Length; index++)
        {
            var (ids, level) = pairs[index];
            actions[index] = new WorldHarvestAction(
                ids.Action,
                ids.Element,
                level,
                5,
                true,
                new BigDouble(100),
                new BigDouble(100),
                false);
            costs[index] = new WorldHarvestActionCost(
                ids.Action,
                ids.Element,
                WorldHarvestActionCostKind.Base,
                0,
                ids.Resource,
                new BigDouble(1));
            elements[index] = Element(ids.Element);
            resources[index] = Resource(ids.Resource);
        }

        return new GameWorldState
        {
            HarvestActions =
                PublicationTable<WorldHarvestAction>.Create(actions),
            HarvestActionCosts =
                PublicationTable<WorldHarvestActionCost>.Create(costs),
            HarvestElements = WorldTable.Create(elements),
            Resources = WorldTable.Create(resources),
            HarvestActionCaptureState = WorldHarvestActionCaptureState.Complete,
            HarvestPlotActionEpoch = plotEpoch,
            HarvestSubmissionEpoch = harvestEpoch,
            CollectedAtEpoch = 1,
        };
    }

    private static WorldHarvestElement Element(Guid id) => new(
        id,
        BigDouble.Zero,
        4,
        0,
        0,
        0,
        0,
        0,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero,
        new BigDouble(100),
        new BigDouble(100),
        BigDouble.Zero,
        BigDouble.Zero);

    private static WorldResource Resource(Guid id)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var sample = new RawResourceSample(
            id,
            new BigDouble(100),
            new BigDouble(-1),
            new BigDouble(10),
            true,
            BigDouble.Zero,
            BigDouble.Zero,
            new BigDouble(100),
            new BigDouble(100),
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.Zero,
            false,
            false,
            false,
            0,
            Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in sample,
            false,
            BigDouble.Zero,
            0,
            false,
            new BigDouble(100),
            new BigDouble(10));
    }

    private static int Compare(PairIds left, PairIds right)
    {
        var action = left.Action.CompareTo(right.Action);
        return action != 0 ? action : left.Element.CompareTo(right.Element);
    }

    private readonly record struct PairIds(
        Guid Action,
        Guid Element,
        Guid Resource)
    {
        internal static PairIds Create() =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }
}
