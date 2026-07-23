using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestReplayAdapterTests
{
    [Fact]
    public void HydratorReconstructsTheRecordedEvaluatorInputs()
    {
        var input = DisabledInput();
        var state = AutoHarvestCycleState.Create(new LifecycleGeneration(1));
        var stateCodec = new AutoHarvestStateCodec();
        var stateBytes = new byte[stateCodec.Descriptor.MaximumEncodedBytes];
        stateCodec.Encode(new AutoHarvestStateRecord(state), stateBytes);
        var stateRecord = stateCodec.Decode(stateBytes);
        var context = ReplayContext(1);
        var hydrator = new AutoHarvestReplayHydrator();
        var frame = default(AutoHarvestCycleFrame);

        hydrator.HydrateFrame(input, context, ref frame);
        var config = hydrator.HydrateConfiguration(input, context);
        var hydratedState = hydrator.HydratePreviousState(stateRecord, context);
        var recreated = hydrator.RecreateCycleInputRecord(frame, config, context);

        Assert.True(new AutoHarvestCycleInputComparer().Compare(input, recreated).IsMatch);
        Assert.True(new AutoHarvestStateComparer().Compare(
            stateRecord,
            new AutoHarvestStateRecord(hydratedState)).IsMatch);
    }

    [Fact]
    public void HydratorRejectsStateFromAnotherLifecycle()
    {
        var state = AutoHarvestCycleState.Create(new LifecycleGeneration(2));
        var record = new AutoHarvestStateRecord(state);

        Assert.Throws<InvalidOperationException>(() =>
            new AutoHarvestReplayHydrator().HydratePreviousState(record, ReplayContext(1)));
    }

    [Fact]
    public void ProjectionPublishesOnlyTheNineStableDomainFields()
    {
        var state = AutoHarvestCycleState.Create(new LifecycleGeneration(1));
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);

        AutoHarvestServiceProjection.Write(state, builder);
        var snapshot = builder.CaptureSnapshot();

        Assert.Equal(9, snapshot.Count);
        for (var index = 0; index < snapshot.Count; index++)
            Assert.Equal(index + 1, snapshot.GetEntry(index).Key.Value);
        Assert.Equal((int)AutoHarvestPair.FruitTree, snapshot.GetEntry(0).Value.Integer);
        Assert.False(snapshot.GetEntry(1).Value.Boolean);
        Assert.Equal((int)AutoHarvestPairHealthKind.NotObserved, snapshot.GetEntry(4).Value.Integer);
        Assert.Equal((int)AutoHarvestPairHealthKind.NotObserved, snapshot.GetEntry(7).Value.Integer);
    }

    [Fact]
    public void PairHealthProjectionRoundTripsThroughItsOwnedSchema()
    {
        var fruit = new AutoHarvestPairHealth(
            AutoHarvestPair.FruitTree,
            selected: true,
            AutoHarvestPairHealthKind.Faulted,
            featureScoped: true);
        var treasure = AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree);
        var state = AutoHarvestCycleState.Restore(
            new LifecycleGeneration(1),
            AutoHarvestPair.TreasureTree,
            hasPlannedAction: false,
            plannedPair: default,
            fruitHealth: fruit,
            treasureHealth: treasure);
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);

        AutoHarvestServiceProjection.Write(state, builder);
        var snapshot = builder.CaptureSnapshot();

        Assert.True(AutoHarvestServiceProjection.TryReadFruitHealth(in snapshot, out var decodedFruit));
        Assert.True(AutoHarvestServiceProjection.TryReadTreasureHealth(in snapshot, out var decodedTreasure));
        AssertHealth(fruit, decodedFruit);
        AssertHealth(treasure, decodedTreasure);
    }

    [Fact]
    public void PairHealthProjectionRejectsMalformedValueKinds()
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        builder.Add(new ServiceProjectionKey(4), ServiceProjectionValue.FromBoolean(true));
        builder.Add(new ServiceProjectionKey(5), ServiceProjectionValue.FromBoolean(true));
        builder.Add(new ServiceProjectionKey(6), ServiceProjectionValue.FromBoolean(false));
        var snapshot = builder.CaptureSnapshot();

        Assert.False(AutoHarvestServiceProjection.TryReadFruitHealth(in snapshot, out _));
    }

    private static void AssertHealth(
        in AutoHarvestPairHealth expected,
        in AutoHarvestPairHealth actual)
    {
        Assert.Equal(expected.Pair, actual.Pair);
        Assert.Equal(expected.Selected, actual.Selected);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.FeatureScoped, actual.FeatureScoped);
    }

    private static AutoHarvestCycleInputRecord DisabledInput()
    {
        var fruit = AutoHarvestPairCapture.NotSelected(AutoHarvestPair.FruitTree);
        var treasure = AutoHarvestPairCapture.NotSelected(AutoHarvestPair.TreasureTree);
        var frame = new AutoHarvestCycleFrame(fruit, treasure, ownsActionFamily: true);
        var config = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: false,
            treasureSelected: false,
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        return new AutoHarvestCycleInputRecord(frame, config);
    }

    private static ServiceCycleReplayContext ReplayContext(ulong lifecycle)
    {
        var identity = new ServiceCycleIdentity(
            new ServiceId("orbautomata.auto-harvest.service-cycle"),
            new LifecycleGeneration(lifecycle),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(1));
        return new ServiceCycleReplayContext(1, context);
    }
}
