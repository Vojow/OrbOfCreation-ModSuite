using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplayArtifactExporterTests
{
    [Fact]
    public void RegisteredServiceWithoutCycleExportsEmptySnapshotsWithoutFaulting()
    {
        var traceSession = new ServiceCycleTraceSessionId(902);
        var session = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 16, 8, 2));
        var descriptor = new ServiceCycleReplayCodecDescriptor(1, 8);
        session.BindCodecManifest(1, new object(), descriptor, descriptor, descriptor);
        var recorder = new ServiceCycleSemanticRecorder(traceSession, 8, 1);
        recorder.RegisterService(0, new ServiceId("test.empty-replay-format"));
        var storage = new MemoryStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            new ServiceCycleSemanticTraceSource(recorder),
            session,
            storage,
            new ServiceCycleReplayExportOptions(true, 2));
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 1,
            TimeSpan.FromSeconds(2)));
        var first = ServiceCycleReplayArtifactCodec.Decode(storage.Latest);
        Assert.True(first.IsComplete);
        Assert.Equal(0, first.CycleCount);
        Assert.Equal(ServiceCycleReplayExportStatus.Running, exporter.Metrics().Status);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 2,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(ServiceCycleReplayExportStatus.Running, exporter.Metrics().Status);
    }

    [Fact]
    public void TwoSlotExporterWritesDecodableArtifactOnBackgroundStorage()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var recorder = Recorder();
        var source = new ServiceCycleSemanticTraceSource(recorder);
        var storage = new MemoryStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            source,
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());

        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 1,
            TimeSpan.FromSeconds(2)));
        var artifact = ServiceCycleReplayArtifactCodec.Decode(storage.Latest);
        Assert.True(artifact.IsComplete);
        Assert.Equal(1, artifact.CycleCount);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, storage.ThreadIds);
    }

    [Fact]
    public void FrozenSnapshotCopyHonorsThePerRequestEventLimit()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var recorder = Recorder();
        var source = new ServiceCycleSemanticTraceSource(recorder);
        var storage = new MemoryStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            source,
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));
        var expectedEvents = new ServiceCycleSemanticEvent[source.Capacity];
        var expectedDrain = source.DrainSince(default, expectedEvents);

        ServiceCycleReplayExportRequestResult result;
        var maximumCopied = 0;
        do
        {
            result = exporter.ContinueFrozenSnapshot(3, out var copied);
            maximumCopied = Math.Max(maximumCopied, copied);
        }
        while (result == ServiceCycleReplayExportRequestResult.Copying);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, result);
        var metrics = exporter.Metrics();
        Assert.Equal(recorder.Count, metrics.SemanticEventsCopied);
        Assert.Equal(3, metrics.PeakSemanticEventsCopiedPerRequest);
        Assert.Equal(3, maximumCopied);
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 1,
            TimeSpan.FromSeconds(2)));
        var artifact = ServiceCycleReplayArtifactCodec.Decode(storage.Latest);
        Assert.Equal(expectedDrain.Copied, artifact.SemanticTrace.Count);
        for (var index = 0; index < expectedDrain.Copied; index++)
            Assert.Equal(expectedEvents[index], artifact.SemanticTrace[index]);
    }

    [Fact]
    public void StagedSnapshotRejectsASourceThatWasNotActuallyFrozen()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var recorder = Recorder();
        var storage = new MemoryStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            new ServiceCycleSemanticTraceSource(recorder),
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(
            ServiceCycleReplayExportRequestResult.Copying,
            exporter.ContinueFrozenSnapshot(3, out _));

        recorder.ConfigurationPublished(0, new ConfigGeneration(99), new MonotonicTimestamp(200));

        Assert.Equal(
            ServiceCycleReplayExportRequestResult.Faulted,
            exporter.ContinueFrozenSnapshot(3, out _));
        Assert.Equal(ServiceCycleReplayExportStatus.Faulted, exporter.Metrics().Status);
    }

    [Fact]
    public void PublicSnapshotRetriesFreshAfterRecordingContention()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var recorder = Recorder();
        var storage = new MemoryStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            new ServiceCycleSemanticTraceSource(recorder),
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));
        var writers = typeof(ServiceCycleReplaySession).GetField(
            "_snapshotWriters",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(nameof(ServiceCycleReplaySession), "_snapshotWriters");
        writers.SetValue(fixture.Session, 1);
        try
        {
            Assert.Equal(
                ServiceCycleReplayExportRequestResult.SnapshotContended,
                exporter.RequestSnapshot());
        }
        finally
        {
            writers.SetValue(fixture.Session, 0);
        }

        recorder.ConfigurationPublished(0, new ConfigGeneration(99), new MonotonicTimestamp(200));

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 1,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(ServiceCycleReplayExportStatus.Running, exporter.Metrics().Status);
        Assert.Equal(1, exporter.Metrics().SnapshotContentionRejections);
    }

    [Fact]
    public void TwoSlotsBoundAdmissionWhileBackgroundStorageIsBlocked()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var source = new ServiceCycleSemanticTraceSource(Recorder());
        using var storage = new BlockingStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            source,
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 2));
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(storage.Entered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.Equal(ServiceCycleReplayExportRequestResult.Backpressured, exporter.RequestSnapshot());

        storage.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 2,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(1, exporter.Metrics().BackpressureRejections);
    }

    [Fact]
    public void StorageFailureDiscardsAcceptedSlotAndLatchesFault()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var source = new ServiceCycleSemanticTraceSource(Recorder());
        var storage = new FailingStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            source,
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());

        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Faulted,
            TimeSpan.FromSeconds(2)));
        var metrics = exporter.Metrics();
        Assert.Equal(1, metrics.FaultCount);
        Assert.Equal(1, metrics.DiscardedArtifacts);
        Assert.Equal(0, metrics.PendingArtifacts);
        Assert.Equal(1, storage.Discarded);
        Assert.Equal(ServiceCycleReplayExportRequestResult.Faulted, exporter.RequestSnapshot());
    }

    [Fact]
    public void FailedStopSignalStillLetsTheWorkerFinishStopping()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        using var storage = new BlockingReconcileStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            new ServiceCycleSemanticTraceSource(Recorder()),
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        Assert.True(storage.Entered.Wait(TimeSpan.FromSeconds(2)));
        var wakeField = typeof(ServiceCycleReplayArtifactExporter).GetField(
            "_wake",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(nameof(ServiceCycleReplayArtifactExporter), "_wake");
        var wake = Assert.IsType<AutoResetEvent>(wakeField.GetValue(exporter));
        wake.Dispose();

        Assert.Throws<ObjectDisposedException>(() => exporter.Stop());
        storage.Release.Set();

        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Stopped,
            TimeSpan.FromSeconds(2)));
    }

    internal static ServiceCycleSemanticRecorder Recorder()
    {
        var recorder = new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(901), 16, 1);
        var service = new ServiceId("test.replay-format");
        recorder.RegisterService(0, service);
        var cycle = new ServiceCycleIdentity(
            service,
            new LifecycleGeneration(2),
            new ConfigGeneration(3),
            new StrategyGeneration(4),
            new CaptureSequence(5),
            new CycleId(6));
        var capture = new ServiceCaptureContext(
            service,
            cycle.Lifecycle,
            cycle.Config,
            cycle.Capture,
            cycle.Cycle,
            new MonotonicTimestamp(90));
        var captured = ServiceCaptureResult.Captured(
            cycle.Strategy,
            CommonServiceDecisionCodes.Captured);
        recorder.ConfigurationPublished(0, cycle.Config, new MonotonicTimestamp(89));
        var start = new ServiceCycleStartContext(
            cycle.Lifecycle, cycle.Config, default, new MonotonicTimestamp(90));
        recorder.StartAttempted(0, in start, new MonotonicTimestamp(90));
        var ready = ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        recorder.StartReady(0, in start, in ready, new MonotonicTimestamp(90), default);
        recorder.CaptureStarted(0, in capture);
        recorder.StrategyPublished(0, cycle.Strategy, new MonotonicTimestamp(90));
        recorder.CaptureCompleted(
            0, in capture, in captured, new MonotonicTimestamp(91), new MonotonicDuration(1));
        recorder.CycleQueued(0, in cycle, in ready, new MonotonicTimestamp(92), default);
        recorder.CycleStarted(0, in cycle, new MonotonicTimestamp(100), default);
        recorder.EvaluationStarted(0, in cycle, new MonotonicTimestamp(100));
        var publication = new ServiceProjectionPublication(
            new ServiceProjectionContext(cycle, new StatePublicationId(1), new MonotonicTimestamp(101)),
            default,
            new ConfigGeneration(3));
        recorder.StatePublished(0, in publication);
        recorder.EvaluationCompleted(
            0, in cycle, 0, WakePolicy.Immediate,
            new MonotonicTimestamp(102), new MonotonicDuration(2));
        recorder.BatchPublished(0, in cycle, new BatchId(1), 0, new MonotonicTimestamp(103));
        var receipt = BatchReceipt.Completed(
            cycle,
            new BatchId(1),
            0,
            default,
            new MonotonicTimestamp(104));
        recorder.BatchTerminal(0, in receipt);
        recorder.CycleCompleted(0, in cycle, new MonotonicTimestamp(104), default);
        return recorder;
    }

    private sealed class MemoryStorage : IRestartAwareTraceSegmentStorage
    {
        private readonly object _gate = new();
        private readonly List<byte[]> _committed = new();
        internal readonly HashSet<int> ThreadIds = new();
        internal byte[] Latest
        {
            get { lock (_gate) return _committed[^1]; }
        }
        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments) => new(0, 0, 0, 0);
        public object BeginSegment(int ordinal)
        {
            lock (_gate) ThreadIds.Add(Environment.CurrentManagedThreadId);
            return new List<byte>();
        }
        public void Append(object segment, ReadOnlySpan<byte> record)
        {
            lock (_gate) ThreadIds.Add(Environment.CurrentManagedThreadId);
            ((List<byte>)segment).AddRange(record.ToArray());
        }
        public void CommitSegment(object segment)
        {
            lock (_gate)
            {
                ThreadIds.Add(Environment.CurrentManagedThreadId);
                _committed.Add(((List<byte>)segment).ToArray());
            }
        }
        public void DiscardSegment(object segment) { }
        public void DeleteOldestCommitted()
        {
            lock (_gate) _committed.RemoveAt(0);
        }
    }

    private sealed class BlockingStorage : IRestartAwareTraceSegmentStorage, IDisposable
    {
        internal readonly ManualResetEventSlim Entered = new(false);
        internal readonly ManualResetEventSlim Release = new(false);

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments) => new(0, 0, 0, 0);
        public object BeginSegment(int ordinal)
        {
            Entered.Set();
            Assert.True(Release.Wait(TimeSpan.FromSeconds(2)));
            return new List<byte>();
        }
        public void Append(object segment, ReadOnlySpan<byte> record) =>
            ((List<byte>)segment).AddRange(record.ToArray());
        public void CommitSegment(object segment) { }
        public void DiscardSegment(object segment) { }
        public void DeleteOldestCommitted() { }
        public void Dispose()
        {
            Release.Set();
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private sealed class FailingStorage : IRestartAwareTraceSegmentStorage
    {
        internal int Discarded;
        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments) => new(0, 0, 0, 0);
        public object BeginSegment(int ordinal) => new object();
        public void Append(object segment, ReadOnlySpan<byte> record) =>
            throw new InvalidOperationException("injected append failure");
        public void CommitSegment(object segment) => throw new InvalidOperationException();
        public void DiscardSegment(object segment) => Interlocked.Increment(ref Discarded);
        public void DeleteOldestCommitted() => throw new InvalidOperationException();
    }

    private sealed class BlockingReconcileStorage : IRestartAwareTraceSegmentStorage, IDisposable
    {
        internal readonly ManualResetEventSlim Entered = new(false);
        internal readonly ManualResetEventSlim Release = new(false);

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments)
        {
            Entered.Set();
            Assert.True(Release.Wait(TimeSpan.FromSeconds(2)));
            return new TraceSegmentStorageRecovery(0, 0, 0, 0);
        }

        public object BeginSegment(int ordinal) => throw new InvalidOperationException();
        public void Append(object segment, ReadOnlySpan<byte> record) => throw new InvalidOperationException();
        public void CommitSegment(object segment) => throw new InvalidOperationException();
        public void DiscardSegment(object segment) => throw new InvalidOperationException();
        public void DeleteOldestCommitted() => throw new InvalidOperationException();

        public void Dispose()
        {
            Release.Set();
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
