using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Plans at most one use from one immutable world publication. Every path waits for another world
/// or committed-configuration publication; temporary expiry is observed rather than scheduled.
/// </summary>
internal static class AutoItemsCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration configuration,
        in ServiceCycleContext context,
        ref AutoItemsCycleState state,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        ConsumableMutationPublicationGapCoordinator publicationGap,
        out AutoItemsDecisionMetrics metrics)
    {
        var previousReceipt = context.PreviousReceipt;
        AutoItemsTemporaryActivationPolicy.ReconcileReceipt(
            in previousReceipt,
            ref state);
        if (state.HasPendingReceipt)
        {
            metrics = EmptyMetrics(
                world,
                AutoItemsConsumableFamilies.IsTemporary(
                    state.PendingReceiptAction.Family)
                    ? AutoItemsDecisionKind.AwaitingTemporaryActivation
                    : AutoItemsDecisionKind.AwaitingPermanentSettlement);
            return WakePolicy.OnPublication;
        }

        // A mutation can occur after a world capture but before that capture is published. Until a
        // strictly later clean consumables capture closes the shared gap, settlement must not consume
        // the late publication and mistake its pre-mutation topology for a drained native action.
        if (publicationGap.BlocksMutation(
                checked((long)context.Identity.Lifecycle.Value)))
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.AwaitingPermanentSettlement);
            return WakePolicy.OnPublication;
        }

        var permanent = AutoItemsPermanentSettlementPolicy.Observe(world, ref state);
        if (permanent.State == AutoItemsPermanentSettlementState.AwaitingSettlement)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.AwaitingPermanentSettlement);
            return WakePolicy.OnPublication;
        }
        if (permanent.State == AutoItemsPermanentSettlementState.Quarantined)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.PermanentSettlementQuarantined);
            return WakePolicy.OnPublication;
        }

        var activation = AutoItemsTemporaryActivationPolicy.Observe(world, ref state);
        if (activation.State is AutoItemsTemporaryActivationState.AwaitingActivation or
            AutoItemsTemporaryActivationState.Active)
        {
            metrics = EmptyMetrics(
                world,
                activation.State == AutoItemsTemporaryActivationState.Active
                    ? AutoItemsDecisionKind.TemporaryEffectActive
                    : AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.OnPublication;
        }
        if (activation.State == AutoItemsTemporaryActivationState.Quarantined)
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.TemporaryItemQuarantined);
            return WakePolicy.OnPublication;
        }

        if (!AutoItemsConfigurationPolicy.IsOperational(configuration))
        {
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.Disabled);
            return WakePolicy.OnPublication;
        }

        var consumables = world.Consumables.AsSpan();
        for (var index = 0; index < consumables.Length; index++)
        {
            if (consumables[index].CurrentPrepTime.CompareTo(BigDouble.Zero) <= 0) continue;
            metrics = EmptyMetrics(world, AutoItemsDecisionKind.NativePreparationActive);
            return WakePolicy.OnPublication;
        }

        var scan = AutoItemsCandidateScanner.Scan(
            world,
            in configuration,
            state.QuarantinedTemporaryItems,
            state.TemporaryAllowlist,
            state.RelicSettlementQuarantined,
            state.ScrollSettlementQuarantined);
        if (scan.TemporaryUsagePresent)
        {
            metrics = scan.ToMetrics(AutoItemsDecisionKind.TemporaryEffectActive);
            return WakePolicy.OnPublication;
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

        metrics = scan.ToMetrics(AutoItemsDecisionKind.Idle);
        return WakePolicy.OnPublication;
    }

    private static AutoItemsDecisionMetrics EmptyMetrics(
        GameWorldState world,
        AutoItemsDecisionKind kind) =>
        new(world.Consumables.Count, 0, 0, 0, 0, 0, 0, kind);

    private static WakePolicy Plan(
        in AutoItemsCycleAction action,
        in AutoItemsCandidateScan scan,
        AutoItemsDecisionKind kind,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        ref AutoItemsCycleState state,
        out AutoItemsDecisionMetrics metrics)
    {
        actions.Add(action);
        state.RecordPlannedAction(in action);
        metrics = scan.ToMetrics(kind, plannedActions: 1);
        return WakePolicy.OnPublication;
    }
}
