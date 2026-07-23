using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayFooterValueDecoder
{
    internal static ServiceCycleReplayCompleteness ReadCompleteness(
        ReadOnlySpan<byte> source,
        int offset,
        ServiceCycleReplayFormatErrorCode errorCode,
        int index)
    {
        var codeValue = ServiceCycleReplayBinary.I32(source, offset);
        var scopeValue = ServiceCycleReplayBinary.I32(source, offset + 4);
        var kindValue = ServiceCycleReplayBinary.I32(source, offset + 8);
        var recordIndex = ServiceCycleReplayBinary.I32(source, offset + 12);
        if (codeValue == (int)ServiceCycleReplayCompletenessCode.Complete)
        {
            if (scopeValue != 0 || kindValue != 0 || recordIndex != 0) throw Error(errorCode, index);
            return ServiceCycleReplayCompleteness.Complete;
        }
        if (codeValue is < (int)ServiceCycleReplayCompletenessCode.ByteBudgetExhausted or
                > (int)ServiceCycleReplayCompletenessCode.ExecutionIncomplete &&
                codeValue != (int)ServiceCycleReplayCompletenessCode.RecordCapacityExhausted)
            throw Error(errorCode, index);
        try
        {
            var scope = (ServiceCycleReplayFailureScope)scopeValue;
            var location = scope switch
            {
                ServiceCycleReplayFailureScope.Record => ServiceCycleReplayFailureLocation.AtRecord(
                    new ServiceCycleReplayRecordIdentity((ServiceCycleReplayRecordKind)kindValue, recordIndex)),
                ServiceCycleReplayFailureScope.Container when kindValue == 0 && recordIndex == 0 =>
                    ServiceCycleReplayFailureLocation.Container,
                ServiceCycleReplayFailureScope.SemanticTrace when kindValue == 0 && recordIndex == 0 =>
                    ServiceCycleReplayFailureLocation.SemanticTrace,
                ServiceCycleReplayFailureScope.Cycle when kindValue == 0 && recordIndex == 0 =>
                    ServiceCycleReplayFailureLocation.Cycle,
                ServiceCycleReplayFailureScope.Execution when kindValue == 0 && recordIndex == 0 =>
                    ServiceCycleReplayFailureLocation.Execution,
                _ => throw Error(errorCode, index),
            };
            return ServiceCycleReplayCompleteness.Incomplete(
                (ServiceCycleReplayCompletenessCode)codeValue, location);
        }
        catch (ArgumentException) { throw Error(errorCode, index); }
    }

    internal static WakePolicy ReadWake(ReadOnlySpan<byte> row, bool present, int index)
    {
        var kindValue = ServiceCycleReplayBinary.I32(row, 136);
        var delay = ServiceCycleReplayBinary.I64(row, 144);
        var due = ServiceCycleReplayBinary.I64(row, 152);
        if (!present)
        {
            if (kindValue != 0 || delay != 0 || due != 0) throw Error(index);
            return default;
        }
        return (WakePolicyKind)kindValue switch
        {
            WakePolicyKind.Default when delay == 0 && due == 0 => WakePolicy.Default,
            WakePolicyKind.Immediate when delay == 0 && due == 0 => WakePolicy.Immediate,
            WakePolicyKind.AfterDecision when delay >= 0 && due == 0 =>
                WakePolicy.AfterDecision(new MonotonicDuration(delay)),
            WakePolicyKind.AfterBatch when delay >= 0 && due == 0 =>
                WakePolicy.AfterBatch(new MonotonicDuration(delay)),
            WakePolicyKind.At when delay == 0 => WakePolicy.At(new MonotonicTimestamp(due)),
            _ => throw Error(index),
        };
    }

    internal static ServiceStateProjectionSnapshot ReadProjection(
        ReadOnlySpan<byte> row, bool present, int index)
    {
        var count = ServiceCycleReplayBinary.I32(row, 140);
        if (!present && count != 0 || count < 0 || count > ServiceStateProjectionSnapshot.MaximumEntryCount)
            throw Error(index);
        var keys = new ServiceProjectionKey[ServiceStateProjectionSnapshot.MaximumEntryCount];
        var values = new ServiceProjectionValue[ServiceStateProjectionSnapshot.MaximumEntryCount];
        for (var entryIndex = 0; entryIndex < ServiceStateProjectionSnapshot.MaximumEntryCount; entryIndex++)
        {
            var entry = row.Slice(384 + entryIndex * ServiceCycleReplayArtifactFormat.ProjectionEntryBytes,
                ServiceCycleReplayArtifactFormat.ProjectionEntryBytes);
            if (entryIndex >= count)
            {
                if (!ServiceCycleReplayBinary.IsZero(entry)) throw Error(index);
                continue;
            }
            var keyValue = ServiceCycleReplayBinary.I32(entry, 0);
            var kindValue = ServiceCycleReplayBinary.I32(entry, 4);
            var integer = ServiceCycleReplayBinary.I64(entry, 8);
            var floatingBits = ServiceCycleReplayBinary.I64(entry, 16);
            if (keyValue <= 0) throw Error(index);
            for (var earlier = 0; earlier < entryIndex; earlier++)
                if (keys[earlier].Value == keyValue) throw Error(index);
            keys[entryIndex] = new ServiceProjectionKey(keyValue);
            values[entryIndex] = (ServiceProjectionValueKind)kindValue switch
            {
                ServiceProjectionValueKind.Boolean when integer is 0 or 1 && floatingBits == 0 =>
                    ServiceProjectionValue.FromBoolean(integer != 0),
                ServiceProjectionValueKind.Integer when floatingBits == 0 =>
                    ServiceProjectionValue.FromInteger(integer),
                ServiceProjectionValueKind.FloatingPoint when integer == 0 => ReadFloating(floatingBits, index),
                _ => throw Error(index),
            };
        }
        return ServiceStateProjectionSnapshot.CopyFrom(keys, values, count);
    }

    private static ServiceProjectionValue ReadFloating(long bits, int index)
    {
        var value = BitConverter.Int64BitsToDouble(bits);
        if (double.IsNaN(value) || double.IsInfinity(value)) throw Error(index);
        return ServiceProjectionValue.FromFloatingPoint(value);
    }

    private static ServiceCycleReplayFormatException Error(int index) =>
        ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.CycleFooterInvalid, index);

    private static ServiceCycleReplayFormatException Error(
        ServiceCycleReplayFormatErrorCode code,
        int index) => ServiceCycleReplayBinary.Error(code, index);
}
