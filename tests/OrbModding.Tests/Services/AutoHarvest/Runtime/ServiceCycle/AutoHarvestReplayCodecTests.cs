using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestReplayCodecTests
{
    [Fact]
    public void DetachedRecordGraphsContainOnlyReviewedValues()
    {
        Assert.True(ServiceCycleReplayRecordValidator.Validate<AutoHarvestCycleInputRecord>().IsValid);
        Assert.True(ServiceCycleReplayRecordValidator.Validate<AutoHarvestStateRecord>().IsValid);
        Assert.True(ServiceCycleReplayRecordValidator.Validate<AutoHarvestActionRecord>().IsValid);
    }

    [Fact]
    public void FixedCodecsRoundTripEveryRecordRole()
    {
        var input = InputRecord(TimeSpan.FromMilliseconds(750));
        var state = StateRecord(AutoHarvestPair.TreasureTree);
        var action = new AutoHarvestActionRecord(
            new AutoHarvestCycleAction(AutoHarvestPair.TreasureTree));

        AssertRoundTrip(
            input,
            new AutoHarvestCycleInputCodec(),
            new AutoHarvestCycleInputComparer());
        AssertRoundTrip(
            state,
            new AutoHarvestStateCodec(),
            new AutoHarvestStateComparer());
        AssertRoundTrip(
            action,
            new AutoHarvestActionCodec(),
            new AutoHarvestActionComparer());
    }

    [Fact]
    public void InputCodecRejectsAnUnknownCaptureReason()
    {
        var codec = new AutoHarvestCycleInputCodec();
        var bytes = Encode(InputRecord(TimeSpan.FromSeconds(1)), codec);
        bytes[1] = byte.MaxValue;

        Assert.Throws<ArgumentException>(() => codec.Decode(bytes));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(16, true)]
    public void InputCodecRejectsSelectionCaptureMismatch(int selectionFlag, bool set)
    {
        var codec = new AutoHarvestCycleInputCodec();
        var bytes = Encode(InputRecord(TimeSpan.FromSeconds(1)), codec);
        bytes[22] = set
            ? (byte)(bytes[22] | selectionFlag)
            : (byte)(bytes[22] & ~selectionFlag);

        Assert.Throws<ArgumentException>(() => codec.Decode(bytes));
    }

    [Fact]
    public void ComparersReturnStableFieldCodes()
    {
        var firstInput = InputRecord(TimeSpan.FromSeconds(1));
        var changedInput = InputRecord(TimeSpan.FromSeconds(2));
        var inputMismatch = new AutoHarvestCycleInputComparer().Compare(firstInput, changedInput);

        var firstState = StateRecord(AutoHarvestPair.FruitTree);
        var changedState = StateRecord(AutoHarvestPair.TreasureTree);
        var stateMismatch = new AutoHarvestStateComparer().Compare(firstState, changedState);

        var fruitAction = new AutoHarvestActionRecord(new AutoHarvestCycleAction(AutoHarvestPair.FruitTree));
        var treasureAction = new AutoHarvestActionRecord(new AutoHarvestCycleAction(AutoHarvestPair.TreasureTree));
        var actionMismatch = new AutoHarvestActionComparer().Compare(fruitAction, treasureAction);

        Assert.Equal(29, inputMismatch.FieldCode);
        Assert.Equal(2, stateMismatch.FieldCode);
        Assert.Equal(1, actionMismatch.FieldCode);
    }

    private static AutoHarvestCycleInputRecord InputRecord(TimeSpan interval)
    {
        var facts = new AutoHarvestPairFacts(
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Rejected,
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Unknown,
            AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Rejected);
        var fruit = AutoHarvestPairCapture.Captured(AutoHarvestPair.FruitTree, facts);
        var treasure = AutoHarvestPairCapture.NotSelected(AutoHarvestPair.TreasureTree);
        var frame = new AutoHarvestCycleFrame(fruit, treasure, ownsActionFamily: false);
        var config = AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: true,
            treasureSelected: false,
            MonotonicDuration.FromTimeSpan(interval));
        return new AutoHarvestCycleInputRecord(frame, config);
    }

    private static AutoHarvestStateRecord StateRecord(AutoHarvestPair nextPair)
    {
        var fruit = new AutoHarvestPairHealthRecord(
            AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree));
        var treasure = new AutoHarvestPairHealthRecord(
            new AutoHarvestPairHealth(
                AutoHarvestPair.TreasureTree,
                selected: true,
                AutoHarvestPairHealthKind.Faulted,
                featureScoped: true));
        return new AutoHarvestStateRecord(
            lifecycle: 4,
            nextPair,
            hasPlannedAction: true,
            plannedPair: AutoHarvestPair.FruitTree,
            fruit,
            treasure);
    }

    private static void AssertRoundTrip<TRecord>(
        TRecord record,
        IServiceCycleReplayCodec<TRecord> codec,
        IServiceCycleReplayComparer<TRecord> comparer)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        var decoded = codec.Decode(Encode(record, codec));
        Assert.True(comparer.Compare(record, decoded).IsMatch);
    }

    private static byte[] Encode<TRecord>(
        TRecord record,
        IServiceCycleReplayCodec<TRecord> codec)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        var bytes = new byte[codec.Descriptor.MaximumEncodedBytes];
        Assert.Equal(bytes.Length, codec.Encode(record, bytes));
        return bytes;
    }
}
