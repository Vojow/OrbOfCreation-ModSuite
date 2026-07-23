using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

internal sealed class FullTraceSessionAssembler
{
    private readonly FullTraceSessionId _expectedSession;
    private readonly IFullTracePriorEventReader _priorEvents;
    private ServiceCycleTraceSessionId _semanticSession;
    private int _serviceCapacity;
    private ulong _segmentCount;
    private ulong _firstSemanticSequence;
    private ulong _writtenRecords;
    private ulong _segmentBytes;
    private long _firstTimestampTicks;
    private long _lastTimestampTicks;
    private bool _partialSegmentSeen;

    internal FullTraceSessionAssembler(
        FullTraceSessionId expectedSession,
        IFullTracePriorEventReader priorEvents)
    {
        if (!expectedSession.IsValid)
            throw new ArgumentException("A valid full-trace session is required.", nameof(expectedSession));
        _expectedSession = expectedSession;
        _priorEvents = priorEvents ?? throw new ArgumentNullException(nameof(priorEvents));
    }

    internal void Add(in FullTraceSegmentDocument segment, int encodedBytes)
    {
        try
        {
            AddCore(in segment, encodedBytes);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    internal FullTraceSessionDocument Complete(FullTraceManifestDocument? manifest)
    {
        try
        {
            return CompleteCore(manifest);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    private void AddCore(in FullTraceSegmentDocument segment, int encodedBytes)
    {
        if (encodedBytes != FullTraceSegmentCodec.GetEncodedLength(segment.Events.Length) ||
            segment.Session != _expectedSession || segment.Ordinal != _segmentCount ||
            segment.FirstTransportSequence != checked(_writtenRecords + 1) || _partialSegmentSeen)
            throw Invalid();

        if (_segmentCount == 0)
        {
            _semanticSession = segment.SemanticSession;
            _serviceCapacity = segment.ServiceCapacity;
            _firstSemanticSequence = segment.Events[0].Id.Sequence;
            _firstTimestampTicks = segment.Events[0].Payload.TimestampTicks;
        }
        else if (segment.SemanticSession != _semanticSession ||
            segment.ServiceCapacity != _serviceCapacity ||
            segment.Events[0].Id.Sequence != checked(_firstSemanticSequence + _writtenRecords))
        {
            throw Invalid();
        }

        ValidateEarlierParents(in segment);
        _partialSegmentSeen = segment.Events.Length != FullTraceSegmentCodec.MaximumRecords;
        _lastTimestampTicks = segment.Events[^1].Payload.TimestampTicks;
        _segmentCount = checked(_segmentCount + 1);
        _writtenRecords = checked(_writtenRecords + (ulong)segment.Events.Length);
        _segmentBytes = checked(_segmentBytes + (ulong)encodedBytes);
    }

    private FullTraceSessionDocument CompleteCore(FullTraceManifestDocument? manifest)
    {
        if (manifest is not { } terminal)
        {
            return new FullTraceSessionDocument(
                FullTraceSessionState.Interrupted,
                _expectedSession,
                _semanticSession,
                _serviceCapacity,
                _segmentCount,
                _firstSemanticSequence,
                _writtenRecords,
                _writtenRecords,
                checked(_writtenRecords + 1),
                _firstSemanticSequence == 0 ? 0 : checked(_firstSemanticSequence + _writtenRecords),
                _firstTimestampTicks,
                _lastTimestampTicks,
                _segmentBytes,
                null);
        }

        if (terminal.Session != _expectedSession || terminal.SegmentCount != _segmentCount ||
            terminal.WrittenRecords != _writtenRecords || terminal.SegmentBytes != _segmentBytes)
            throw Invalid();

        if (_segmentCount == 0)
        {
            _semanticSession = terminal.SemanticSession;
            _serviceCapacity = terminal.ServiceCapacity;
            _firstSemanticSequence = terminal.FirstSemanticSequence;
        }
        else if (terminal.SemanticSession != _semanticSession ||
            terminal.ServiceCapacity != _serviceCapacity ||
            terminal.FirstSemanticSequence != _firstSemanticSequence ||
            terminal.FirstTimestampTicks != _firstTimestampTicks ||
            terminal.LastTimestampTicks != _lastTimestampTicks)
        {
            throw Invalid();
        }

        return new FullTraceSessionDocument(
            terminal.Completeness == FullTraceCompleteness.Complete
                ? FullTraceSessionState.Complete
                : FullTraceSessionState.Incomplete,
            terminal.Session,
            terminal.SemanticSession,
            terminal.ServiceCapacity,
            terminal.SegmentCount,
            terminal.FirstSemanticSequence,
            terminal.AcceptedRecords,
            terminal.WrittenRecords,
            terminal.FirstIncompleteTransportSequence,
            terminal.FirstIncompleteSemanticSequence,
            terminal.FirstTimestampTicks,
            terminal.LastTimestampTicks,
            terminal.SegmentBytes,
            terminal.Reason);
    }

    private void ValidateEarlierParents(in FullTraceSegmentDocument segment)
    {
        var segmentFirst = segment.Events[0].Id.Sequence;
        for (var index = 0; index < segment.Events.Length; index++)
        {
            var item = segment.Events[index];
            if (!item.HasParent || item.Parent.Sequence < _firstSemanticSequence ||
                item.Parent.Sequence >= segmentFirst)
                continue;

            var offset = item.Parent.Sequence - _firstSemanticSequence;
            var ordinal = offset / FullTraceSegmentCodec.MaximumRecords;
            var eventIndex = checked((int)(offset % FullTraceSegmentCodec.MaximumRecords));
            var parent = _priorEvents.ReadEvent(ordinal, eventIndex);
            if (parent.Id != item.Parent || parent.Payload.TimestampTicks > item.Payload.TimestampTicks)
                throw Invalid();
        }
    }

    private static FormatException Invalid() => new("Invalid manual full-trace session.");
}
