using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestSemanticParityTests
{
    [Theory]
    [InlineData(DecisionScenario.BothReady, true, 0,
        FeatureStatusState.Operational, FeatureStatusReasonCode.None)]
    [InlineData(DecisionScenario.FruitLocked, true, 1,
        FeatureStatusState.Operational, FeatureStatusReasonCode.None)]
    [InlineData(DecisionScenario.FruitPairUnavailable, true, 1,
        FeatureStatusState.Degraded, FeatureStatusReasonCode.PartialCapabilityUnavailable)]
    [InlineData(DecisionScenario.FeatureUnavailable, false, -1,
        FeatureStatusState.NotReady, FeatureStatusReasonCode.RegistryNotReady)]
    [InlineData(DecisionScenario.BothNativeBusy, false, -1,
        FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.NativeBusy)]
    [InlineData(DecisionScenario.OnlyTreasureSelected, true, 1,
        FeatureStatusState.Operational, FeatureStatusReasonCode.None)]
    public void FrozenLegacyDecisionMatrixMatchesServiceCycle(
        DecisionScenario scenario,
        bool expectedAction,
        int expectedPair,
        FeatureStatusState expectedHealth,
        FeatureStatusReasonCode expectedReason)
    {
        var input = CreateInput(scenario);
        var result = Evaluate(input, InitialState(), Context(1));
        var state = new AutoHarvestStateRecord(result.State).ToState();
        var health = AutoHarvestFeatureStatusProjector.Project(
            state.FruitHealth,
            state.TreasureHealth);
        var pair = result.HasAction
            ? (int)new AutoHarvestActionRecord(result.Action).Pair
            : -1;

        Assert.Equal(expectedAction, result.HasAction);
        Assert.Equal(expectedPair, pair);
        Assert.Equal(expectedHealth, health.State);
        Assert.Equal(expectedReason, health.Reason);
    }

    [Theory]
    [InlineData(true, (int)AutoHarvestPair.TreasureTree)]
    [InlineData(false, (int)AutoHarvestPair.FruitTree)]
    public void FrozenLegacyFairnessRotatesOnlyAfterCommittedMutation(
        bool committed,
        int expectedNext)
    {
        var input = CreateInput(DecisionScenario.BothReady);
        var firstContext = Context(1);
        var first = Evaluate(input, InitialState(), firstContext);
        var receipt = committed
            ? BatchReceipt.Completed(
                firstContext.Identity,
                new BatchId(1),
                1,
                new ServiceNativeCallTotals(1, 1, 1),
                new MonotonicTimestamp(20))
            : BatchReceipt.Terminated(
                firstContext.Identity,
                new BatchId(1),
                1,
                0,
                0,
                ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected),
                new ServiceNativeCallTotals(0, 0, 0),
                new MonotonicTimestamp(20));

        var second = Evaluate(
            input,
            new AutoHarvestStateRecord(first.State).ToState(),
            Context(2, receipt));

        Assert.True(second.HasAction);
        Assert.Equal(expectedNext, (int)new AutoHarvestActionRecord(second.Action).Pair);
    }

    private static AutoHarvestCycleEvaluation Evaluate(
        in AutoHarvestCycleInputRecord input,
        in AutoHarvestCycleState state,
        in ServiceCycleContext context) =>
        new AutoHarvestCycleEvaluator().Evaluate(
            input.ToFrame(),
            input.ToConfiguration(),
            state,
            context);

    private static AutoHarvestCycleInputRecord CreateInput(DecisionScenario scenario)
    {
        var fruitSelected = scenario != DecisionScenario.OnlyTreasureSelected;
        var fruit = scenario switch
        {
            DecisionScenario.FruitLocked => Captured(
                AutoHarvestPair.FruitTree,
                Facts(actionAvailability: AutoHarvestEvidenceState.Rejected)),
            DecisionScenario.FruitPairUnavailable => Unavailable(
                AutoHarvestPair.FruitTree,
                AutoHarvestCaptureFailureScope.Pair),
            DecisionScenario.FeatureUnavailable => Unavailable(
                AutoHarvestPair.FruitTree,
                AutoHarvestCaptureFailureScope.Feature),
            DecisionScenario.BothNativeBusy => Captured(
                AutoHarvestPair.FruitTree,
                Facts(readiness: AutoHarvestEvidenceState.Rejected)),
            DecisionScenario.OnlyTreasureSelected =>
                AutoHarvestPairCapture.NotSelected(AutoHarvestPair.FruitTree),
            _ => Captured(AutoHarvestPair.FruitTree, Facts()),
        };
        var treasureFacts = scenario == DecisionScenario.BothNativeBusy
            ? Facts(readiness: AutoHarvestEvidenceState.Rejected)
            : Facts();
        var frame = new AutoHarvestCycleFrame(
            fruit,
            Captured(AutoHarvestPair.TreasureTree, treasureFacts),
            ownsActionFamily: true);
        var config = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected,
            treasureSelected: true,
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        return new AutoHarvestCycleInputRecord(frame, config);
    }

    private static AutoHarvestPairCapture Captured(
        AutoHarvestPair pair,
        in AutoHarvestPairFacts facts) =>
        AutoHarvestPairCapture.Captured(pair, facts);

    private static AutoHarvestPairCapture Unavailable(
        AutoHarvestPair pair,
        AutoHarvestCaptureFailureScope scope) =>
        AutoHarvestPairCapture.Unavailable(
            pair,
            AutoHarvestCaptureUnavailableReason.RegistryNotReady,
            scope);

    private static AutoHarvestPairFacts Facts(
        AutoHarvestEvidenceState actionAvailability = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState readiness = AutoHarvestEvidenceState.Verified) => new(
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        actionAvailability,
        AutoHarvestEvidenceState.Verified,
        readiness,
        AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified);

    private static AutoHarvestCycleState InitialState() =>
        new AutoHarvestStateRecord(
            AutoHarvestCycleState.Create(new LifecycleGeneration(1)))
        .ToState();

    private static ServiceCycleContext Context(ulong cycle, BatchReceipt receipt = default)
    {
        var identity = new ServiceCycleIdentity(
            AutoHarvestServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(cycle),
            new CycleId(cycle));
        return new ServiceCycleContext(
            identity,
            receipt,
            new MonotonicTimestamp(10 + (long)cycle));
    }

    public enum DecisionScenario
    {
        BothReady,
        FruitLocked,
        FruitPairUnavailable,
        FeatureUnavailable,
        BothNativeBusy,
        OnlyTreasureSelected,
    }
}
