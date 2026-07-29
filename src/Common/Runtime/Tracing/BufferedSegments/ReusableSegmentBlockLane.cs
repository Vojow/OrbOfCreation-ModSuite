using System;
using System.Threading;

namespace OrbModding.Common.Runtime.Tracing.BufferedSegments;

internal enum SegmentLaneAppendResult
{
    Accepted = 0,
    SequenceExhausted = 1,
}

internal sealed class ReusableSegmentBlockLane<TRecord> where TRecord : struct
{
    private readonly ReusableSegmentBlock<TRecord>[] _blocks;
    private int _producerIndex;
    private int _writerIndex;
    private bool _producerOwnsBlock = true;
    private long _nextRecordSequence = 1;
    private long _nextBlockOrdinal;

    internal ReusableSegmentBlockLane(int blockCount, int recordsPerBlock)
    {
        if (blockCount < 3) throw new ArgumentOutOfRangeException(nameof(blockCount));
        if (recordsPerBlock <= 0) throw new ArgumentOutOfRangeException(nameof(recordsPerBlock));
        _blocks = new ReusableSegmentBlock<TRecord>[blockCount];
        for (var index = 0; index < blockCount; index++)
            _blocks[index] = new ReusableSegmentBlock<TRecord>(recordsPerBlock);
        _blocks[0].State = ReusableSegmentBlock<TRecord>.ProducerOwned;
    }

    internal long NextRecordSequence => _nextRecordSequence;
    internal bool HasProducerRecords =>
        _producerOwnsBlock && _blocks[_producerIndex].Count != 0;

    internal SegmentLaneAppendResult Append(
        in TRecord record,
        out int preparedRecordCount)
    {
        preparedRecordCount = 0;
        if (_nextRecordSequence == long.MaxValue)
        {
            var sealResult = PreparePartialBlock(out preparedRecordCount);
            return sealResult == SegmentLaneAppendResult.Accepted
                ? SegmentLaneAppendResult.SequenceExhausted
                : sealResult;
        }
        if (_nextBlockOrdinal == long.MaxValue)
            return SegmentLaneAppendResult.SequenceExhausted;
        if (!_producerOwnsBlock)
            throw new InvalidOperationException("The producer has no writable segment block.");

        var block = _blocks[_producerIndex];
        if (block.Count == 0) block.FirstRecordSequence = _nextRecordSequence;
        block.Records[block.Count++] = record;
        _nextRecordSequence++;
        if (block.Count != block.Records.Length) return SegmentLaneAppendResult.Accepted;

        preparedRecordCount = Prepare(block);
        return SegmentLaneAppendResult.Accepted;
    }

    internal SegmentLaneAppendResult PreparePartialBlock(out int preparedRecordCount)
    {
        preparedRecordCount = 0;
        if (!_producerOwnsBlock) return SegmentLaneAppendResult.Accepted;
        var block = _blocks[_producerIndex];
        if (block.Count == 0)
        {
            _producerOwnsBlock = false;
            Volatile.Write(ref block.State, ReusableSegmentBlock<TRecord>.Free);
            return SegmentLaneAppendResult.Accepted;
        }
        if (_nextBlockOrdinal == long.MaxValue)
            return SegmentLaneAppendResult.SequenceExhausted;
        preparedRecordCount = Prepare(block);
        return SegmentLaneAppendResult.Accepted;
    }

    internal bool PublishPreparedBlock(bool claimNextProducerBlock)
    {
        if (_producerOwnsBlock)
            throw new InvalidOperationException("No prepared segment block is available for publication.");
        var block = _blocks[_producerIndex];
        if (Volatile.Read(ref block.State) != ReusableSegmentBlock<TRecord>.ProducerOwned || block.Count == 0)
            throw new InvalidOperationException("The prepared segment block is not producer-owned.");
        Volatile.Write(ref block.State, ReusableSegmentBlock<TRecord>.Ready);
        _producerIndex = Next(_producerIndex);
        return !claimNextProducerBlock || TryClaimNextProducerBlock();
    }

    internal bool TryTakeNextReady(out ReusableSegmentBlock<TRecord>? block)
    {
        var candidate = _blocks[_writerIndex];
        if (Interlocked.CompareExchange(
                ref candidate.State,
                ReusableSegmentBlock<TRecord>.WriterOwned,
                ReusableSegmentBlock<TRecord>.Ready) != ReusableSegmentBlock<TRecord>.Ready)
        {
            block = null;
            return false;
        }
        block = candidate;
        return true;
    }

    internal void ReleaseWritten(ReusableSegmentBlock<TRecord> block)
    {
        if (!ReferenceEquals(block, _blocks[_writerIndex]) ||
            Volatile.Read(ref block.State) != ReusableSegmentBlock<TRecord>.WriterOwned)
            throw new InvalidOperationException("The writer does not own the expected segment block.");
        Clear(block);
        Volatile.Write(ref block.State, ReusableSegmentBlock<TRecord>.Free);
        _writerIndex = Next(_writerIndex);
    }

    internal void DiscardAll(
        out int discardedBlocks,
        out int discardedPendingBlocks,
        out long discardedRecords)
    {
        discardedBlocks = 0;
        discardedPendingBlocks = 0;
        discardedRecords = 0;
        for (var index = 0; index < _blocks.Length; index++)
        {
            var block = _blocks[index];
            var state = Volatile.Read(ref block.State);
            if (state == ReusableSegmentBlock<TRecord>.Free) continue;
            if (block.Count > 0)
            {
                discardedBlocks++;
                discardedRecords += block.Count;
            }
            if (state is ReusableSegmentBlock<TRecord>.Ready or
                ReusableSegmentBlock<TRecord>.WriterOwned)
                discardedPendingBlocks++;
            Clear(block);
            Volatile.Write(ref block.State, ReusableSegmentBlock<TRecord>.Free);
        }
        _producerOwnsBlock = false;
    }

    private bool TryClaimNextProducerBlock()
    {
        var block = _blocks[_producerIndex];
        if (Interlocked.CompareExchange(
                ref block.State,
                ReusableSegmentBlock<TRecord>.ProducerOwned,
                ReusableSegmentBlock<TRecord>.Free) != ReusableSegmentBlock<TRecord>.Free)
            return false;
        _producerOwnsBlock = true;
        return true;
    }

    private int Prepare(ReusableSegmentBlock<TRecord> block)
    {
        block.Ordinal = _nextBlockOrdinal++;
        var count = block.Count;
        _producerOwnsBlock = false;
        return count;
    }

    private int Next(int index) => index + 1 == _blocks.Length ? 0 : index + 1;

    private static void Clear(ReusableSegmentBlock<TRecord> block)
    {
        Array.Clear(block.Records, 0, block.Count);
        block.Count = 0;
        block.Ordinal = 0;
        block.FirstRecordSequence = 0;
    }
}

internal sealed class ReusableSegmentBlock<TRecord> where TRecord : struct
{
    internal const int Free = 0;
    internal const int ProducerOwned = 1;
    internal const int Ready = 2;
    internal const int WriterOwned = 3;

    internal ReusableSegmentBlock(int recordCapacity) =>
        Records = new TRecord[recordCapacity];

    internal readonly TRecord[] Records;
    internal int State;
    internal int Count;
    internal long Ordinal;
    internal long FirstRecordSequence;
}
