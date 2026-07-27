using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace.Format;

public sealed class FullTraceSessionAssemblerTests
{
    [Fact]
    public void DenseSegmentsResolveCrossSegmentParentsWithoutFlatteningTheSession()
    {
        var fixture = TwoSegments(childTimestamp: 100);
        var prior = new PriorEvents(fixture.First.Events);
        var assembler = new FullTraceSessionAssembler(fixture.Session, prior);
        var first = fixture.First;
        var second = fixture.Second;

        assembler.Add(in first, fixture.FirstBytes);
        assembler.Add(in second, fixture.SecondBytes);
        var document = assembler.Complete(fixture.Manifest);

        Assert.Equal(FullTraceSessionState.Complete, document.State);
        Assert.Equal(2UL, document.SegmentCount);
        Assert.Equal((ulong)FullTraceSegmentCodec.MaximumRecords + 1, document.WrittenRecords);
        Assert.Equal(1, prior.Reads);
    }

    [Fact]
    public void CrossSegmentParentCannotOccurAfterItsChild()
    {
        var fixture = TwoSegments(childTimestamp: 99);
        var assembler = new FullTraceSessionAssembler(
            fixture.Session,
            new PriorEvents(fixture.First.Events));
        var first = fixture.First;
        var second = fixture.Second;
        assembler.Add(in first, fixture.FirstBytes);

        Assert.Throws<FormatException>(() => assembler.Add(in second, fixture.SecondBytes));
    }

    [Fact]
    public void OnlyTheFinalSegmentMayBePartial()
    {
        var session = new FullTraceSessionId(80);
        var semantic = new ServiceCycleTraceSessionId(81);
        var first = DecodeSegment(session, semantic, 0, 1, new[] { Event(semantic, 1, 100) });
        var second = DecodeSegment(session, semantic, 1, 2, new[] { Event(semantic, 2, 100) });
        var assembler = new FullTraceSessionAssembler(session, new PriorEvents(first.Events));
        assembler.Add(in first, FullTraceSegmentCodec.GetEncodedLength(1));

        Assert.Throws<FormatException>(() =>
            assembler.Add(in second, FullTraceSegmentCodec.GetEncodedLength(1)));
    }

    [Fact]
    public void ParentBeforeTheCapturedWindowRemainsExternalAncestry()
    {
        var session = new FullTraceSessionId(82);
        var semantic = new ServiceCycleTraceSessionId(83);
        var item = Event(semantic, 10, 100, parentSequence: 9);
        var segment = DecodeSegment(session, semantic, 0, 1, new[] { item });
        var prior = new PriorEvents(Array.Empty<ServiceCycleSemanticEvent>());
        var assembler = new FullTraceSessionAssembler(session, prior);

        assembler.Add(in segment, FullTraceSegmentCodec.GetEncodedLength(1));
        var document = assembler.Complete(manifest: null);

        Assert.Equal(FullTraceSessionState.Interrupted, document.State);
        Assert.Equal(10UL, document.FirstSemanticSequence);
        Assert.Equal(0, prior.Reads);
    }

    private static TwoSegmentFixture TwoSegments(long childTimestamp)
    {
        var session = new FullTraceSessionId(70);
        var semantic = new ServiceCycleTraceSessionId(71);
        var firstEvents = new ServiceCycleSemanticEvent[FullTraceSegmentCodec.MaximumRecords];
        for (var index = 0; index < firstEvents.Length; index++)
            firstEvents[index] = Event(semantic, (ulong)index + 1, 100);
        var child = Event(
            semantic,
            (ulong)FullTraceSegmentCodec.MaximumRecords + 1,
            childTimestamp,
            parentSequence: 1);
        var first = DecodeSegment(session, semantic, 0, 1, firstEvents);
        var second = DecodeSegment(
            session,
            semantic,
            1,
            (ulong)FullTraceSegmentCodec.MaximumRecords + 1,
            new[] { child });
        var firstBytes = FullTraceSegmentCodec.GetEncodedLength(firstEvents.Length);
        var secondBytes = FullTraceSegmentCodec.GetEncodedLength(1);
        var written = (ulong)firstEvents.Length + 1;
        var manifest = new FullTraceManifestDocument(
            FullTraceCompleteness.Complete,
            FullTraceTerminalReason.UserStopped,
            session,
            semantic,
            7,
            2,
            1,
            written,
            written,
            0,
            0,
            100,
            childTimestamp,
            (ulong)(firstBytes + secondBytes));
        return new TwoSegmentFixture(session, first, second, firstBytes, secondBytes, manifest);
    }

    private static FullTraceSegmentDocument DecodeSegment(
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semantic,
        ulong ordinal,
        ulong firstTransportSequence,
        ServiceCycleSemanticEvent[] events)
    {
        var bytes = new byte[FullTraceSegmentCodec.GetEncodedLength(events.Length)];
        FullTraceSegmentCodec.Encode(session, semantic, ordinal, firstTransportSequence, 7, events, bytes);
        return FullTraceSegmentCodec.Decode(bytes);
    }

    private static ServiceCycleSemanticEvent Event(
        ServiceCycleTraceSessionId semantic,
        ulong sequence,
        long timestamp,
        ulong parentSequence = 0)
    {
        var payload = ServiceCycleSemanticPayload.Publication(false, 1, timestamp);
        return new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(semantic, sequence),
            parentSequence == 0 ? default : new ServiceCycleTraceEventId(semantic, parentSequence),
            ServiceCycleSemanticEventKind.ConfigurationPublished,
            in payload);
    }

    private sealed class PriorEvents : IFullTracePriorEventReader
    {
        private readonly ServiceCycleSemanticEvent[] _events;

        internal PriorEvents(ServiceCycleSemanticEvent[] events) => _events = events;
        internal int Reads { get; private set; }

        public ServiceCycleSemanticEvent ReadEvent(ulong segmentOrdinal, int eventIndex)
        {
            Assert.Equal(0UL, segmentOrdinal);
            Reads++;
            return _events[eventIndex];
        }
    }

    private readonly record struct TwoSegmentFixture(
        FullTraceSessionId Session,
        FullTraceSegmentDocument First,
        FullTraceSegmentDocument Second,
        int FirstBytes,
        int SecondBytes,
        FullTraceManifestDocument Manifest);
}
