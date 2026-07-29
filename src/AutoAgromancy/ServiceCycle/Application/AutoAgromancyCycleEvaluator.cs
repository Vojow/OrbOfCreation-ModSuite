using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoAgromancyCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration configuration,
        ref AutoAgromancyCycleState state,
        ServiceActionWriter<AutoAgromancyCycleAction> actions,
        out AutoAgromancyDecisionMetrics metrics)
    {
        var idle = AutoAgromancyConfigurationPolicy.EvaluationInterval(in configuration);
        if (!AutoAgromancyConfigurationPolicy.IsOperational(in configuration))
        {
            metrics = Decision(
                world, state.SweepCursor, 0, AutoAgromancyDecisionKind.Disabled,
                AutoAgromancyPlanDisposition.InvalidSnapshot);
            return WakePolicy.AfterDecision(idle);
        }

        if (world.HarvestActionCaptureState != WorldHarvestActionCaptureState.Complete)
        {
            metrics = Decision(
                world, state.SweepCursor, 0,
                AutoAgromancyDecisionKind.CaptureUnavailable,
                AutoAgromancyPlanDisposition.InvalidSnapshot);
            return WakePolicy.AfterDecision(idle);
        }

        if (!state.ObservedLevels.IsInitialized)
        {
            state.ObservedLevels.Initialize(world.HarvestActions);
            state.PlotActionEpoch = world.HarvestPlotActionEpoch;
            state.HarvestSubmissionEpoch = world.HarvestSubmissionEpoch;
            metrics = Decision(
                world, 0, 0, AutoAgromancyDecisionKind.Idle,
                AutoAgromancyPlanDisposition.InvalidSnapshot);
            return WakePolicy.AfterDecision(idle);
        }

        ReconcilePending(world, ref state);

        if (world.HarvestPlotActionEpoch > state.PlotActionEpoch ||
            world.HarvestSubmissionEpoch > state.HarvestSubmissionEpoch)
        {
            state.PlotActionEpoch = Math.Max(
                state.PlotActionEpoch, world.HarvestPlotActionEpoch);
            state.HarvestSubmissionEpoch = Math.Max(
                state.HarvestSubmissionEpoch, world.HarvestSubmissionEpoch);
            if (!state.SweepPending)
            {
                state.SweepPending = true;
                state.SweepCursor = 0;
                state.SweepPairCount = world.HarvestActions.Count;
                state.SweepPairIdentityFingerprint =
                    PairIdentityFingerprint(world.HarvestActions);
            }
        }

        var direct = state.ObservedLevels.TryTakeIncrease(
            world.HarvestActions,
            out var actionId,
            out var elementId,
            out var previousLevel);

        var kind = AutoAgromancyDecisionKind.DirectIncrease;
        var fromSweep = false;
        WorldHarvestAction pair;
        if (direct)
        {
            if (!WorldHarvestActionLookup.TryFind(
                    world.HarvestActions, actionId, elementId, out pair))
            {
                metrics = Decision(
                    world, state.SweepCursor, 0,
                    AutoAgromancyDecisionKind.InvalidFacts,
                    AutoAgromancyPlanDisposition.InvalidSnapshot);
                return WakePolicy.AfterDecision(idle);
            }
        }
        else if (state.SweepPending)
        {
            var pairIdentityFingerprint =
                PairIdentityFingerprint(world.HarvestActions);
            if (state.SweepPairCount != world.HarvestActions.Count ||
                state.SweepPairIdentityFingerprint != pairIdentityFingerprint)
            {
                state.SweepCursor = 0;
                state.SweepPairCount = world.HarvestActions.Count;
                state.SweepPairIdentityFingerprint = pairIdentityFingerprint;
            }
            if (state.SweepCursor >= world.HarvestActions.Count)
            {
                state.SweepPending = false;
                state.SweepCursor = 0;
                state.SweepPairCount = 0;
                state.SweepPairIdentityFingerprint = 0;
                metrics = Decision(
                    world, 0, 0, AutoAgromancyDecisionKind.Idle,
                    AutoAgromancyPlanDisposition.InvalidSnapshot);
                return WakePolicy.AfterDecision(idle);
            }
            pair = world.HarvestActions[state.SweepCursor];
            kind = AutoAgromancyDecisionKind.TriggerSweep;
            fromSweep = true;
        }
        else
        {
            state.SweepPending = false;
            state.SweepCursor = 0;
            state.SweepPairCount = 0;
            state.SweepPairIdentityFingerprint = 0;
            metrics = Decision(
                world, 0, 0, AutoAgromancyDecisionKind.Idle,
                AutoAgromancyPlanDisposition.InvalidSnapshot);
            return WakePolicy.AfterDecision(idle);
        }

        if (!pair.Visible ||
            !TryPlan(
                world,
                in pair,
                out var plan,
                out var fingerprint))
        {
            if (fromSweep) CompleteSweepPair(world, ref state);
            metrics = Decision(
                world,
                state.SweepCursor,
                0,
                AutoAgromancyDecisionKind.InvalidFacts,
                AutoAgromancyPlanDisposition.InvalidSnapshot);
            return state.SweepPending ? WakePolicy.Immediate : WakePolicy.AfterDecision(idle);
        }

        var target = plan.TargetLevel;
        if (direct && plan.Disposition == AutoAgromancyPlanDisposition.LevelOneUnsustainable)
            target = previousLevel;

        if (!plan.HasTarget &&
            !(direct && plan.Disposition == AutoAgromancyPlanDisposition.LevelOneUnsustainable))
        {
            if (fromSweep) CompleteSweepPair(world, ref state);
            metrics = Decision(
                world,
                state.SweepCursor,
                0,
                plan.Disposition == AutoAgromancyPlanDisposition.LevelOneUnsustainable
                    ? AutoAgromancyDecisionKind.Unsustainable
                    : AutoAgromancyDecisionKind.InvalidFacts,
                plan.Disposition);
            return state.SweepPending ? WakePolicy.Immediate : WakePolicy.AfterDecision(idle);
        }

        if (target == pair.CurrentLevel)
        {
            if (direct)
                state.ObservedLevels.Accept(
                    pair.ActionId, pair.ElementId, pair.CurrentLevel);
            if (fromSweep) CompleteSweepPair(world, ref state);
            metrics = Decision(
                world,
                state.SweepCursor,
                0,
                AutoAgromancyDecisionKind.AlreadyBalanced,
                plan.Disposition);
            return state.SweepPending ? WakePolicy.Immediate : WakePolicy.AfterDecision(idle);
        }

        var action = new AutoAgromancyCycleAction(
            pair.ActionId,
            pair.ElementId,
            pair.CurrentLevel,
            target,
            pair.MaximumLevel,
            world.CollectedAtEpoch,
            fingerprint);
        actions.Add(action);
        state.RecordPending(in action, fromSweep);
        metrics = Decision(world, state.SweepCursor, 1, kind, plan.Disposition);
        return WakePolicy.Immediate;
    }

    private static void ReconcilePending(
        GameWorldState world,
        ref AutoAgromancyCycleState state)
    {
        if (state.PendingActionId == Guid.Empty) return;

        if (WorldHarvestActionLookup.TryFind(
                world.HarvestActions,
                state.PendingActionId,
                state.PendingElementId,
                out var pair) &&
            pair.CurrentLevel == state.PendingTargetLevel)
        {
            state.ObservedLevels.Accept(
                state.PendingActionId,
                state.PendingElementId,
                state.PendingTargetLevel);
            if (state.PendingWasSweep &&
                state.SweepPending &&
                state.SweepPairCount == world.HarvestActions.Count &&
                state.SweepPairIdentityFingerprint ==
                    PairIdentityFingerprint(world.HarvestActions) &&
                state.SweepCursor < world.HarvestActions.Count)
            {
                var current = world.HarvestActions[state.SweepCursor];
                if (current.ActionId == state.PendingActionId &&
                    current.ElementId == state.PendingElementId)
                    CompleteSweepPair(world, ref state);
            }
        }

        state.ClearPending();
    }

    private static void CompleteSweepPair(
        GameWorldState world,
        ref AutoAgromancyCycleState state)
    {
        state.SweepCursor++;
        if (state.SweepCursor < world.HarvestActions.Count) return;
        state.SweepPending = false;
        state.SweepCursor = 0;
        state.SweepPairCount = 0;
        state.SweepPairIdentityFingerprint = 0;
    }

    private static ulong PairIdentityFingerprint(
        PublicationTable<WorldHarvestAction> pairs)
    {
        var hash = new FingerprintBuilder();
        var rows = pairs.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            hash.Add(rows[index].ActionId);
            hash.Add(rows[index].ElementId);
        }
        return hash.Value;
    }

    internal static bool TryPlan(
        GameWorldState world,
        in WorldHarvestAction pair,
        out AutoAgromancyCompactPlan plan,
        out AutoAgromancyFactFingerprint fingerprint)
    {
        plan = default;
        fingerprint = default;
        WorldHarvestActionLookup.TryFindCosts(
            world.HarvestActionCosts,
            pair.ActionId,
            pair.ElementId,
            WorldHarvestActionCostKind.Base,
            out var baseStart,
            out var baseCount);
        if (!WorldLookup.TryFind(world.HarvestElements, pair.ElementId, out var element))
            return false;

        var resources = new List<AutoAgromancyCompactResource>(baseCount);
        var resourceIndexes = new Dictionary<Guid, int>(baseCount);
        var baseCosts = new AutoAgromancyBaseCost[baseCount];
        for (var index = 0; index < baseCount; index++)
        {
            var cost = world.HarvestActionCosts[baseStart + index];
            if (!resourceIndexes.TryGetValue(cost.ResourceId, out var resourceIndex))
            {
                if (!TryFindResource(world, cost.ResourceId, out var resource))
                    return false;
                resourceIndex = resources.Count;
                resourceIndexes.Add(cost.ResourceId, resourceIndex);
                resources.Add(new AutoAgromancyCompactResource(
                    cost.ResourceId,
                    cost.ResourceId.ToString(),
                    resource.TrueRate,
                    resource.Reading.Quality));
            }
            baseCosts[index] = new AutoAgromancyBaseCost(resourceIndex, cost.Amount);
        }

        if (WorldHarvestActionLookup.TryFindCosts(
                world.HarvestActionCosts,
                pair.ActionId,
                pair.ElementId,
                WorldHarvestActionCostKind.ObservedCurrent,
                out var currentStart,
                out var currentCount))
        {
            for (var index = 0; index < currentCount; index++)
            {
                var current = world.HarvestActionCosts[currentStart + index];
                if (!resourceIndexes.TryGetValue(current.ResourceId, out var resourceIndex))
                    return false;
                var resource = resources[resourceIndex];
                if (!GameResourceSpendMath.TryGetTrueSpend(
                        current.Amount, resource.Quality, out var currentSpend))
                    return false;
                resources[resourceIndex] = new AutoAgromancyCompactResource(
                    resource.ResourceId,
                    resource.Name,
                    resource.BaselineWithoutSelected + currentSpend,
                    resource.Quality);
            }
        }

        var costModifiers = new List<GameValueModifier>();
        var costExponents = new List<GameValueModifier>();
        var speedModifiers = new List<GameValueModifier>();
        var speedExponents = new List<GameValueModifier>();
        AppendModifiers(
            world,
            in pair,
            WorldHarvestActionScalingAxis.Cost,
            costModifiers,
            costExponents);
        AppendModifiers(
            world,
            in pair,
            WorldHarvestActionScalingAxis.Speed,
            speedModifiers,
            speedExponents);

        var scaling = new AutoAgromancyScalingSnapshot(
            pair.HasInstanceScaling,
            pair.ActionCostModifier,
            pair.ActionSpeed,
            element.ActionCostMod,
            element.ActionSpeed);
        plan = AutoAgromancyCompactLevelPlanner.Plan(
            pair.MaximumLevel,
            resources.ToArray(),
            baseCosts,
            in scaling,
            costModifiers.ToArray(),
            costExponents.ToArray(),
            speedModifiers.ToArray(),
            speedExponents.ToArray());
        return TryBuildFingerprint(world, in pair, out fingerprint);
    }

    /// <summary>
    /// Fingerprints only captured facts. The live boundary can reproduce this
    /// after an immediate collection without executing the level planner.
    /// </summary>
    internal static bool TryBuildFingerprint(
        GameWorldState world,
        in WorldHarvestAction pair,
        out AutoAgromancyFactFingerprint fingerprint)
    {
        fingerprint = default;
        if (!WorldLookup.TryFind(world.HarvestElements, pair.ElementId, out var element))
            return false;
        WorldHarvestActionLookup.TryFindCosts(
            world.HarvestActionCosts,
            pair.ActionId,
            pair.ElementId,
            WorldHarvestActionCostKind.Base,
            out var baseStart,
            out var baseCount);

        var hash = new FingerprintBuilder();
        hash.Add(pair.ActionId);
        hash.Add(pair.ElementId);
        hash.Add(pair.CurrentLevel);
        hash.Add(pair.MaximumLevel);
        hash.Add(pair.Visible);
        hash.Add(pair.HasInstanceScaling);
        hash.Add(pair.ActionCostModifier);
        hash.Add(pair.ActionSpeed);
        hash.Add(element.ActionCostMod);
        hash.Add(element.ActionSpeed);

        for (var index = 0; index < baseCount; index++)
        {
            var cost = world.HarvestActionCosts[baseStart + index];
            if (!TryFindResource(world, cost.ResourceId, out var resource))
                return false;
            hash.Add((int)cost.Kind);
            hash.Add(cost.Position);
            hash.Add(cost.ResourceId);
            hash.Add(cost.Amount);
            hash.Add(resource.TrueRate);
            hash.Add(resource.Reading.Quality);
        }

        if (WorldHarvestActionLookup.TryFindCosts(
                world.HarvestActionCosts,
                pair.ActionId,
                pair.ElementId,
                WorldHarvestActionCostKind.ObservedCurrent,
                out var currentStart,
                out var currentCount))
        {
            for (var index = 0; index < currentCount; index++)
            {
                var cost = world.HarvestActionCosts[currentStart + index];
                hash.Add((int)cost.Kind);
                hash.Add(cost.Position);
                hash.Add(cost.ResourceId);
                hash.Add(cost.Amount);
            }
        }

        var modifierRows = world.HarvestActionModifiers.AsSpan();
        for (var index = 0; index < modifierRows.Length; index++)
        {
            ref readonly var modifier = ref modifierRows[index];
            if (modifier.ActionId != pair.ActionId ||
                modifier.ElementId != pair.ElementId)
                continue;
            hash.Add((int)modifier.Axis);
            hash.Add((int)modifier.Role);
            hash.Add(modifier.Position);
            hash.Add((int)modifier.Type);
            hash.Add(modifier.Amount);
            hash.Add(modifier.Order);
        }

        fingerprint = new AutoAgromancyFactFingerprint(hash.Value);
        return fingerprint.IsValid;
    }

    private static void AppendModifiers(
        GameWorldState world,
        in WorldHarvestAction pair,
        WorldHarvestActionScalingAxis axis,
        ICollection<GameValueModifier> modifiers,
        ICollection<GameValueModifier> exponents)
    {
        if (!WorldHarvestActionLookup.TryFindModifiers(
                world.HarvestActionModifiers,
                pair.ActionId,
                pair.ElementId,
                axis,
                out var start,
                out var count))
            return;

        for (var index = 0; index < count; index++)
        {
            var row = world.HarvestActionModifiers[start + index];
            var value = new GameValueModifier(row.Type, row.Amount, row.Order);
            if (row.Role == WorldHarvestActionModifierRole.Exponent)
                exponents.Add(value);
            else
                modifiers.Add(value);
        }
    }

    private static bool TryFindResource(
        GameWorldState world,
        Guid resourceId,
        out WorldResource resource)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out resource))
            return true;
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvest))
        {
            resource = harvest.Resource;
            return true;
        }
        resource = default;
        return false;
    }

    private static AutoAgromancyDecisionMetrics Decision(
        GameWorldState world,
        int cursor,
        int actions,
        AutoAgromancyDecisionKind kind,
        AutoAgromancyPlanDisposition disposition) =>
        new(world.HarvestActions.Count, cursor, actions, kind, disposition);

    private sealed class FingerprintBuilder
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        internal ulong Value { get; private set; } = Offset;

        internal void Add(bool value) => Add(value ? 1 : 0);
        internal void Add(int value) => Add(unchecked((ulong)(uint)value));
        internal void Add(long value) => Add(unchecked((ulong)value));
        internal void Add(Guid value)
        {
            var bytes = value.ToByteArray();
            for (var index = 0; index < bytes.Length; index++) Mix(bytes[index]);
        }
        internal void Add(BigDouble value)
        {
            Add(BitConverter.DoubleToInt64Bits(value.Mantissa));
            Add(value.Exponent);
        }
        private void Add(ulong value)
        {
            for (var shift = 0; shift < 64; shift += 8)
                Mix((byte)(value >> shift));
        }
        private void Mix(byte value)
        {
            Value ^= value;
            Value *= Prime;
            if (Value == 0) Value = Offset;
        }
    }
}
