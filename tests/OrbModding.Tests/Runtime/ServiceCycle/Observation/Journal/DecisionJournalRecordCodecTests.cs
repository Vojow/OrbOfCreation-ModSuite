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
    public void EveryTerminalDispositionRoundTripsCanonicalAggregate()
    {
        var cycle = Identity(1);
        var completed = BatchReceipt.Completed(
            cycle,
            new BatchId(1),
            1,
            new ServiceNativeCallTotals(1, 1, 1),
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
        var receipts = new[] { completed, rejected, faulted, orphaned };
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];

        foreach (var receipt in receipts)
        {
            var observation = CreateObservation(in receipt, 10);
            var expected = DecisionJournalRecord.Decision(in observation);
            DecisionJournalRecordCodec.Write(bytes, in expected);

            var actual = DecisionJournalRecordCodec.Read(bytes);

            Assert.Equal(receipt.Disposition, actual.TerminalDisposition);
            Assert.Equal(receipt.ResultCode.Value, actual.TerminalResultCode);
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
        var record = DecisionJournalRecord.Transition(
            (DecisionJournalRecordKind)kindValue,
            new ServiceCycleTraceServiceId(1),
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
}
