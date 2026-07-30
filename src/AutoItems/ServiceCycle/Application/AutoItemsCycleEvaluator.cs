using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
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
        in ServiceCycleContext context,
        ref AutoItemsCycleState state,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        PublicationTable<Guid>? temporaryAllowlist,
        AutoScribeIdentityProfile? autoScribeIdentityProfile,
        out AutoItemsDecisionMetrics metrics)
    {
        var interval = AutoItemsConfigurationPolicy.EvaluationInterval(config);
        var previousReceipt = context.PreviousReceipt;
        AutoItemsTemporaryActivationPolicy.ReconcileReceipt(
            in previousReceipt,
            ref state);
        if (!AutoItemsConfigurationPolicy.IsOperational(config))
        {
            state.EndRecoveryWait();
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.Disabled);
            return WakePolicy.AfterDecision(interval);
        }

        // A planned action wakes immediately so a verified Scroll/Relic chain can continue as soon
        // as the world source publishes the mutation. A rejected or faulted action gets one
        // zero-action configured cooldown here, preventing stale native conditions from spinning.
        if (ShouldCoolDownAfterPreviousAction(in previousReceipt))
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.RejectedActionCooldown);
            return WakePolicy.AfterDecision(interval);
        }

        if (state.HasPendingReceipt)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.AfterDecision(interval);
        }

        var activation = AutoItemsTemporaryActivationPolicy.Observe(
            world,
            ref state,
            out _);
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
            return WakePolicy.AfterDecision(
                AutoItemsConfigurationPolicy.NativeBusyInterval(config));
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
            state.QuarantinedTemporaryItems,
            temporaryAllowlist,
            autoScribeIdentityProfile);

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
                ref state,
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
                ref state,
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
                ref state,
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

    private static bool ShouldCoolDownAfterPreviousAction(in BatchReceipt receipt) =>
        receipt.IsPresent &&
        receipt.ActionCount > 0 &&
        receipt.CommittedCount == 0;

    private static AutoItemsDecisionMetrics EmptyMetrics(
        GameWorldState world,
        AutoItemsDecisionKind kind) =>
        new(world.Consumables.Count, 0, 0, 0, 0, 0, kind);

    private static WakePolicy Plan(
        in AutoItemsCycleAction action,
        in AutoItemsCandidateScan scan,
        AutoItemsDecisionKind kind,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        ref AutoItemsCycleState state,
        out AutoItemsDecisionMetrics metrics)
    {
        actions.Add(action);
        if (AutoItemsConsumableFamilies.IsTemporary(action.Family))
            state.RecordPlannedTemporary(in action);
        metrics = scan.ToMetrics(kind, plannedActions: 1);
        return WakePolicy.Immediate;
    }
}
