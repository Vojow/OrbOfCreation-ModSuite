using System;
using System.Collections.Generic;
using System.Text;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.HostTrace;

public sealed class HostTraceSnapshotWriterTests
{
    private static readonly FullTraceSessionId DumpSession = new(0xD0_00_00_01);
    private static readonly ServiceCycleTraceSessionId SemanticSession = new(0x5E_00_00_01);

    [Fact]
    public void AWrappedRingBecomesAReadableSessionStartingAtTheOldestEventItStillHolds()
    {
        var capacity = FullTraceSegmentCodec.MaximumRecords + 200;
        var source = Ring(capacity, fill: true);
        var storage = new MemorySessionStorage();

        var outcome = HostTraceSnapshotWriter.Write(source, DumpSession, storage, serviceCapacity: 1);

        var manifest = FullTraceManifestCodec.Decode(Assert.IsType<byte[]>(storage.Manifest));
        var oldest = source.Cursor.Sequence - (ulong)capacity + 1;
        Assert.Equal(FullTraceCompleteness.Complete, manifest.Completeness);
        Assert.Equal(FullTraceTerminalReason.UserStopped, manifest.Reason);
        Assert.Equal(SemanticSession, manifest.SemanticSession);
        Assert.Equal((ulong)capacity, manifest.WrittenRecords);
        Assert.Equal(oldest, manifest.FirstSemanticSequence);
        Assert.Equal(capacity, outcome.WrittenEvents);
        Assert.NotEqual(0UL, outcome.OverwrittenEvents);

        // Two segments, because a segment is capped and only the last one may be short.
        Assert.Equal(2, storage.Segments.Count);
        Assert.Equal(2UL, manifest.SegmentCount);
        var sequences = new List<ulong>();
        foreach (var segment in storage.Segments)
            foreach (var recorded in FullTraceSegmentCodec.Decode(segment).Events)
                sequences.Add(recorded.Id.Sequence);
        Assert.Equal(capacity, sequences.Count);
        for (var index = 0; index < sequences.Count; index++)
            Assert.Equal(oldest + (ulong)index, sequences[index]);
    }

    [Fact]
    public void AnEmptyRingWritesNothingRatherThanAnEmptySession()
    {
        var source = Ring(capacity: 64, fill: false);
        var storage = new MemorySessionStorage();

        var outcome = HostTraceSnapshotWriter.Write(source, DumpSession, storage, serviceCapacity: 1);

        Assert.Equal(0, outcome.WrittenEvents);
        Assert.Equal(0, outcome.BytesWritten);
        Assert.Empty(storage.Segments);
        Assert.Null(storage.Manifest);
    }

    /// <summary>
    /// A dump is the artifact a user attaches to a bug report, so it has to answer "what is service 2"
    /// on its own. The roster lands before the manifest, because the manifest seals the session.
    /// </summary>
    [Fact]
    public void ASnapshotCarriesTheNamesOfTheServicesItRecorded()
    {
        var source = Ring(capacity: 64, fill: true);
        var storage = new MemorySessionStorage();
        var roster = new ServiceCycleTraceRoster(new[]
        {
            new ServiceCycleTraceRosterEntry(ServiceCycleTraceRoster.ServiceKind, 1, "orbautomata.auto-harvest", "Auto Harvest"),
        });

        HostTraceSnapshotWriter.Write(source, DumpSession, storage, serviceCapacity: 1, roster);

        Assert.True(storage.SideArtifacts.TryGetValue(TraceRosterFormat.FileName, out var written));
        var decoded = TraceRosterFormat.Decode(Encoding.UTF8.GetString(written!));
        Assert.Equal(1, decoded.Count);
        Assert.Equal("Auto Harvest", decoded[0].DisplayName);
    }

    [Fact]
    public void ASnapshotWithNoRosterIsStillAReadableSession()
    {
        var source = Ring(capacity: 64, fill: true);
        var storage = new MemorySessionStorage();

        var outcome = HostTraceSnapshotWriter.Write(source, DumpSession, storage, serviceCapacity: 1);

        Assert.Equal(64, outcome.WrittenEvents);
        Assert.Empty(storage.SideArtifacts);
        Assert.NotNull(storage.Manifest);
    }

    /// <summary>
    /// A ring holding <paramref name="capacity"/> events and having already dropped older ones, which
    /// is the state a long-running suite is always in by the time a user asks for a dump.
    /// </summary>
    private static ServiceCycleSemanticTraceSource Ring(int capacity, bool fill)
    {
        var recorder = new ServiceCycleSemanticRecorder(
            SemanticSession,
            eventCapacity: capacity,
            serviceCapacity: 1);
        var trace = new ServiceCycleSemanticRuntimeTrace(recorder, 1);
        if (!fill) return trace.Source;

        trace.Bind(
            0,
            new ServiceId("orbmodding.host-trace-dump-test"),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new LifecycleGeneration(1),
            lifecycleSemanticVersion: 1,
            new MonotonicTimestamp(1));
        var generation = 2UL;
        var limit = (ulong)capacity * 4 + 64;
        var overshoot = 0;
        while (generation < limit && (trace.Source.Count < capacity || overshoot < 8))
        {
            if (trace.Source.Count == capacity) overshoot++;
            trace.LifecycleRequested(
                0,
                new LifecycleGeneration(generation),
                new MonotonicTimestamp((long)generation));
            generation++;
        }
        Assert.Equal(capacity, trace.Source.Count);
        Assert.NotEqual(0UL, trace.Source.OverwrittenTotal);
        return trace.Source;
    }

    private sealed class MemorySessionStorage : ISegmentSessionStorage, ISessionSideArtifactSink
    {
        internal List<byte[]> Segments { get; } = new();
        internal byte[]? Manifest { get; private set; }
        internal Dictionary<string, byte[]> SideArtifacts { get; } = new(StringComparer.Ordinal);

        public void Initialize() { }

        public void CommitSegment(long ordinal, ReadOnlySpan<byte> bytes)
        {
            Assert.Equal(Segments.Count, ordinal);
            Segments.Add(bytes.ToArray());
        }

        public void CommitManifest(ReadOnlySpan<byte> bytes) => Manifest = bytes.ToArray();

        public void CommitSideArtifact(string name, ReadOnlySpan<byte> bytes)
        {
            Assert.Null(Manifest);
            SideArtifacts[name] = bytes.ToArray();
        }
    }
}
