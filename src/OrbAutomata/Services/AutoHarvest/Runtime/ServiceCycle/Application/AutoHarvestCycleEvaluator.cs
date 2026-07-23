using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoHarvestCycleEvaluator
{
    public AutoHarvestCycleEvaluation Evaluate(
        in AutoHarvestCycleFrame frame,
        in AutomataConfiguration config,
        in AutoHarvestCycleState previousState,
        in ServiceCycleContext context)
    {
        if (previousState.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Harvest state belongs to a different lifecycle.");

        var fruitHealth = ProjectHealth(frame.Fruit, config.AutoHarvest.CollectFruitTrees);
        var treasureHealth = ProjectHealth(frame.Treasure, config.AutoHarvest.CollectTreasureTrees);
        var nextPair = ApplyPreviousReceipt(previousState, context.PreviousReceipt);
        var actionPair = default(AutoHarvestPair);
        var hasAction = AutoHarvestConfigurationPolicy.IsOperational(config) &&
            frame.OwnsActionFamily &&
            !HasFeatureFailure(frame, config) &&
            TrySelect(nextPair, fruitHealth, treasureHealth, out actionPair);
        var action = hasAction ? new AutoHarvestCycleAction(actionPair) : default;
        var state = previousState.CompleteEvaluation(
            nextPair,
            fruitHealth,
            treasureHealth,
            hasAction,
            actionPair);
        return new AutoHarvestCycleEvaluation(
            state,
            action,
            hasAction,
            WakePolicy.AfterBatch(config.AutoHarvest.EvaluationInterval));
    }

    private static AutoHarvestPairHealth ProjectHealth(
        in AutoHarvestPairCapture capture,
        bool selected)
    {
        if (!selected) return AutoHarvestPairHealth.NotSelected(capture.Pair);
        if (capture.Kind == AutoHarvestPairCaptureKind.Unavailable)
        {
            var unavailableKind = capture.UnavailableReason switch
            {
                AutoHarvestCaptureUnavailableReason.RegistryNotReady =>
                    AutoHarvestPairHealthKind.RegistryNotReady,
                AutoHarvestCaptureUnavailableReason.ContractUnavailable =>
                    AutoHarvestPairHealthKind.ContractUnavailable,
                AutoHarvestCaptureUnavailableReason.Faulted =>
                    AutoHarvestPairHealthKind.Faulted,
                _ => throw new InvalidOperationException("Auto Harvest capture has no unavailable reason."),
            };
            return new AutoHarvestPairHealth(
                capture.Pair,
                true,
                unavailableKind,
                capture.FailureScope == AutoHarvestCaptureFailureScope.Feature);
        }

        var decision = AutoHarvestPolicy.EvaluatePair(capture.Pair, selected, capture.Facts);
        return AutoHarvestPairHealthMapper.FromDecision(capture.Pair, decision);
    }

    private static AutoHarvestPair ApplyPreviousReceipt(
        in AutoHarvestCycleState state,
        in BatchReceipt receipt)
    {
        if (!state.HasPlannedAction ||
            !receipt.IsPresent ||
            receipt.Disposition != BatchTerminalDisposition.Completed ||
            receipt.ActionCount != 1 ||
            receipt.CommittedCount != 1)
        {
            return state.NextPair;
        }

        return Other(state.PlannedPair);
    }

    private static bool TrySelect(
        AutoHarvestPair first,
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        out AutoHarvestPair pair)
    {
        if (IsEligible(first, fruit, treasure))
        {
            pair = first;
            return true;
        }

        var second = Other(first);
        if (IsEligible(second, fruit, treasure))
        {
            pair = second;
            return true;
        }

        pair = default;
        return false;
    }

    private static bool HasFeatureFailure(
        in AutoHarvestCycleFrame frame,
        in AutomataConfiguration config) =>
        IsSelectedFeatureFailure(frame.Fruit, config.AutoHarvest.CollectFruitTrees) ||
        IsSelectedFeatureFailure(frame.Treasure, config.AutoHarvest.CollectTreasureTrees);

    private static bool IsSelectedFeatureFailure(
        in AutoHarvestPairCapture capture,
        bool selected) =>
        selected &&
        capture.Kind == AutoHarvestPairCaptureKind.Unavailable &&
        capture.FailureScope == AutoHarvestCaptureFailureScope.Feature;

    private static bool IsEligible(
        AutoHarvestPair pair,
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure) =>
        (pair == AutoHarvestPair.FruitTree ? fruit : treasure).Kind ==
        AutoHarvestPairHealthKind.Eligible;

    private static AutoHarvestPair Other(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => AutoHarvestPair.TreasureTree,
        AutoHarvestPair.TreasureTree => AutoHarvestPair.FruitTree,
        _ => throw new ArgumentOutOfRangeException(nameof(pair)),
    };
}
