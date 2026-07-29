using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoHarvestCycleEvaluator
{
    public AutoHarvestCycleEvaluation Evaluate(
        in AutoHarvestCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in AutoHarvestCycleState previousState,
        in ServiceCycleContext context)
    {
        if (previousState.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Harvest state belongs to a different lifecycle.");

        var faults = RememberPreviousFault(previousState, context.PreviousReceipt);
        var fruitHealth = ProjectHealth(frame.Fruit, config.AutoHarvest.CollectFruitTrees, faults);
        var treasureHealth = ProjectHealth(frame.Treasure, config.AutoHarvest.CollectTreasureTrees, faults);
        var nextPair = ApplyPreviousReceipt(previousState, context.PreviousReceipt);
        var actionPair = default(AutoHarvestPair);
        var hasAction = AutoHarvestConfigurationPolicy.IsOperational(config) &&
            !HasFeatureFailure(frame, config) &&
            TrySelect(nextPair, fruitHealth, treasureHealth, out actionPair);
        var action = default(AutoHarvestCycleAction);
        if (hasAction)
        {
            var capture = CaptureOf(in frame, actionPair);
            action = new AutoHarvestCycleAction(actionPair, capture.Facts, capture.Safety);
        }

        var state = previousState.CompleteEvaluation(
            nextPair,
            fruitHealth,
            treasureHealth,
            hasAction,
            actionPair,
            faults);
        return new AutoHarvestCycleEvaluation(
            state,
            action,
            hasAction,
            WakePolicy.AfterBatch(config.AutoHarvest.EvaluationInterval));
    }

    /// <summary>
    /// What one pair's health is, given what the world says about it and what this service already
    /// knows about itself.
    /// </summary>
    /// <remarks>
    /// A remembered fault outranks anything the snapshot could say, which is the order the two
    /// main-thread flags produced when capture consulted them before reading the world. A pair that
    /// faulted stays faulted for the lifecycle whatever the world does next, because nothing about the
    /// world is evidence that the failure was fixed.
    /// </remarks>
    private static AutoHarvestPairHealth ProjectHealth(
        in AutoHarvestPairCapture capture,
        bool selected,
        in AutoHarvestFaultMemory faults)
    {
        if (!selected) return AutoHarvestPairHealth.NotSelected(capture.Pair);
        var fault = faults.For(capture.Pair);
        if (fault != AutoHarvestFaultKind.None)
        {
            return new AutoHarvestPairHealth(
                capture.Pair,
                true,
                fault == AutoHarvestFaultKind.ContractUnavailable
                    ? AutoHarvestPairHealthKind.ContractUnavailable
                    : AutoHarvestPairHealthKind.Faulted,
                faults.HasFeatureFault);
        }

        if (capture.Kind == AutoHarvestPairCaptureKind.Unavailable)
        {
            return new AutoHarvestPairHealth(
                capture.Pair,
                true,
                AutoHarvestPairHealthKind.RegistryNotReady,
                featureScoped: true);
        }

        var decision = AutoHarvestPolicy.EvaluatePair(capture.Pair, selected, capture.Facts);
        return AutoHarvestPairHealthMapper.FromDecision(capture.Pair, decision);
    }

    /// <summary>
    /// Folds the last batch's terminal code into what this service knows about its own failures.
    /// </summary>
    /// <remarks>
    /// The receipt names the pair only through the plan that produced it, so this reads
    /// <see cref="AutoHarvestCycleState.PlannedPair"/> for the pair-scoped codes. A batch that ended
    /// any other way, or a code that names no failure of this kind, leaves the memory alone —
    /// including a rejection, which says the action was declined rather than that it went wrong.
    /// </remarks>
    private static AutoHarvestFaultMemory RememberPreviousFault(
        in AutoHarvestCycleState state,
        in BatchReceipt receipt)
    {
        if (!state.HasPlannedAction ||
            !receipt.IsPresent ||
            receipt.Disposition != BatchTerminalDisposition.Faulted)
        {
            return state.Faults;
        }

        if (receipt.ResultCode == AutoHarvestActionResultCodes.FeatureContractUnavailable)
            return state.Faults.WithFeature(AutoHarvestFaultKind.ContractUnavailable);
        if (receipt.ResultCode == AutoHarvestActionResultCodes.PairContractUnavailable)
            return state.Faults.With(state.PlannedPair, AutoHarvestFaultKind.ContractUnavailable);
        if (receipt.ResultCode == AutoHarvestActionResultCodes.PairFaulted)
            return state.Faults.With(state.PlannedPair, AutoHarvestFaultKind.Faulted);
        return state.Faults;
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

    private static AutoHarvestPairCapture CaptureOf(
        in AutoHarvestCycleFrame frame,
        AutoHarvestPair pair) =>
        pair == AutoHarvestPair.FruitTree ? frame.Fruit : frame.Treasure;

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

    /// <summary>
    /// Whether a selected pair reported something that stops the feature rather than itself.
    /// </summary>
    /// <remarks>
    /// Only this reaches the sibling. A remembered feature fault needs no equivalent here because
    /// <see cref="AutoHarvestFaultMemory.For"/> already answers for both pairs, so neither is
    /// eligible and nothing is left to select.
    /// </remarks>
    private static bool HasFeatureFailure(
        in AutoHarvestCycleFrame frame,
        in SuiteRuntimeConfiguration config) =>
        IsSelectedFeatureFailure(frame.Fruit, config.AutoHarvest.CollectFruitTrees) ||
        IsSelectedFeatureFailure(frame.Treasure, config.AutoHarvest.CollectTreasureTrees);

    private static bool IsSelectedFeatureFailure(
        in AutoHarvestPairCapture capture,
        bool selected) =>
        selected && capture.Kind == AutoHarvestPairCaptureKind.Unavailable;

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
