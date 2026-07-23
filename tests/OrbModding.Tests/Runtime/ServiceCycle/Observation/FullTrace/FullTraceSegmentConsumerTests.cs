using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using OrbModding.Tests.Runtime.ServiceCycle.Tracing;
using Xunit;
using static OrbModding.Tests.Runtime.Tracing.BufferedSegments.BufferedSegmentTestWait;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace;

public sealed class FullTraceSegmentConsumerTests
{
    [Fact]
    public void PartialStopPublishesExactSegmentAndCompleteManifest()
    {
        var semantic = new ServiceCycleTraceSessionId(1_001);
        var storage = new MemorySessionStorage();
        var terminal = new FullTraceTerminalRequest();
        var consumer = CreateConsumer(storage, terminal, semantic);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(Event(semantic, 1)));
        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(Event(semantic, 2, 1)));
        terminal.Set(FullTraceTerminalReason.UserStopped);
        sink.Stop();
        ForStatus(sink, BufferedSegmentStatus.Stopped);

        var segment = FullTraceSegmentCodec.Decode(Assert.Single(storage.Segments));
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(new ulong[] { 1, 2 }, Array.ConvertAll(segment.Events, item => item.Id.Sequence));
        Assert.Equal(FullTraceCompleteness.Complete, manifest.Completeness);
        Assert.Equal(FullTraceTerminalReason.UserStopped, manifest.Reason);
        Assert.Equal(2UL, manifest.WrittenRecords);
        Assert.True(consumer.ManifestCommitted);
    }

    [Fact]
    public void StorageFailurePublishesIncompleteManifestForTheDurablePrefix()
    {
        var semantic = new ServiceCycleTraceSessionId(1_101);
        var storage = new MemorySessionStorage { FailSegmentWrite = true };
        var terminal = new FullTraceTerminalRequest();
        var consumer = CreateConsumer(storage, terminal, semantic);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(Event(semantic, 1)));
        terminal.Set(FullTraceTerminalReason.UserStopped);
        sink.Stop();
        ForStatus(sink, BufferedSegmentStatus.Faulted);

        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(FullTraceCompleteness.Incomplete, manifest.Completeness);
        Assert.Equal(FullTraceTerminalReason.WriteFailed, manifest.Reason);
        Assert.Equal(1UL, manifest.AcceptedRecords);
        Assert.Equal(0UL, manifest.WrittenRecords);
        Assert.Equal(1UL, manifest.FirstIncompleteTransportSequence);
    }

    [Fact]
    public void ProducerFailureDrainsAcceptedRecordsAndMarksTheNextRecordMissing()
    {
        var semantic = new ServiceCycleTraceSessionId(1_201);
        var storage = new MemorySessionStorage();
        var terminal = new FullTraceTerminalRequest();
        var consumer = CreateConsumer(storage, terminal, semantic);
        using var sink = CreateSink(consumer);
        ForStatus(sink, BufferedSegmentStatus.Running);

        Assert.Equal(BufferedSegmentAppendResult.Accepted, sink.Append(Event(semantic, 1)));
        sink.FailProducer();
        ForStatus(sink, BufferedSegmentStatus.Faulted);

        Assert.Single(FullTraceSegmentCodec.Decode(Assert.Single(storage.Segments)).Events);
        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        Assert.Equal(FullTraceTerminalReason.SemanticFault, manifest.Reason);
        Assert.Equal(1UL, manifest.WrittenRecords);
        Assert.Equal(2UL, manifest.FirstIncompleteTransportSequence);
        Assert.Equal(2UL, manifest.FirstIncompleteSemanticSequence);
    }

    private static FullTraceSegmentConsumer CreateConsumer(
        MemorySessionStorage storage,
        FullTraceTerminalRequest terminal,
        ServiceCycleTraceSessionId semantic) => new(
            storage,
            terminal,
            new FullTraceSessionId(500),
            semantic,
            serviceCapacity: 7);

    private static BufferedSegmentSink<ServiceCycleSemanticEvent> CreateSink(
        FullTraceSegmentConsumer consumer) => new(
            consumer,
            new BufferedSegmentOptions(
                blockCount: 3,
                recordsPerBlock: FullTraceSegmentCodec.MaximumRecords,
                workerName: "Full trace consumer test"));

    private static ServiceCycleSemanticEvent Event(
        ServiceCycleTraceSessionId semantic,
        ulong sequence,
        ulong parent = 0) => ServiceCycleTraceFixtures.Event(
            sequence,
            parentSequence: parent,
            eventSession: semantic);

    private sealed class MemorySessionStorage : ISegmentSessionStorage
    {
        internal List<byte[]> Segments { get; } = new();
        internal byte[]? Manifest { get; private set; }
        internal bool FailSegmentWrite { get; init; }

        public void Initialize() { }

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
        {
            if (FailSegmentWrite) throw new InvalidOperationException("Injected segment failure.");
            Assert.Equal(Segments.Count, ordinal);
            Segments.Add(bytes.ToArray());
        }

        public void CommitManifest(ReadOnlySpan<byte> bytes) => Manifest = bytes.ToArray();
    }
}
