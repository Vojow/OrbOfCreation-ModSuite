using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplayArtifactExporterObserverTests
{
    [Fact]
    public void CommitNotificationFollowsDurableStorageOnTheWorker()
    {
        var storage = new ScriptedStorage();
        var observer = new RecordingObserver(() => storage.Committed);
        var ownerThread = Environment.CurrentManagedThreadId;
        using var exporter = CreateExporter(storage, observer);
        WaitForRunning(exporter);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => observer.Committed.Count == 1,
            TimeSpan.FromSeconds(2)));

        var committed = Assert.Single(observer.Committed);
        Assert.Equal(0, committed.Ordinal);
        Assert.True(committed.Bytes > 0);
        Assert.True(observer.StorageWasCommitted);
        Assert.DoesNotContain(ownerThread, observer.CallbackThreadIds);
    }

    [Fact]
    public void WriteFailureReportsDiscardAndExporterFaultWithoutCommit()
    {
        var storage = new ScriptedStorage(failWrite: true);
        var observer = new RecordingObserver();
        using var exporter = CreateExporter(storage, observer);
        WaitForRunning(exporter);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => observer.Discarded.Count == 1 && observer.Faults.Count == 1,
            TimeSpan.FromSeconds(2)));

        Assert.Empty(observer.Committed);
        Assert.Equal(
            (0, ServiceCycleReplayArtifactDiscardReason.WriteFailed),
            Assert.Single(observer.Discarded));
        Assert.Equal(
            ServiceCycleReplayExporterFaultReason.EncodingOrStorageFailure,
            Assert.Single(observer.Faults));
    }

    [Fact]
    public void WriteFailureReportsTheSecondQueuedArtifactAsDiscarded()
    {
        using var storage = new BlockingWriteStorage(failWrite: true);
        var observer = new RecordingObserver();
        using var exporter = CreateExporter(storage, observer);
        WaitForRunning(exporter);

        try
        {
            Assert.Equal(
                ServiceCycleReplayExportRequestResult.Accepted,
                exporter.RequestSnapshot());
            Assert.True(storage.WriteEntered.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(
                ServiceCycleReplayExportRequestResult.Accepted,
                exporter.RequestSnapshot());
        }
        finally
        {
            storage.ReleaseWrite.Set();
        }

        Assert.True(SpinWait.SpinUntil(
            () => observer.Discarded.Count == 2 && observer.Faults.Count == 1,
            TimeSpan.FromSeconds(2)));
        Assert.Contains(
            (0, ServiceCycleReplayArtifactDiscardReason.WriteFailed),
            observer.Discarded);
        Assert.Contains(
            (1, ServiceCycleReplayArtifactDiscardReason.ExporterFaulted),
            observer.Discarded);
        Assert.Empty(observer.Committed);
    }

    [Fact]
    public void AcceptedArtifactCanReportCommitAfterNonBlockingDisposalReturns()
    {
        using var storage = new BlockingWriteStorage(failWrite: false);
        var observer = new RecordingObserver();
        using var exporter = CreateExporter(storage, observer);
        WaitForRunning(exporter);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(storage.WriteEntered.Wait(TimeSpan.FromSeconds(2)));

        exporter.Dispose();

        Assert.Empty(observer.Committed);
        storage.ReleaseWrite.Set();
        Assert.True(SpinWait.SpinUntil(
            () => observer.Committed.Count == 1 &&
                exporter.Metrics().Status == ServiceCycleReplayExportStatus.Stopped,
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void RetentionFailureDoesNotMisreportACommittedArtifactAsDiscarded()
    {
        var storage = new ScriptedStorage(
            nextOrdinal: 7,
            retained: 1,
            failPrune: true);
        var observer = new RecordingObserver(() => storage.Committed);
        using var exporter = CreateExporter(storage, observer);
        WaitForRunning(exporter);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => observer.Committed.Count == 1 && observer.Faults.Count == 1,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(7, Assert.Single(observer.Committed).Ordinal);
        Assert.True(observer.StorageWasCommitted);
        Assert.Empty(observer.Discarded);
        Assert.Equal(
            ServiceCycleReplayExporterFaultReason.RetentionFailure,
            Assert.Single(observer.Faults));
    }

    [Fact]
    public void ObserverFailureCannotPoisonSuccessfulExport()
    {
        using var exporter = CreateExporter(new ScriptedStorage(), new ThrowingObserver());
        WaitForRunning(exporter);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 1,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(ServiceCycleReplayExportStatus.Running, exporter.Metrics().Status);
    }

    [Fact]
    public void ObserverFailureCannotChangeFailedExportState()
    {
        using var exporter = CreateExporter(
            new ScriptedStorage(failWrite: true),
            new ThrowingObserver());
        WaitForRunning(exporter);

        Assert.Equal(ServiceCycleReplayExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Faulted,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(1, exporter.Metrics().DiscardedArtifacts);
        Assert.Equal(1, exporter.Metrics().FaultCount);
    }

    [Fact]
    public void StartupFailureIsReportedWithoutAnArtifactOrdinal()
    {
        var observer = new RecordingObserver();
        using var exporter = CreateExporter(
            new ScriptedStorage(failStartup: true),
            observer);

        Assert.True(SpinWait.SpinUntil(
            () => observer.Faults.Count == 1,
            TimeSpan.FromSeconds(2)));
        Assert.Empty(observer.Committed);
        Assert.Empty(observer.Discarded);
        Assert.Equal(
            ServiceCycleReplayExporterFaultReason.StartupFailure,
            Assert.Single(observer.Faults));
    }

    [Fact]
    public void StopRacingReconciliationCannotRelabelAStartupFailure()
    {
        using var storage = new BlockingStartupFailureStorage();
        var observer = new RecordingObserver();
        using var exporter = CreateExporter(storage, observer);

        Assert.True(storage.ReconcileEntered.Wait(TimeSpan.FromSeconds(2)));
        exporter.Stop();
        storage.ReleaseReconcile.Set();

        Assert.True(SpinWait.SpinUntil(
            () => observer.Faults.Count == 1,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(
            ServiceCycleReplayExporterFaultReason.StartupFailure,
            Assert.Single(observer.Faults));
    }

    [Fact]
    public void FileStorageOrdinalExhaustionHasItsExactReason()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "service-cycle-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(
                Path.Combine(directory, "artifact-2147483647.oscr"),
                new byte[] { 1 });
            var observer = new RecordingObserver();
            using var exporter = CreateExporter(
                new FileTraceSegmentStorage(directory, "artifact", ".oscr"),
                observer);

            Assert.True(SpinWait.SpinUntil(
                () => observer.Faults.Count == 1,
                TimeSpan.FromSeconds(2)));
            Assert.Equal(
                ServiceCycleReplayExporterFaultReason.OrdinalExhausted,
                Assert.Single(observer.Faults));
            Assert.Empty(observer.Committed);
            Assert.Empty(observer.Discarded);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceCycleReplayArtifactExporter CreateExporter(
        IRestartAwareTraceSegmentStorage storage,
        IServiceCycleReplayExportObserver observer)
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        return new ServiceCycleReplayArtifactExporter(
            new ServiceCycleSemanticTraceSource(ServiceCycleReplayArtifactExporterTests.Recorder()),
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1),
            observer);
    }

    private static void WaitForRunning(ServiceCycleReplayArtifactExporter exporter) =>
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));

    private sealed class ScriptedStorage : IRestartAwareTraceSegmentStorage
    {
        private readonly int _nextOrdinal;
        private readonly int _retained;
        private readonly bool _failWrite;
        private readonly bool _failPrune;
        private readonly bool _failStartup;
        private int _committed;

        internal ScriptedStorage(
            int nextOrdinal = 0,
            int retained = 0,
            bool failWrite = false,
            bool failPrune = false,
            bool failStartup = false)
        {
            _nextOrdinal = nextOrdinal;
            _retained = retained;
            _failWrite = failWrite;
            _failPrune = failPrune;
            _failStartup = failStartup;
        }

        internal bool Committed => Volatile.Read(ref _committed) != 0;
        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments)
        {
            if (_failStartup) throw new InvalidOperationException("injected startup failure");
            return new TraceSegmentStorageRecovery(_nextOrdinal, _retained, 0, 0);
        }
        public object BeginSegment(int ordinal) => new List<byte>();
        public void Append(object segment, ReadOnlySpan<byte> record)
        {
            if (_failWrite) throw new InvalidOperationException("injected write failure");
            ((List<byte>)segment).AddRange(record.ToArray());
        }
        public void CommitSegment(object segment) => Volatile.Write(ref _committed, 1);
        public void DiscardSegment(object segment) { }
        public void DeleteOldestCommitted()
        {
            if (_failPrune) throw new InvalidOperationException("injected prune failure");
        }
    }

    private sealed class RecordingObserver : IServiceCycleReplayExportObserver
    {
        private readonly Func<bool>? _storageCommitted;
        private int _storageWasCommitted;

        internal RecordingObserver(Func<bool>? storageCommitted = null) =>
            _storageCommitted = storageCommitted;

        internal ConcurrentQueue<(int Ordinal, int Bytes)> Committed { get; } = new();
        internal ConcurrentQueue<(int Ordinal, ServiceCycleReplayArtifactDiscardReason Reason)> Discarded { get; } = new();
        internal ConcurrentQueue<ServiceCycleReplayExporterFaultReason> Faults { get; } = new();
        internal ConcurrentQueue<int> CallbackThreadIds { get; } = new();
        internal bool StorageWasCommitted => Volatile.Read(ref _storageWasCommitted) != 0;

        public void ArtifactCommitted(int ordinal, int bytes)
        {
            Committed.Enqueue((ordinal, bytes));
            CallbackThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            if (_storageCommitted?.Invoke() == true)
                Volatile.Write(ref _storageWasCommitted, 1);
        }

        public void ArtifactDiscarded(
            int ordinal,
            ServiceCycleReplayArtifactDiscardReason reason)
        {
            Discarded.Enqueue((ordinal, reason));
            CallbackThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        }

        public void ExporterFaulted(ServiceCycleReplayExporterFaultReason reason)
        {
            Faults.Enqueue(reason);
            CallbackThreadIds.Enqueue(Environment.CurrentManagedThreadId);
        }
    }

    private sealed class BlockingWriteStorage : IRestartAwareTraceSegmentStorage, IDisposable
    {
        private readonly bool _failWrite;

        internal BlockingWriteStorage(bool failWrite) => _failWrite = failWrite;

        internal ManualResetEventSlim WriteEntered { get; } = new(false);
        internal ManualResetEventSlim ReleaseWrite { get; } = new(false);

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments) =>
            new(0, 0, 0, 0);

        public object BeginSegment(int ordinal) => new List<byte>();

        public void Append(object segment, ReadOnlySpan<byte> record)
        {
            WriteEntered.Set();
            ReleaseWrite.Wait(TimeSpan.FromSeconds(2));
            if (_failWrite) throw new InvalidOperationException("injected write failure");
            ((List<byte>)segment).AddRange(record.ToArray());
        }

        public void CommitSegment(object segment) { }

        public void DiscardSegment(object segment) { }
        public void DeleteOldestCommitted() { }

        public void Dispose()
        {
            ReleaseWrite.Set();
            WriteEntered.Dispose();
            ReleaseWrite.Dispose();
        }
    }

    private sealed class BlockingStartupFailureStorage :
        IRestartAwareTraceSegmentStorage,
        IDisposable
    {
        internal ManualResetEventSlim ReconcileEntered { get; } = new(false);
        internal ManualResetEventSlim ReleaseReconcile { get; } = new(false);

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments)
        {
            ReconcileEntered.Set();
            ReleaseReconcile.Wait(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("injected startup failure");
        }

        public object BeginSegment(int ordinal) => throw new NotSupportedException();
        public void Append(object segment, ReadOnlySpan<byte> record) =>
            throw new NotSupportedException();
        public void CommitSegment(object segment) => throw new NotSupportedException();
        public void DiscardSegment(object segment) => throw new NotSupportedException();
        public void DeleteOldestCommitted() => throw new NotSupportedException();

        public void Dispose()
        {
            ReleaseReconcile.Set();
            ReconcileEntered.Dispose();
            ReleaseReconcile.Dispose();
        }
    }

    private sealed class ThrowingObserver : IServiceCycleReplayExportObserver
    {
        public void ArtifactCommitted(int ordinal, int bytes) => throw new InvalidOperationException();
        public void ArtifactDiscarded(
            int ordinal,
            ServiceCycleReplayArtifactDiscardReason reason) => throw new InvalidOperationException();
        public void ExporterFaulted(ServiceCycleReplayExporterFaultReason reason) =>
            throw new InvalidOperationException();
    }
}
