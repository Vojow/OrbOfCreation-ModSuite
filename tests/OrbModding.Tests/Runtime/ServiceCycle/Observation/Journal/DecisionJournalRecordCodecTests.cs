using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalRecordCodecTests
{
    [Fact]
    public void DecisionRoundTripsEveryNumericField()
    {
        var expected = DecisionJournalRecord.Decision(CreateObservation(1, 10, faultOccurrence: 2));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];

        DecisionJournalRecordCodec.Write(bytes, in expected);
        var actual = DecisionJournalRecordCodec.Read(bytes);

        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Service, actual.Service);
        Assert.Equal(expected.FirstCycle, actual.FirstCycle);
        Assert.Equal(expected.LastTimestampTicks, actual.LastTimestampTicks);
        Assert.Equal(expected.Wake, actual.Wake);
        Assert.Equal(expected.FaultCategory, actual.FaultCategory);
        Assert.Equal(expected.TerminalDisposition, actual.TerminalDisposition);
        Assert.Equal(expected.NativeCallsAttempted, actual.NativeCallsAttempted);
        Assert.True(DecisionJournalProjection.Equals(expected.Projection, actual.Projection));
    }

    [Fact]
    public void GlobalTransitionRoundTripsWithoutServiceIdentity()
    {
        var expected = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.EmergencyEntered,
            default,
            0,
            new MonotonicTimestamp(42),
            code: 3);
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];

        DecisionJournalRecordCodec.Write(bytes, in expected);
        var actual = DecisionJournalRecordCodec.Read(bytes);

        Assert.Equal(expected.Kind, actual.Kind);
        Assert.False(actual.Service.IsValid);
        Assert.Equal(3, actual.TransitionCode);
    }

    [Fact]
    public void WorldGateHoldRoundTripsItsServiceLifecycleAndReason()
    {
        var expected = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.WorldGateHeld,
            new ServiceCycleTraceServiceId(2),
            5,
            new MonotonicTimestamp(42),
            code: 2);
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];

        DecisionJournalRecordCodec.Write(bytes, in expected);
        var actual = DecisionJournalRecordCodec.Read(bytes);

        Assert.Equal(DecisionJournalRecordKind.WorldGateHeld, actual.Kind);
        Assert.Equal(expected.Service, actual.Service);
        Assert.Equal((ulong)5, actual.Lifecycle);
        Assert.Equal(2, actual.TransitionCode);
    }

    [Fact]
    public void EveryTerminalDispositionRoundTripsCanonicalAggregate()
    {
        var cycle = Identity(1);
        var completed = BatchReceipt.Completed(
            cycle,
            new BatchId(1),
            1,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(11));
        var skipped = BatchReceipt.Completed(
            cycle,
            new BatchId(5),
            1,
            0,
            new ServiceNativeCallTotals(1, 1, 0),
            new MonotonicTimestamp(11));
        var rejectedAction = ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        var rejected = BatchReceipt.Terminated(
            cycle,
            new BatchId(2),
            2,
            1,
            1,
            rejectedAction,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(11));
        var faultedAction = ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        var faulted = BatchReceipt.Terminated(
            cycle,
            new BatchId(3),
            1,
            0,
            0,
            faultedAction,
            default,
            new MonotonicTimestamp(11));
        var orphaned = BatchReceipt.Orphaned(
            cycle,
            new BatchId(4),
            2,
            1,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(11));
        var published = BatchReceipt.Completed(
            cycle,
            new BatchId(6),
            actionCount: 2,
            committedCount: 2,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(11),
            publishedCount: 1);
        var receipts = new[] { completed, skipped, rejected, faulted, orphaned, published };
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];

        foreach (var receipt in receipts)
        {
            var observation = CreateObservation(in receipt, 10);
            var expected = DecisionJournalRecord.Decision(in observation);
            DecisionJournalRecordCodec.Write(bytes, in expected);

            var actual = DecisionJournalRecordCodec.Read(bytes);

            Assert.Equal(receipt.Disposition, actual.TerminalDisposition);
            Assert.Equal(receipt.ResultCode.Value, actual.TerminalResultCode);
            Assert.Equal(receipt.CommittedCount, actual.CommittedActions);
            Assert.Equal(receipt.PublishedCount, actual.PublishedActions);
        }
    }

    [Fact]
    public void ReservedBytesAreRejected()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        bytes[^1] = 1;

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void NonCanonicalBooleanProjectionIsRejected()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        bytes[188] = 1;
        bytes[192] = 2;

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Theory]
    [InlineData((int)DecisionJournalRecordKind.ConfigurationChanged, 16)]
    [InlineData((int)DecisionJournalRecordKind.LifecycleChanged, 24)]
    [InlineData((int)DecisionJournalRecordKind.ConfigurationChanged, 32)]
    public void TransitionRejectsNonOwnedGeneration(int kindValue, int offset)
    {
        var kind = (DecisionJournalRecordKind)kindValue;
        var record = DecisionJournalRecord.Transition(
            kind,
            kind == DecisionJournalRecordKind.LifecycleChanged
                ? new ServiceCycleTraceServiceId(1)
                : default,
            2,
            new MonotonicTimestamp(3));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), 9);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void CompletedTerminalRejectsFaultResultCode()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(136, 4),
            CommonActionResultCodes.AdapterFault.Value);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void FaultRejectsNonFaultResultCode()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10, faultOccurrence: 1));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(120, 4),
            CommonActionResultCodes.Committed.Value);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void CompletedTerminalRejectsMissingCommittedActions()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(144, 8), 0);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void TerminatedReceiptRejectsCommittedTerminalPosition()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(132, 4),
            (int)BatchTerminalDisposition.Rejected);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(136, 4),
            CommonActionResultCodes.PolicyRejected.Value);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void TerminalRejectsMissingCycleIdentity()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        bytes.AsSpan(40, 32).Clear();

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void CapturedSpanRejectsRangeCardinalityMismatch()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64, 8), 2);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void MorePublishedActionsThanCommittedAreRejected()
    {
        var record = PublicationRecord();
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(PublishedActionsOffset, 8), 2);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    /// <summary>
    /// A span whose every action published made no native call, so any native evidence in it is a lie
    /// about which action produced it.
    /// </summary>
    [Fact]
    public void AFullyPublishedSpanCannotCarryNativeEvidence()
    {
        var record = PublicationRecord();
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(152, 8), 1);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(160, 8), 1);

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    private const int PublishedActionsOffset = 440;

    private static DecisionJournalRecord PublicationRecord()
    {
        var receipt = BatchReceipt.Completed(
            Identity(1),
            new BatchId(1),
            actionCount: 1,
            committedCount: 1,
            default,
            new MonotonicTimestamp(11),
            publishedCount: 1);
        var observation = CreateObservation(in receipt, 10);
        return DecisionJournalRecord.Decision(in observation);
    }
}
