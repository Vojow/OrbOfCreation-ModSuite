using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoItemsCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        ref AutoItemsCycleState state,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        AutoItemsTemporaryActivationTracker temporaryActivations,
        ISet<Guid>? temporaryAllowlist,
        out AutoItemsDecisionMetrics metrics)
    {
        var interval = AutoItemsConfigurationPolicy.EvaluationInterval(config);
        if (!AutoItemsConfigurationPolicy.IsOperational(config))
        {
            state.EndRecoveryWait();
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.Disabled);
            return WakePolicy.AfterDecision(interval);
        }

        var activation = temporaryActivations.Observe(world, out _);
        if (activation is AutoItemsTemporaryActivationState.AwaitingActivation or
            AutoItemsTemporaryActivationState.Active)
        {
            metrics = EmptyMetrics(
                world,
                activation == AutoItemsTemporaryActivationState.Active
                    ? AutoItemsDecisionKind.TemporaryEffectActive
                    : AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.AfterDecision(interval);
        }
        if (activation == AutoItemsTemporaryActivationState.Quarantined)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.TemporaryItemQuarantined);
            return WakePolicy.AfterDecision(interval);
        }

        if (HasPreparedConsumable(world))
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.NativeBusy);
            return WakePolicy.AfterDecision(interval);
        }

        var hasToxicityReading =
            WorldLookup.TryFind(
                world.Resources,
                KnownEntities.PotionToxicity.Uuid,
                out var toxicity) &&
            toxicity.IsCapped &&
            toxicity.Reading.Traits.InvertedResource;
        var toxicityAtZero = hasToxicityReading && toxicity.IsAtCapacity;
        if (state.RecoveryWaitActive)
        {
            if (toxicityAtZero)
            {
                state.EndRecoveryWait();
            }
            else if (hasToxicityReading)
            {
                metrics = EmptyMetrics(world, AutoItemsDecisionKind.WaitingForToxicityRecovery);
                return WakePolicy.AfterDecision(interval);
            }
        }

        var scan = AutoItemsCandidateScanner.Scan(
            world,
            in config,
            temporaryActivations,
            temporaryAllowlist);

        // An externally restored or manually started temporary effect has the same exclusion
        // strength as one submitted by this service. Check it before every family priority.
        if (scan.TemporaryUsagePresent)
        {
            metrics = scan.ToMetrics(AutoItemsDecisionKind.TemporaryEffectActive);
            return WakePolicy.AfterDecision(interval);
        }
        if (scan.EligibleRelics > 0)
        {
            var action = scan.FirstRelic;
            return Plan(
                in action,
                in scan,
                AutoItemsDecisionKind.Relic,
                actions,
                out metrics);
        }
        if (scan.EligibleTemporaryItems > 0)
        {
            var action = scan.FirstTemporary;
            return Plan(
                in action,
                in scan,
                AutoItemsDecisionKind.TemporaryItem,
                actions,
                out metrics);
        }
        if (scan.EligibleScrolls > 0)
        {
            var action = scan.FirstScroll;
            return Plan(
                in action,
                in scan,
                AutoItemsDecisionKind.Scroll,
                actions,
                out metrics);
        }
        if (scan.SaturationCandidate && hasToxicityReading && !toxicityAtZero)
        {
            state.BeginRecoveryWait();
            metrics = scan.ToMetrics(AutoItemsDecisionKind.WaitingForToxicityRecovery);
            return WakePolicy.AfterDecision(interval);
        }

        metrics = scan.ToMetrics(AutoItemsDecisionKind.Idle);
        return WakePolicy.AfterDecision(interval);
    }

    private static bool HasPreparedConsumable(GameWorldState world)
    {
        var rows = world.Consumables.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].QueuedQuantity > 0 ||
                rows[index].CurrentPrepTime.CompareTo(BigDouble.Zero) > 0)
            {
                return true;
            }
        }
        return false;
    }

    private static AutoItemsDecisionMetrics EmptyMetrics(
        GameWorldState world,
        AutoItemsDecisionKind kind) =>
        new(world.Consumables.Count, 0, 0, 0, 0, 0, kind);

    private static WakePolicy Plan(
        in AutoItemsCycleAction action,
        in AutoItemsCandidateScan scan,
        AutoItemsDecisionKind kind,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        out AutoItemsDecisionMetrics metrics)
    {
        actions.Add(action);
        metrics = scan.ToMetrics(kind, plannedActions: 1);
        return WakePolicy.Immediate;
    }
}
