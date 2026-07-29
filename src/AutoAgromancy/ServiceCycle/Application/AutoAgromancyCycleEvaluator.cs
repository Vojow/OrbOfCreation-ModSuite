using System;
using OrbModding.Common.Runtime.Configuration;
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
            !AutoAgromancyPlanningProjection.TryPlan(
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
        var hash = new AutoAgromancyFingerprintBuilder();
        var rows = pairs.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            hash.Add(rows[index].ActionId);
            hash.Add(rows[index].ElementId);
        }
        return hash.Value;
    }

    private static AutoAgromancyDecisionMetrics Decision(
        GameWorldState world,
        int cursor,
        int actions,
        AutoAgromancyDecisionKind kind,
        AutoAgromancyPlanDisposition disposition) =>
        new(world.HarvestActions.Count, cursor, actions, kind, disposition);
}
