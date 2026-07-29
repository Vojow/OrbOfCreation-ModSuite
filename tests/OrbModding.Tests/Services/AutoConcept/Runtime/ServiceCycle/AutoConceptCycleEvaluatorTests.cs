using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

public sealed class AutoConceptCycleEvaluatorTests
{
    private static readonly Guid Alpha = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beta = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Core = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void ReschedulesAtTheConfiguredFallbackWhenThereIsNoWork()
    {
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var actions = Plan(World(), Config(fallbackSeconds: 17), ref state, out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicyKind.AfterDecision, wake.Kind);
        Assert.Equal(TimeSpan.FromSeconds(17), wake.Delay.ToTimeSpan());
    }

    [Fact]
    public void BreadthStartsTheLowestMasteryUnassignedConcept()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 2), Recipe(Beta, masteryLevel: 1) },
            Array.Empty<WorldAlchemyInstance>());
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.Add, action.Kind);
        Assert.Equal(Beta, action.RecipeId);
        Assert.Equal(1, action.TargetOrDelta);
    }

    [Fact]
    public void BreadthWaitsForASettledAssignment()
    {
        var world = World(
            new[] { Recipe(Alpha) },
            new[] { Instance(Alpha, quantity: 0, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        Assert.Empty(Plan(world, Config(), ref state, out _));
    }

    [Fact]
    public void DepthRaisesAnActiveConceptToTheConfiguredCap()
    {
        var world = World(
            new[] { Recipe(Alpha, maximum: 8) },
            new[] { Instance(Alpha, quantity: 2, queued: 2) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, Config(quantityCap: 5), ref state, out _));

        Assert.Equal(AutoConceptActionKind.Add, action.Kind);
        Assert.Equal(5, action.TargetOrDelta);
    }

    [Fact]
    public void RotateAllRemovesTheHigherMasteryOccupantBeforeReplacingIt()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 4), Recipe(Beta, masteryLevel: 1) },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void PreserveManualOnlyRemovesAnEntirelyOwnedAssignment()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 4), Recipe(Beta, masteryLevel: 1) },
            new[] { Instance(Alpha, quantity: 3, queued: 3) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        state.BaselineCaptured = true;
        state.Ownership.ObserveBaseline(Alpha.ToString(), 0);
        state.Ownership.RecordAutomatedDelta(Alpha.ToString(), 3, 3);

        var action = Assert.Single(Plan(
            world, Config(slotMode: AutoConceptSlotManagementMode.PreserveManual),
            ref state, out _));

        Assert.Equal(AutoConceptActionKind.RemoveOwned, action.Kind);
        Assert.Equal(3, action.TargetOrDelta);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void UnsafeOwnedDrainRollsBackBeforeBreadthOrRebalance()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 4), Recipe(Beta, masteryLevel: 1) },
            new[] { Instance(Alpha, quantity: 2, queued: 2, drainReadable: false) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        state.BaselineCaptured = true;
        state.Ownership.ObserveBaseline(Alpha.ToString(), 1);
        state.Ownership.RecordAutomatedDelta(Alpha.ToString(), 2, 1);

        var action = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.RemoveOwned, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(1, action.TargetOrDelta);
    }

    [Fact]
    public void CandidateCursorLetsBoundaryRefusalsAdvancePastTheFirstRankedRecipe()
    {
        var world = World(
            new[] { Recipe(Alpha), Recipe(Beta) },
            Array.Empty<WorldAlchemyInstance>());
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var first = Assert.Single(Plan(world, Config(), ref state, out _));
        var second = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(Alpha, first.RecipeId);
        Assert.Equal(Beta, second.RecipeId);
    }

    [Fact]
    public void OperationalGuardsFailClosed()
    {
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var world = World(new[] { Recipe(Alpha) }, Array.Empty<WorldAlchemyInstance>());

        Assert.Empty(Plan(world, Config(enabled: false), ref state, out _));
        Assert.Empty(Plan(world, Config(emergencyDisabled: true), ref state, out _));
        Assert.Empty(Plan(world, Config(mode: AutoConceptOperationMode.Disabled), ref state, out _));
    }

    private static WorldAlchemyRecipe Recipe(
        Guid id,
        int masteryLevel = 0,
        double masteryXp = 0,
        double requiredXp = 100,
        int maximum = 4) =>
        new(
            id, Core, true, 0, 0, 0, new BigDouble(masteryXp), masteryLevel, default,
            false, false, false, 0, false,
            default, default, default, default, default, default, default, default,
            default, default, default, default, default, new BigDouble(maximum), default,
            new BigDouble(requiredXp));

    private static WorldAlchemyInstance Instance(
        Guid id,
        int quantity,
        int queued,
        bool drainReadable = true,
        double drainRatio = 1) =>
        new(id, quantity, queued, drainReadable, new BigDouble(drainRatio));

    private static GameWorldState World() =>
        World(Array.Empty<WorldAlchemyRecipe>(), Array.Empty<WorldAlchemyInstance>());

    private static GameWorldState World(
        WorldAlchemyRecipe[] recipes,
        WorldAlchemyInstance[] instances,
        long collectedAtEpoch = 1)
    {
        var concepts = new WorldConceptRecipeBuffer();
        foreach (var recipe in recipes)
        {
            var row = new WorldConceptRecipe(recipe.RecipeId, Core);
            concepts.Append(in row);
        }
        var active = new WorldAlchemyInstanceBuffer();
        foreach (var instance in instances) active.Append(in instance);

        return new GameWorldState
        {
            AlchemyRecipes = WorldTable.Create(recipes),
            ConceptRecipes = WorldAlchemyRowDeriver.Build(concepts),
            AlchemyInstances = WorldAlchemyRowDeriver.Build(active),
            CollectedAtEpoch = collectedAtEpoch,
        };
    }

    private static SuiteRuntimeConfiguration Config(
        bool enabled = true,
        bool emergencyDisabled = false,
        AutoConceptOperationMode mode = AutoConceptOperationMode.Active,
        AutoConceptSlotManagementMode slotMode = AutoConceptSlotManagementMode.RotateAll,
        int fallbackSeconds = 30,
        int quantityCap = 0) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = enabled },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = mode,
                SlotManagement = slotMode,
                FallbackEvaluationIntervalSeconds = fallbackSeconds,
                TrainingPeriodSeconds = 60,
                QuantityCap = quantityCap,
                MinimumDrainRatio = 0.25f,
            },
        };

    private static IReadOnlyList<AutoConceptCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state,
        out WakePolicy wake)
    {
        var store = new ReusableActionStore<AutoConceptCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoConceptCycleAction>(store);
        var identity = new ServiceCycleIdentity(
            new ServiceId("auto-concept"), new LifecycleGeneration(1),
            new ConfigGeneration(1), StrategyGeneration.Initial,
            new WorldGeneration(1), new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(0));
        wake = AutoConceptCycleEvaluator.Evaluate(
            world, in config, in context, ref state, writer, out var metrics);
        state.RecordDecision(in metrics);

        var actions = new List<AutoConceptCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        return actions;
    }
}
