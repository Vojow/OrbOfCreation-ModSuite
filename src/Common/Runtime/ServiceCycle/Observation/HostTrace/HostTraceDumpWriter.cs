using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;

internal readonly struct HostTraceDumpOutcome
{
    internal HostTraceDumpOutcome(long writtenEvents, long bytesWritten, ulong overwrittenEvents)
    {
        WrittenEvents = writtenEvents;
        BytesWritten = bytesWritten;
        OverwrittenEvents = overwrittenEvents;
    }

    internal long WrittenEvents { get; }
    internal long BytesWritten { get; }
    internal ulong OverwrittenEvents { get; }
}

/// <summary>
/// Writes whatever the host ring is holding into an ordinary full-trace session artifact.
/// </summary>
/// <remarks>
/// Synchronous and on the main thread, unlike the armed recorder's background writer, because a dump
/// is one bounded user-initiated write rather than a stream: the events are already in hand, and a
/// second writer thread would only add a lifecycle to get wrong. The artifact is the same OSCS/OSCM
/// pair an armed session produces, so the analysis tool reads a dump without knowing it is one.
/// </remarks>
internal static class HostTraceDumpWriter
{
    internal static HostTraceDumpOutcome Write(
        ServiceCycleSemanticTraceSource source,
        FullTraceSessionId session,
        ISegmentSessionStorage storage,
        int serviceCapacity,
        ServiceCycleTraceRoster? roster = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (storage is null) throw new ArgumentNullException(nameof(storage));
        if (!session.IsValid) throw new ArgumentException("A valid full-trace session is required.", nameof(session));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));

        var held = source.Count;
        var overwritten = source.OverwrittenTotal;
        if (held == 0) return new HostTraceDumpOutcome(0, 0, overwritten);

        var terminalRequest = new FullTraceTerminalRequest();
        terminalRequest.Set(FullTraceTerminalReason.UserStopped);
        var consumer = new FullTraceSegmentConsumer(
            storage,
            terminalRequest,
            session,
            source.Session,
            serviceCapacity,
            firstSemanticSequence: source.Cursor.Sequence - (ulong)held + 1);
        consumer.Initialize();

        // A dump is the same artifact an armed session writes, so it carries the same roster: the
        // reader of a bug report has no more idea what service 2 was than the reader of a capture.
        TraceRosterWriter.TryWrite(storage, roster);

        // Full segments except the last: the segment writer accepts one partial segment and it must
        // be the final one, so the drain buffer is exactly a segment rather than the whole ring.
        var buffer = new ServiceCycleSemanticEvent[FullTraceSegmentCodec.MaximumRecords];
        var cursor = default(ServiceCycleTraceCursor);
        var blockOrdinal = 0L;
        var transportSequence = 1L;
        var written = 0L;
        var bytes = 0L;
        while (true)
        {
            var drain = source.DrainSince(cursor, buffer);
            if (drain.Copied == 0) break;
            cursor = drain.Cursor;
            bytes += consumer.Write(
                blockOrdinal,
                transportSequence,
                new ReadOnlySpan<ServiceCycleSemanticEvent>(buffer, 0, drain.Copied));
            blockOrdinal++;
            transportSequence = checked(transportSequence + drain.Copied);
            written = checked(written + drain.Copied);
            if (!drain.HasMore) break;
        }

        consumer.Complete(new BufferedSegmentCompletion(
            complete: true,
            BufferedSegmentFaultReason.None,
            acceptedRecords: written,
            writtenRecords: written,
            firstIncompleteSequence: 0));
        return new HostTraceDumpOutcome(
            written,
            checked(bytes + FullTraceManifestCodec.ManifestBytes),
            overwritten);
    }
}
