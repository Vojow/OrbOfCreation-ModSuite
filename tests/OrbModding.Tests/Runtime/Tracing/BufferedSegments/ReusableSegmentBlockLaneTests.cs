using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using Xunit;

namespace OrbModding.Tests.Runtime.Tracing.BufferedSegments;

public sealed class ReusableSegmentBlockLaneTests
{
    [Fact]
    public void FixedCircularLanePreservesSequenceAndPayloadAcrossReuse()
    {
        var lane = new ReusableSegmentBlockLane<int>(3, 2);

        for (var segment = 0; segment < 12; segment++)
        {
            var first = segment * 2 + 1;
            Assert.Equal(SegmentLaneAppendResult.Accepted, lane.Append(first, out var preparedCount));
            Assert.Equal(0, preparedCount);
            Assert.Equal(SegmentLaneAppendResult.Accepted, lane.Append(first + 1, out preparedCount));
            Assert.Equal(2, preparedCount);
            Assert.True(lane.PublishPreparedBlock(claimNextProducerBlock: true));

            Assert.True(lane.TryTakeNextReady(out var candidate));
            var block = Assert.IsType<ReusableSegmentBlock<int>>(candidate);
            Assert.Equal(segment, block.Ordinal);
            Assert.Equal(first, block.FirstRecordSequence);
            Assert.Equal(new[] { first, first + 1 }, block.Records[..block.Count]);
            lane.ReleaseWritten(block);
        }

        Assert.Equal(25, lane.NextRecordSequence);
        Assert.False(lane.TryTakeNextReady(out _));
    }

    [Fact]
    public void PublishingTheLastEmptyBlockReportsExhaustionAtTheNextSequence()
    {
        var lane = new ReusableSegmentBlockLane<int>(3, 1);

        Assert.Equal(SegmentLaneAppendResult.Accepted, lane.Append(1, out _));
        Assert.True(lane.PublishPreparedBlock(claimNextProducerBlock: true));
        Assert.Equal(SegmentLaneAppendResult.Accepted, lane.Append(2, out _));
        Assert.True(lane.PublishPreparedBlock(claimNextProducerBlock: true));
        Assert.Equal(SegmentLaneAppendResult.Accepted, lane.Append(3, out _));

        Assert.False(lane.PublishPreparedBlock(claimNextProducerBlock: true));
        Assert.Equal(4, lane.NextRecordSequence);
    }

    [Fact]
    public void PreparedPayloadIsInvisibleUntilAccountingCanPrecedePublication()
    {
        var lane = new ReusableSegmentBlockLane<int>(3, 1);
        Assert.Equal(SegmentLaneAppendResult.Accepted, lane.Append(1, out var preparedCount));
        Assert.Equal(1, preparedCount);

        Assert.False(lane.TryTakeNextReady(out _));

        Assert.True(lane.PublishPreparedBlock(claimNextProducerBlock: true));
        Assert.True(lane.TryTakeNextReady(out var candidate));
        Assert.Equal(1, Assert.IsType<ReusableSegmentBlock<int>>(candidate).Records[0]);
    }

    [Fact]
    public void DiscardDistinguishesPartialProducerDataFromPendingSealedBlocks()
    {
        var lane = new ReusableSegmentBlockLane<int>(3, 2);
        Assert.Equal(SegmentLaneAppendResult.Accepted, lane.Append(1, out _));

        lane.DiscardAll(
            out var discardedBlocks,
            out var discardedPendingBlocks,
            out var discardedRecords);

        Assert.Equal(1, discardedBlocks);
        Assert.Equal(0, discardedPendingBlocks);
        Assert.Equal(1, discardedRecords);
        Assert.False(lane.TryTakeNextReady(out _));
    }
}
