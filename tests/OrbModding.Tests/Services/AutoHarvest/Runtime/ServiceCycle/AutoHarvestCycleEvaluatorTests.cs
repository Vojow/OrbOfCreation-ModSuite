using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestCycleEvaluatorTests
{
    [Fact]
    public void FirstEligibleCyclePlansOnlyFruitAndWaitsAfterBatch()
    {
        var result = Evaluate(ReadyFrame(), Configuration(), InitialState(), Context(1));

        Assert.True(result.HasAction);
        Assert.Equal(AutoHarvestPair.FruitTree, result.Action.Pair);
        Assert.True(result.State.HasPlannedAction);
        Assert.Equal(AutoHarvestPair.FruitTree, result.State.PlannedPair);
        Assert.Equal(WakePolicyKind.AfterBatch, result.Wake.Kind);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Wake.Delay.ToTimeSpan());
    }

    [Fact]
    public void CommittedReceiptRotatesTheNextEligiblePair()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        var receipt = BatchReceipt.Completed(
            firstContext.Identity,
            new BatchId(1),
            1,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(20));

        var second = Evaluate(
            ReadyFrame(),
            Configuration(),
            first.State,
            Context(2, receipt));

        Assert.True(second.HasAction);
        Assert.Equal(AutoHarvestPair.TreasureTree, second.Action.Pair);
        Assert.Equal(AutoHarvestPair.TreasureTree, second.State.NextPair);
    }

    [Fact]
    public void RejectedReceiptDoesNotRotateFairness()
    {
        var firstContext = Context(1);
        var first = Evaluate(ReadyFrame(), Configuration(), InitialState(), firstContext);
        var terminal = ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
        var receipt = BatchReceipt.Terminated(
            firstContext.Identity,
            new BatchId(1),
            1,
            0,
            0,
            terminal,
            new ServiceNativeCallTotals(0, 0, 0),
            new MonotonicTimestamp(20));

        var second = Evaluate(
            ReadyFrame(),
            Configuration(),
            first.State,
            Context(2, receipt));

        Assert.True(second.HasAction);
        Assert.Equal(AutoHarvestPair.FruitTree, second.Action.Pair);
        Assert.Equal(AutoHarvestPair.FruitTree, second.State.NextPair);
    }

    [Theory]
    [InlineData((int)AutoHarvestCaptureUnavailableReason.RegistryNotReady, (int)AutoHarvestPairHealthKind.RegistryNotReady)]
    [InlineData((int)AutoHarvestCaptureUnavailableReason.Faulted, (int)AutoHarvestPairHealthKind.Faulted)]
    public void PairScopedFailureDoesNotBlockEligibleSibling(
        int reason,
        int expectedHealth)
    {
        var fruit = AutoHarvestPairCapture.Unavailable(
            AutoHarvestPair.FruitTree,
            (AutoHarvestCaptureUnavailableReason)reason,
            AutoHarvestCaptureFailureScope.Pair);
        var treasure = Captured(AutoHarvestPair.TreasureTree, ReadyFacts());
        var frame = new AutoHarvestCycleFrame(fruit, treasure, ownsActionFamily: true);

        var result = Evaluate(frame, Configuration(), InitialState(), Context(1));

        Assert.True(result.HasAction);
        Assert.Equal(AutoHarvestPair.TreasureTree, result.Action.Pair);
        Assert.Equal((AutoHarvestPairHealthKind)expectedHealth, result.State.FruitHealth.Kind);
        Assert.Equal(AutoHarvestPairHealthKind.Eligible, result.State.TreasureHealth.Kind);
    }

    [Fact]
    public void FeatureScopedFailureBlocksAnEligibleSibling()
    {
        var fruit = AutoHarvestPairCapture.Unavailable(
            AutoHarvestPair.FruitTree,
            AutoHarvestCaptureUnavailableReason.ContractUnavailable,
            AutoHarvestCaptureFailureScope.Feature);
        var treasure = Captured(AutoHarvestPair.TreasureTree, ReadyFacts());
        var frame = new AutoHarvestCycleFrame(fruit, treasure, ownsActionFamily: true);

        var result = Evaluate(frame, Configuration(), InitialState(), Context(1));

        Assert.False(result.HasAction);
        Assert.False(result.State.HasPlannedAction);
        Assert.True(result.State.FruitHealth.FeatureScoped);
        Assert.Equal(AutoHarvestPairHealthKind.Eligible, result.State.TreasureHealth.Kind);
    }

    private static AutoHarvestCycleEvaluation Evaluate(
        in AutoHarvestCycleFrame frame,
        in AutomataConfiguration config,
        in AutoHarvestCycleState state,
        in ServiceCycleContext context) =>
        new AutoHarvestCycleEvaluator().Evaluate(frame, config, state, context);

    private static AutoHarvestCycleFrame ReadyFrame()
    {
        var facts = ReadyFacts();
        var fruit = Captured(AutoHarvestPair.FruitTree, facts);
        var treasure = Captured(AutoHarvestPair.TreasureTree, facts);
        return new AutoHarvestCycleFrame(fruit, treasure, ownsActionFamily: true);
    }

    private static AutoHarvestPairCapture Captured(
        AutoHarvestPair pair,
        in AutoHarvestPairFacts facts) =>
        AutoHarvestPairCapture.Captured(pair, facts);

    private static AutoHarvestPairFacts ReadyFacts() => new(
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified,
        AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
        AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState.Verified);

    private static AutomataConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: true,
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

    private static AutoHarvestCycleState InitialState() =>
        AutoHarvestCycleState.Create(new LifecycleGeneration(1));

    private static ServiceCycleContext Context(ulong cycle, BatchReceipt receipt = default)
    {
        var identity = new ServiceCycleIdentity(
            new ServiceId("orbautomata.auto-harvest.service-cycle"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(cycle),
            new CycleId(cycle));
        return new ServiceCycleContext(identity, receipt, new MonotonicTimestamp(10 + (long)cycle));
    }
}
