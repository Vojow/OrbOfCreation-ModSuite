using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public sealed partial class ServiceCycleReplaySession
{
    internal bool TryAppendRecord(
        in ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordIdentity identity,
        in ServiceCycleReplayCodecDescriptor descriptor,
        byte[] scratch,
        int encodedLength,
        out long sequence)
    {
        sequence = 0;
        if (!_encodingEnabled || Volatile.Read(ref _failureState) != 0) return false;
        lock (_commitGate)
        {
            if (Volatile.Read(ref _failureState) != 0) return false;
            EnsureStorageInitialized();
            if (_recordCount == _recordCapacity)
            {
                MarkIncomplete(
                    in cycle,
                    ServiceCycleReplayCompleteness.Incomplete(
                        ServiceCycleReplayCompletenessCode.RecordCapacityExhausted,
                        ServiceCycleReplayFailureLocation.AtRecord(identity)),
                    default);
                return false;
            }
            if (encodedLength > _byteCapacity - _byteCount)
            {
                MarkIncomplete(
                    in cycle,
                    ServiceCycleReplayCompleteness.Incomplete(
                        ServiceCycleReplayCompletenessCode.ByteBudgetExhausted,
                        ServiceCycleReplayFailureLocation.AtRecord(identity)),
                    default);
                return false;
            }

            var offset = _byteCount;
            if (encodedLength != 0)
                Buffer.BlockCopy(scratch, 0, _bytes!, offset, encodedLength);
            sequence = checked(_recordSequence + 1);
            _records![_recordCount] = new ServiceCycleReplayRecordHeader(
                sequence, cycle, identity, descriptor.SchemaVersion, offset, encodedLength);
            BeginFenceWrite();
            _byteCount = checked(offset + encodedLength);
            _recordCount++;
            _recordSequence = sequence;
            EndFenceWrite();
            return true;
        }
    }

    internal bool TryAppendFooter(
        in ServiceCycleReplayCycleFooter footer,
        out long sequence)
    {
        sequence = 0;
        if (!_encodingEnabled) return false;
        lock (_commitGate)
        {
            EnsureStorageInitialized();
            if (_footerCount == _cycleFooterCapacity)
            {
                var cycle = footer.Context.Cycle;
                MarkIncomplete(
                    in cycle,
                    ServiceCycleReplayCompleteness.Incomplete(
                        ServiceCycleReplayCompletenessCode.CycleIncomplete,
                        ServiceCycleReplayFailureLocation.Cycle),
                    default);
                return false;
            }

            sequence = checked(_footerSequence + 1);
            _footers![_footerCount] = footer.WithSequence(sequence);
            BeginFenceWrite();
            _footerCount++;
            _footerSequence = sequence;
            EndFenceWrite();
            if (_offlineFooterWaiterCount != 0)
            {
                _offlineFooterWakePulseCount++;
                Monitor.PulseAll(_commitGate);
            }
            return true;
        }
    }

    private void EnsureStorageInitialized()
    {
        if (_bytes is not null) return;
        var bytes = new byte[_byteCapacity];
        var records = new ServiceCycleReplayRecordHeader[_recordCapacity];
        var footers = new ServiceCycleReplayCycleFooter[_cycleFooterCapacity];
        _bytes = bytes;
        _records = records;
        _footers = footers;
    }
}
