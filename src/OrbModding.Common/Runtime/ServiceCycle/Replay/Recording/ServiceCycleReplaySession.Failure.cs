using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public sealed partial class ServiceCycleReplaySession
{
    internal void MarkRequiredRecordMissing(
        in ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordIdentity identity) => MarkIncomplete(
            in cycle,
            ServiceCycleReplayCompleteness.Incomplete(
                ServiceCycleReplayCompletenessCode.RequiredRecordMissing,
                ServiceCycleReplayFailureLocation.AtRecord(identity)),
            default);

    internal void MarkCodecContractRejected(
        in ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordIdentity identity,
        ServiceCycleReplayCodecContractCode detail) => MarkIncomplete(
            in cycle,
            ServiceCycleReplayCompleteness.Incomplete(
                ServiceCycleReplayCompletenessCode.CodecContractRejected,
                ServiceCycleReplayFailureLocation.AtRecord(identity)),
            new ServiceCycleReplayFault(
                ServiceCycleReplayFaultCode.CodecContractRejected,
                ServiceCycleReplayFailureLocation.AtRecord(identity),
                (int)detail));

    internal void MarkCodecThrew(
        in ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordIdentity identity) => MarkIncomplete(
            in cycle,
            ServiceCycleReplayCompleteness.Incomplete(
                ServiceCycleReplayCompletenessCode.CodecFaulted,
                ServiceCycleReplayFailureLocation.AtRecord(identity)),
            new ServiceCycleReplayFault(
                ServiceCycleReplayFaultCode.CodecThrew,
                ServiceCycleReplayFailureLocation.AtRecord(identity)));

    internal bool TryReadFailure(
        out ServiceCycleReplayCycleKey cycle,
        out ServiceCycleReplayCompleteness completeness,
        out ServiceCycleReplayFault fault)
    {
        if (Volatile.Read(ref _failureState) != 1)
        {
            cycle = default;
            completeness = ServiceCycleReplayCompleteness.Complete;
            fault = default;
            return false;
        }

        cycle = _firstIncompleteCycle;
        completeness = _completeness;
        fault = _fault;
        return true;
    }

    internal ServiceCycleReplayRecordHeader ReadRecordHeader(int index, in ServiceCycleReplayHighWaterFence fence)
    {
        if ((uint)index >= (uint)fence.RecordCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _records![index];
    }

    internal ServiceCycleReplayCycleFooter ReadFooter(int index, in ServiceCycleReplayHighWaterFence fence)
    {
        if ((uint)index >= (uint)fence.FooterCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _footers![index];
    }

    internal void CopyBytes(int offset, Span<byte> destination, in ServiceCycleReplayHighWaterFence fence)
    {
        if (offset < 0 || destination.Length > fence.ByteCount - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _bytes!.AsSpan(offset, destination.Length).CopyTo(destination);
    }

    private void MarkIncomplete(
        in ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayCompleteness completeness,
        ServiceCycleReplayFault fault)
    {
        if (Interlocked.CompareExchange(ref _failureState, -1, 0) != 0) return;
        BeginFenceWrite();
        _firstIncompleteCycle = cycle;
        _completeness = completeness;
        _fault = fault;
        Volatile.Write(ref _failureState, 1);
        EndFenceWrite();
    }

    private void BeginFenceWrite()
    {
        Interlocked.Increment(ref _snapshotWriters);
        Interlocked.Increment(ref _fenceVersion);
    }

    private void EndFenceWrite()
    {
        Interlocked.Increment(ref _fencePublication);
        Interlocked.Increment(ref _fenceVersion);
        Interlocked.Decrement(ref _snapshotWriters);
    }
}
