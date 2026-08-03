using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;

internal sealed class HostTraceSnapshot
{
    internal HostTraceSnapshot(
        long writtenEvents,
        long bytesWritten,
        ulong overwrittenEvents,
        IReadOnlyList<HostTraceSnapshotMember> members)
    {
        WrittenEvents = writtenEvents;
        BytesWritten = bytesWritten;
        OverwrittenEvents = overwrittenEvents;
        Members = members ?? throw new ArgumentNullException(nameof(members));
    }

    internal long WrittenEvents { get; }
    internal long BytesWritten { get; }
    internal ulong OverwrittenEvents { get; }
    internal IReadOnlyList<HostTraceSnapshotMember> Members { get; }
}

internal readonly struct HostTraceSnapshotMember
{
    internal HostTraceSnapshotMember(string name, byte[] bytes, bool isText)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        IsText = isText;
    }

    internal string Name { get; }
    internal byte[] Bytes { get; }
    internal bool IsText { get; }
}

/// <summary>Materializes the host's bounded recent-event ring without creating another disk artifact.</summary>
internal static class HostTraceSnapshotWriter
{
    internal static HostTraceSnapshot Capture(
        ServiceCycleSemanticTraceSource source,
        FullTraceSessionId session,
        int serviceCapacity,
        ServiceCycleTraceRoster? roster = null)
    {
        var storage = new SnapshotStorage();
        var outcome = Write(source, session, storage, serviceCapacity, roster);
        return new HostTraceSnapshot(
            outcome.WrittenEvents,
            outcome.BytesWritten,
            outcome.OverwrittenEvents,
            storage.Members);
    }

    internal static HostTraceSnapshotOutcome Write(
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
        if (held == 0) return new HostTraceSnapshotOutcome(0, 0, overwritten);

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
        TraceRosterWriter.TryWrite(storage, roster);

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
        return new HostTraceSnapshotOutcome(
            written,
            checked(bytes + FullTraceManifestCodec.ManifestBytes),
            overwritten);
    }

    private sealed class SnapshotStorage : ISegmentSessionStorage, ISessionSideArtifactSink
    {
        private readonly List<HostTraceSnapshotMember> _members = new();

        internal IReadOnlyList<HostTraceSnapshotMember> Members => _members;

        public void Initialize() { }

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes) => _members.Add(
            new HostTraceSnapshotMember(
                "segment-" + ordinal.ToString("D8", CultureInfo.InvariantCulture) + ".oscs",
                bytes.ToArray(),
                isText: false));

        public void CommitManifest(ReadOnlySpan<byte> bytes) => _members.Add(
            new HostTraceSnapshotMember("manifest.oscm", bytes.ToArray(), isText: false));

        public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes) => _members.Add(
            new HostTraceSnapshotMember(name, bytes.ToArray(), isText: true));
    }
}

internal readonly struct HostTraceSnapshotOutcome
{
    internal HostTraceSnapshotOutcome(long writtenEvents, long bytesWritten, ulong overwrittenEvents)
    {
        WrittenEvents = writtenEvents;
        BytesWritten = bytesWritten;
        OverwrittenEvents = overwrittenEvents;
    }

    internal long WrittenEvents { get; }
    internal long BytesWritten { get; }
    internal ulong OverwrittenEvents { get; }
}
