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
    private static readonly Guid Gamma = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Core = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherCore = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Resource = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

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
        var second = Assert.Single(PlanAt(
            world,
            Config(),
            ref state,
            decisionAtTicks: 0,
            out _,
            out _,
            previousRejected: true));

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

    [Fact]
    public void TimedCycleRotatesToAnUnlockedConceptAfterTraining()
    {
        var world = World(
            new[]
            {
                Recipe(Alpha, maximum: 1),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(slotMode: AutoConceptSlotManagementMode.TimedCycle);

        Assert.Empty(PlanAt(
            world, config, ref state, decisionAtTicks: 0, out _, out var training));
        var action = Assert.Single(PlanAt(
            world, config, ref state, TimeSpan.FromSeconds(61).Ticks, out _, out _));

        Assert.Equal(AutoConceptIdleReason.WaitingForTraining, training.IdleReason);
        Assert.Equal(AutoConceptActionKind.RotateOut, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void TimedCycleDepthSettlementDoesNotRestartTheActiveSession()
    {
        var initial = World(
            new[]
            {
                Recipe(Alpha, maximum: 4),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var queued = World(
            new[]
            {
                Recipe(Alpha, maximum: 4),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 4) });
        var settled = World(
            new[]
            {
                Recipe(Alpha, maximum: 4),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 4, queued: 4) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(
            slotMode: AutoConceptSlotManagementMode.TimedCycle,
            trainingSeconds: 30);

        var depth = Assert.Single(PlanAt(
            initial, config, ref state, decisionAtTicks: 0, out _, out _));
        Assert.Equal(AutoConceptDecisionKind.Depth, state.Decision.Kind);
        Assert.Equal(4, depth.TargetOrDelta);
        Assert.Empty(PlanAt(
            World(), config, ref state, TimeSpan.FromSeconds(1).Ticks,
            out _, out _, previousCommitted: true));
        Assert.True(state.HasPendingReceipt);
        Assert.Empty(PlanAt(
            queued, config, ref state, TimeSpan.FromSeconds(2).Ticks,
            out _, out _));
        Assert.False(state.HasPendingReceipt);
        Assert.Empty(PlanAt(
            settled, config, ref state, TimeSpan.FromSeconds(10).Ticks,
            out _, out _));

        var rotation = Assert.Single(PlanAt(
            settled, config, ref state, TimeSpan.FromSeconds(31).Ticks,
            out _, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, rotation.Kind);
        Assert.Equal(Alpha, rotation.RecipeId);
        Assert.Equal(Beta, rotation.ReplacementId);
    }

    [Fact]
    public void NegativeLiveRateRollsBackOwnedDepthBeforeTheResourceReachesZero()
    {
        var currentDrain = new[]
        {
            new WorldAlchemyCost(
                Alpha,
                WorldAlchemyCostKind.CurrentDrain,
                Resource,
                new BigDouble(1)),
        };
        var world = World(
            new[] { Recipe(Alpha, maximum: 2) },
            new[] { Instance(Alpha, quantity: 2, queued: 2) },
            resources: new[] { DrainingResource(Resource, quantity: 50, capacity: 100, trueRate: -1) },
            costs: currentDrain);
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        state.BaselineCaptured = true;
        state.Ownership.ObserveBaseline(Alpha.ToString(), 1);
        state.Ownership.RecordAutomatedDelta(Alpha.ToString(), 2, 1);

        var rollback = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.RemoveOwned, rollback.Kind);
        Assert.Equal(Alpha, rollback.RecipeId);
        Assert.Equal(1, rollback.TargetOrDelta);
    }

    [Fact]
    public void TimedCycleRotatesAcrossConceptTypesWhenAFreeSlotMustBeReleased()
    {
        var world = World(
            new[]
            {
                Recipe(Alpha, maximum: 1),
                Recipe(Beta, maximum: 1, coreTypeId: OtherCore),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) },
            cannotAddNow: Beta);
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(slotMode: AutoConceptSlotManagementMode.TimedCycle);

        Assert.Empty(PlanAt(
            world, config, ref state, decisionAtTicks: 0, out _, out _));
        var action = Assert.Single(PlanAt(
            world, config, ref state, TimeSpan.FromSeconds(61).Ticks, out _, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void TimedCycleHighlightsWhenOnlyAlternativeIsLocked()
    {
        var world = World(
            new[]
            {
                Recipe(Alpha, maximum: 1),
                Recipe(Beta, maximum: 1, discovered: false),
                Recipe(Gamma, maximum: 0, coreTypeId: OtherCore),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(slotMode: AutoConceptSlotManagementMode.TimedCycle);

        Assert.Empty(PlanAt(
            world, config, ref state, decisionAtTicks: 0, out _, out _));
        var actions = PlanAt(
            world, config, ref state, TimeSpan.FromSeconds(61).Ticks,
            out _, out var idle);

        Assert.Empty(actions);
        Assert.Equal(3, idle.CapturedRecipes);
        Assert.Equal(2, idle.EligibleRecipes);
        Assert.Equal(AutoConceptDecisionKind.Idle, idle.Kind);
        Assert.Equal(
            AutoConceptIdleReason.NoUnlockedAssignableReplacement,
            idle.IdleReason);
    }

    private static WorldAlchemyRecipe Recipe(
        Guid id,
        int masteryLevel = 0,
        double masteryXp = 0,
        double requiredXp = 100,
        int maximum = 4,
        bool discovered = true,
        Guid? coreTypeId = null) =>
        new(
            id, coreTypeId ?? Core, discovered, 0, 0, 0,
            new BigDouble(masteryXp), masteryLevel, default,
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
        long collectedAtEpoch = 1,
        Guid? cannotAddNow = null,
        WorldResource[]? resources = null,
        WorldAlchemyCost[]? costs = null)
    {
        var concepts = new WorldConceptRecipeBuffer();
        foreach (var recipe in recipes)
        {
            var canAddNow = recipe.RecipeId != cannotAddNow;
            foreach (var instance in instances)
            {
                if (instance.RecipeId == recipe.RecipeId ||
                    Math.Max(instance.Quantity, instance.QueuedQuantity) <= 0) continue;
                foreach (var activeRecipe in recipes)
                    if (activeRecipe.RecipeId == instance.RecipeId &&
                        activeRecipe.CoreTypeId == recipe.CoreTypeId)
                        canAddNow = false;
            }
            var row = new WorldConceptRecipe(
                recipe.RecipeId,
                recipe.CoreTypeId,
                canAddNow);
            concepts.Append(in row);
        }
        var active = new WorldAlchemyInstanceBuffer();
        foreach (var instance in instances) active.Append(in instance);

        return new GameWorldState
        {
            AlchemyRecipes = WorldTable.Create(recipes),
            ConceptRecipes = WorldAlchemyRowDeriver.Build(concepts),
            AlchemyInstances = WorldAlchemyRowDeriver.Build(active),
            Resources = WorldTable.Create(resources ?? Array.Empty<WorldResource>()),
            AlchemyCosts = PublicationTable<WorldAlchemyCost>.Create(
                costs ?? Array.Empty<WorldAlchemyCost>()),
            CollectedAtEpoch = collectedAtEpoch,
        };
    }

    private static WorldResource DrainingResource(
        Guid id,
        double quantity,
        double capacity,
        double trueRate)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            id,
            new BigDouble(quantity),
            new BigDouble(capacity),
            new BigDouble(trueRate),
            visible: true,
            lifetimeQuantity: default,
            discoveryTime: default,
            quality: new BigDouble(100),
            gainRate: new BigDouble(100),
            drain: new BigDouble(-trueRate),
            reservation: default,
            usage: default,
            inLossMode: false,
            inRestMode: false,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            isCapped: true,
            headroom: new BigDouble(capacity - quantity),
            fillFraction: quantity / capacity,
            isAtCapacity: false,
            trueQuantity: new BigDouble(quantity),
            trueRate: new BigDouble(trueRate));
    }

    private static SuiteRuntimeConfiguration Config(
        bool enabled = true,
        bool emergencyDisabled = false,
        AutoConceptOperationMode mode = AutoConceptOperationMode.Active,
        AutoConceptSlotManagementMode slotMode = AutoConceptSlotManagementMode.RotateAll,
        int fallbackSeconds = 30,
        int quantityCap = 0,
        int trainingSeconds = 60) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = enabled },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = mode,
                SlotManagement = slotMode,
                FallbackEvaluationIntervalSeconds = fallbackSeconds,
                TrainingPeriodSeconds = trainingSeconds,
                QuantityCap = quantityCap,
                MinimumDrainRatio = 0.25f,
            },
        };

    private static IReadOnlyList<AutoConceptCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state,
        out WakePolicy wake) =>
        PlanAt(world, config, ref state, 0, out wake, out _);

    private static IReadOnlyList<AutoConceptCycleAction> PlanAt(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state,
        long decisionAtTicks,
        out WakePolicy wake,
        out AutoConceptDecisionMetrics metrics,
        bool previousCommitted = false,
        bool previousRejected = false)
    {
        var store = new ReusableActionStore<AutoConceptCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoConceptCycleAction>(store);
        var identity = new ServiceCycleIdentity(
            new ServiceId("auto-concept"), new LifecycleGeneration(1),
            new ConfigGeneration(1), StrategyGeneration.Initial,
            new WorldGeneration(1), new CycleId(1));
        var receipt = previousCommitted || previousRejected
            ? BatchReceipt.Completed(
                identity,
                new BatchId(1),
                1,
                previousCommitted ? 1 : 0,
                new ServiceNativeCallTotals(1, 1, previousCommitted ? 1 : 0),
                new MonotonicTimestamp(decisionAtTicks))
            : default;
        var context = new ServiceCycleContext(
            identity, receipt, new MonotonicTimestamp(decisionAtTicks));
        wake = AutoConceptCycleEvaluator.Evaluate(
            world, in config, in context, ref state, writer, out metrics);
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
