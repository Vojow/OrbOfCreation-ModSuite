using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Contracts;

public sealed class ServiceCycleReplayContractTests
{
    [Fact]
    public void CodecDescriptorRequiresAnExplicitVersionAndPositiveFiniteBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleReplayCodecDescriptor(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCycleReplayCodecDescriptor(ushort.MaxValue + 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleReplayCodecDescriptor(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleReplayCodecDescriptor(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleReplayCodecDescriptor(
            1, ServiceCycleReplayCodecLimits.MaximumEncodedBytes + 1));

        var descriptor = new ServiceCycleReplayCodecDescriptor(
            ushort.MaxValue, ServiceCycleReplayCodecLimits.MaximumEncodedBytes);
        Assert.True(descriptor.IsValid);
        Assert.Equal(ushort.MaxValue, descriptor.SchemaVersion);
        Assert.Equal(ServiceCycleReplayCodecLimits.MaximumEncodedBytes, descriptor.MaximumEncodedBytes);
    }

    [Fact]
    public void CodecGuardRejectsEveryCapacityContractViolationWithStableCode()
    {
        var descriptor = new ServiceCycleReplayCodecDescriptor(3, 16);
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.InvalidDescriptor,
            ServiceCycleReplayCodecContract.ValidateDescriptor(default));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.NegativeEncodedLength,
            ServiceCycleReplayCodecContract.ValidateEncodeResult(in descriptor, 16, -1));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.EncodedLengthExceedsBound,
            ServiceCycleReplayCodecContract.ValidateEncodeResult(in descriptor, 32, 17));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.EncodedLengthExceedsDestination,
            ServiceCycleReplayCodecContract.ValidateEncodeResult(in descriptor, 8, 9));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.DestinationBelowPromisedCapacity,
            ServiceCycleReplayCodecContract.ValidateEncodeResult(in descriptor, 8, 8));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.SourceExceedsBound,
            ServiceCycleReplayCodecContract.ValidateDecodeSource(in descriptor, 17));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.SourceExceedsBound,
            ServiceCycleReplayCodecContract.ValidateDecodeSource(in descriptor, -1));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.Valid,
            ServiceCycleReplayCodecContract.ValidateEncodeResult(in descriptor, 16, 16));
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.Valid,
            ServiceCycleReplayCodecContract.ValidateDecodeSource(in descriptor, 0));
    }

    [Fact]
    public void RecordIdentitiesAreBoundedByStableRecordRole()
    {
        Assert.True(new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0).IsValid);
        Assert.True(new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, 500).IsValid);
        Assert.Throws<ArgumentException>(() =>
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.NextState, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, -1));
        Assert.False(default(ServiceCycleReplayRecordIdentity).IsValid);
    }

    [Fact]
    public void ComparisonCarriesOnlyStableNumericMismatchMetadata()
    {
        Assert.True(ServiceCycleReplayRecordComparison.Match.IsMatch);
        Assert.True(ServiceCycleReplayRecordComparison.Match.IsValid);

        var mismatch = new ServiceCycleReplayRecordComparison(27, 4);
        Assert.False(mismatch.IsMatch);
        Assert.True(mismatch.IsValid);
        Assert.Equal(27, mismatch.FieldCode);
        Assert.Equal(4, mismatch.ElementIndex);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleReplayRecordComparison(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleReplayRecordComparison(1, -1));
    }

    [Fact]
    public void CompletenessNamesTheFirstMissingRecordAndDefaultIsNotComplete()
    {
        var action = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, 9);
        var incomplete = ServiceCycleReplayCompleteness.Incomplete(
            ServiceCycleReplayCompletenessCode.ByteBudgetExhausted,
            ServiceCycleReplayFailureLocation.AtRecord(action));

        Assert.True(ServiceCycleReplayCompleteness.Complete.IsComplete);
        Assert.True(ServiceCycleReplayCompleteness.Complete.IsValid);
        Assert.False(default(ServiceCycleReplayCompleteness).IsValid);
        Assert.False(incomplete.IsComplete);
        Assert.True(incomplete.IsValid);
        Assert.Equal(action, incomplete.FailureLocation.Record);
        Assert.Equal(ServiceCycleReplayFailureScope.Record, incomplete.FailureLocation.Scope);
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceCycleReplayCompleteness.Incomplete(
            ServiceCycleReplayCompletenessCode.Complete,
            ServiceCycleReplayFailureLocation.AtRecord(action)));
        Assert.Throws<ArgumentException>(() => ServiceCycleReplayCompleteness.Incomplete(
            ServiceCycleReplayCompletenessCode.RequiredRecordMissing, default));
        Assert.Throws<ArgumentException>(() => ServiceCycleReplayCompleteness.Incomplete(
            ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete,
            ServiceCycleReplayFailureLocation.AtRecord(action)));

        var traceIncomplete = ServiceCycleReplayCompleteness.Incomplete(
            ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete,
            ServiceCycleReplayFailureLocation.SemanticTrace);
        Assert.True(traceIncomplete.IsValid);
        Assert.False(traceIncomplete.FailureLocation.Record.IsValid);
    }

    [Fact]
    public void FaultMetadataSeparatesRecordAndCycleLevelFailures()
    {
        var input = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0);
        var codecFault = new ServiceCycleReplayFault(
            ServiceCycleReplayFaultCode.CodecThrew,
            ServiceCycleReplayFailureLocation.AtRecord(input),
            31);
        var executionFault = new ServiceCycleReplayFault(
            ServiceCycleReplayFaultCode.ExecutionFaulted,
            ServiceCycleReplayFailureLocation.Execution,
            4);

        Assert.True(codecFault.IsValid);
        Assert.True(executionFault.IsValid);
        Assert.Equal(31, codecFault.DetailCode);
        Assert.Throws<ArgumentException>(() =>
            new ServiceCycleReplayFault(
                ServiceCycleReplayFaultCode.CodecThrew,
                ServiceCycleReplayFailureLocation.Cycle));
        Assert.Throws<ArgumentException>(() =>
            new ServiceCycleReplayFault(
                ServiceCycleReplayFaultCode.ExecutionFaulted,
                ServiceCycleReplayFailureLocation.AtRecord(input)));
    }

    [Fact]
    public void FailureLocationsNeverInventRecordIdentityForNonRecordScopes()
    {
        var action = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, 4);
        var record = ServiceCycleReplayFailureLocation.AtRecord(action);
        var locations = new[]
        {
            ServiceCycleReplayFailureLocation.Container,
            ServiceCycleReplayFailureLocation.SemanticTrace,
            ServiceCycleReplayFailureLocation.Cycle,
            ServiceCycleReplayFailureLocation.Execution,
        };

        Assert.True(record.IsValid);
        Assert.Equal(action, record.Record);
        foreach (var location in locations)
        {
            Assert.True(location.IsValid);
            Assert.False(location.Record.IsValid);
        }
        Assert.False(default(ServiceCycleReplayFailureLocation).IsValid);
        Assert.Throws<ArgumentException>(() => ServiceCycleReplayFailureLocation.AtRecord(default));
    }

    [Fact]
    public void MismatchIdentityRequiresTheExactRecordRoleAndStableFieldCode()
    {
        var action = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.Action, 12);
        var mismatch = new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.Action, action, fieldCode: 7, elementIndex: 3);

        Assert.True(mismatch.IsValid);
        Assert.Equal(action, mismatch.Record);
        Assert.Equal(7, mismatch.FieldCode);
        Assert.Equal(3, mismatch.ElementIndex);
        Assert.Equal(mismatch, new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.Action, action, 7, 3));
        Assert.Throws<ArgumentException>(() => new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.CycleInput, action, 1));
        Assert.Throws<ArgumentException>(() => new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.WakePolicy, action, 1));
        Assert.Throws<ArgumentException>(() => new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.NextState, default, 1));
        Assert.Throws<ArgumentException>(() => new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.NativeOutcome, default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.Action, action, 0));

        var nativeMismatch = new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.NativeOutcome, action, fieldCode: 9);
        Assert.True(nativeMismatch.IsValid);
        Assert.Equal(action, nativeMismatch.Record);
        Assert.Throws<ArgumentException>(() => new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.NativeOutcome,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0),
            9));

        var wakeMismatch = new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.WakePolicy, default, fieldCode: 2);
        Assert.True(wakeMismatch.IsValid);
        Assert.False(wakeMismatch.Record.IsValid);
    }

    [Fact]
    public void PersistedEnumCodesRemainExplicitAndStable()
    {
        Assert.Equal(1, (int)ServiceCycleReplayRecordKind.CycleInput);
        Assert.Equal(2, (int)ServiceCycleReplayRecordKind.PreviousState);
        Assert.Equal(3, (int)ServiceCycleReplayRecordKind.NextState);
        Assert.Equal(4, (int)ServiceCycleReplayRecordKind.Action);

        Assert.Equal(1, (int)ServiceCycleReplayFailureScope.Record);
        Assert.Equal(2, (int)ServiceCycleReplayFailureScope.Container);
        Assert.Equal(3, (int)ServiceCycleReplayFailureScope.SemanticTrace);
        Assert.Equal(4, (int)ServiceCycleReplayFailureScope.Cycle);
        Assert.Equal(5, (int)ServiceCycleReplayFailureScope.Execution);

        Assert.Equal(1, (int)ServiceCycleReplayCompletenessCode.Complete);
        Assert.Equal(2, (int)ServiceCycleReplayCompletenessCode.ByteBudgetExhausted);
        Assert.Equal(3, (int)ServiceCycleReplayCompletenessCode.CodecContractRejected);
        Assert.Equal(4, (int)ServiceCycleReplayCompletenessCode.CodecFaulted);
        Assert.Equal(5, (int)ServiceCycleReplayCompletenessCode.RecordTypeRejected);
        Assert.Equal(6, (int)ServiceCycleReplayCompletenessCode.RequiredRecordMissing);
        Assert.Equal(7, (int)ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete);
        Assert.Equal(8, (int)ServiceCycleReplayCompletenessCode.ContainerIncomplete);
        Assert.Equal(9, (int)ServiceCycleReplayCompletenessCode.CycleIncomplete);
        Assert.Equal(10, (int)ServiceCycleReplayCompletenessCode.ExecutionIncomplete);
        Assert.Equal(11, (int)ServiceCycleReplayCompletenessCode.RecordCapacityExhausted);

        Assert.Equal(1, (int)ServiceCycleReplayFaultCode.RecordTypeRejected);
        Assert.Equal(2, (int)ServiceCycleReplayFaultCode.CodecContractRejected);
        Assert.Equal(3, (int)ServiceCycleReplayFaultCode.CodecThrew);
        Assert.Equal(4, (int)ServiceCycleReplayFaultCode.DecodeRejected);
        Assert.Equal(5, (int)ServiceCycleReplayFaultCode.ComparerThrew);
        Assert.Equal(6, (int)ServiceCycleReplayFaultCode.ContainerCorrupt);
        Assert.Equal(7, (int)ServiceCycleReplayFaultCode.SemanticTraceRejected);
        Assert.Equal(8, (int)ServiceCycleReplayFaultCode.CycleContextRejected);
        Assert.Equal(9, (int)ServiceCycleReplayFaultCode.EvaluatorFaulted);
        Assert.Equal(10, (int)ServiceCycleReplayFaultCode.ExecutionFaulted);

        Assert.Equal(1, (int)ServiceCycleReplayMismatchCode.CycleInput);
        Assert.Equal(2, (int)ServiceCycleReplayMismatchCode.PreviousState);
        Assert.Equal(3, (int)ServiceCycleReplayMismatchCode.NextState);
        Assert.Equal(4, (int)ServiceCycleReplayMismatchCode.Action);
        Assert.Equal(5, (int)ServiceCycleReplayMismatchCode.ActionCount);
        Assert.Equal(6, (int)ServiceCycleReplayMismatchCode.WakePolicy);
        Assert.Equal(7, (int)ServiceCycleReplayMismatchCode.NativeOutcome);
        Assert.Equal(8, (int)ServiceCycleReplayMismatchCode.BatchReceipt);
        Assert.Equal(9, (int)ServiceCycleReplayMismatchCode.SemanticEvent);

        Assert.Equal(1, (int)ServiceCycleReplayCodecContractCode.Valid);
        Assert.Equal(2, (int)ServiceCycleReplayCodecContractCode.InvalidDescriptor);
        Assert.Equal(3, (int)ServiceCycleReplayCodecContractCode.DestinationBelowPromisedCapacity);
        Assert.Equal(4, (int)ServiceCycleReplayCodecContractCode.NegativeEncodedLength);
        Assert.Equal(5, (int)ServiceCycleReplayCodecContractCode.EncodedLengthExceedsBound);
        Assert.Equal(6, (int)ServiceCycleReplayCodecContractCode.EncodedLengthExceedsDestination);
        Assert.Equal(7, (int)ServiceCycleReplayCodecContractCode.SourceExceedsBound);

        Assert.Equal(1, (int)ServiceCycleReplayRecordViolationCode.RootMustBeReadonlyRecord);
        Assert.Equal(2, (int)ServiceCycleReplayRecordViolationCode.ReferenceType);
        Assert.Equal(3, (int)ServiceCycleReplayRecordViolationCode.String);
        Assert.Equal(4, (int)ServiceCycleReplayRecordViolationCode.Object);
        Assert.Equal(5, (int)ServiceCycleReplayRecordViolationCode.Interface);
        Assert.Equal(6, (int)ServiceCycleReplayRecordViolationCode.Delegate);
        Assert.Equal(7, (int)ServiceCycleReplayRecordViolationCode.ArrayOrCollection);
        Assert.Equal(8, (int)ServiceCycleReplayRecordViolationCode.HandleOrPointer);
        Assert.Equal(9, (int)ServiceCycleReplayRecordViolationCode.Nullable);
        Assert.Equal(10, (int)ServiceCycleReplayRecordViolationCode.OpenOrConstructedGeneric);
        Assert.Equal(11, (int)ServiceCycleReplayRecordViolationCode.ByRefLike);
        Assert.Equal(12, (int)ServiceCycleReplayRecordViolationCode.MutableValueType);
        Assert.Equal(13, (int)ServiceCycleReplayRecordViolationCode.NativeOrRuntimeType);
        Assert.Equal(14, (int)ServiceCycleReplayRecordViolationCode.AmbientSource);
        Assert.Equal(15, (int)ServiceCycleReplayRecordViolationCode.UnsupportedPrimitive);
        Assert.Equal(16, (int)ServiceCycleReplayRecordViolationCode.ExplicitOrUnmanagedLayout);
        Assert.Equal(17, (int)ServiceCycleReplayRecordViolationCode.TypeGraphCycle);
        Assert.Equal(18, (int)ServiceCycleReplayRecordViolationCode.MaximumDepthExceeded);
        Assert.Equal(19, (int)ServiceCycleReplayRecordViolationCode.MaximumFlattenedScalarCountExceeded);
        Assert.Equal(20, (int)ServiceCycleReplayRecordViolationCode.ReflectionFailure);
        Assert.Equal(21, (int)ServiceCycleReplayRecordViolationCode.UnreviewedFrameworkValueType);
        Assert.Equal(22, (int)ServiceCycleReplayRecordViolationCode.StaticStorage);
        Assert.Equal(23, (int)ServiceCycleReplayRecordViolationCode.EmptyValueRecord);
        Assert.Equal(24, (int)ServiceCycleReplayRecordViolationCode.MissingReplayRecordMarker);
        Assert.Equal(25, (int)ServiceCycleReplayRecordViolationCode.MaximumInlineBytesExceeded);
    }
}
