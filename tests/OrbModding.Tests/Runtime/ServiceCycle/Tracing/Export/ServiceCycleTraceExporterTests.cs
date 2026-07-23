using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing.Export;

public sealed class ServiceCycleTraceExporterTests
{
    [Fact]
    public void PublicCompositionAcceptsOnlyTheReadOnlyTraceSource()
    {
        var constructor = Assert.Single(typeof(ServiceCycleTraceExporter).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.Equal(typeof(ServiceCycleSemanticTraceSource), parameters[0].ParameterType);
        Assert.Equal(typeof(IRestartAwareTraceSegmentStorage), parameters[1].ParameterType);
        Assert.DoesNotContain(parameters, parameter =>
            parameter.ParameterType == typeof(ServiceCycleSemanticRecorder));
    }

    [Fact]
    public void ConstructionRejectsSourceCapacityAboveSupportedBound()
    {
        var recorder = Recorder(ServiceCycleTraceExporter.MaximumSupportedEventCapacity + 1);
        var source = new ServiceCycleSemanticTraceSource(recorder);
        var storage = new RecordingStorage(Environment.CurrentManagedThreadId);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCycleTraceExporter(
            source,
            storage,
            new ServiceCycleTraceExportOptions(enabled: true)));
        Assert.Equal(0, storage.BeginCalls);
    }

    [Fact]
    public void CompleteAndOverwrittenSnapshotsDecodeExactlyAsCanonicalSchemaThree()
    {
        var completeRecorder = Recorder(capacity: 4);
        Emit(completeRecorder, 1);
        Emit(completeRecorder, 2);
        var completeStorage = new RecordingStorage(Environment.CurrentManagedThreadId);
        var complete = EnabledExporter(completeRecorder, completeStorage);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, complete.RequestSnapshot());
        Assert.True(completeStorage.Committed.Wait(TestTimeout));
        var completeBytes = Assert.Single(completeStorage.FinalDocuments());
        var completeDocument = ServiceCycleTraceCodec.Decode(completeBytes);

        Assert.Equal(ServiceCycleTraceCodec.SchemaVersion, completeDocument.SchemaVersion);
        Assert.True(completeDocument.IsComplete);
        Assert.Equal(2, completeDocument.Count);
        Assert.Equal(1UL, completeDocument[0].Id.Sequence);
        Assert.Equal(2UL, completeDocument[1].Id.Sequence);
        Assert.Equal(ServiceCycleTraceCodec.GetEncodedLength(2), completeBytes.Length);
        complete.Stop();
        WaitForStatus(complete, ServiceCycleTraceExportStatus.Stopped);

        var incompleteRecorder = Recorder(capacity: 2);
        for (var generation = 1; generation <= 4; generation++) Emit(incompleteRecorder, generation);
        var incompleteStorage = new RecordingStorage(Environment.CurrentManagedThreadId);
        var incomplete = EnabledExporter(incompleteRecorder, incompleteStorage);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, incomplete.RequestSnapshot());
        Assert.True(incompleteStorage.Committed.Wait(TestTimeout));
        var incompleteBytes = Assert.Single(incompleteStorage.FinalDocuments());
        var incompleteDocument = ServiceCycleTraceCodec.Decode(incompleteBytes);

        Assert.False(incompleteDocument.IsComplete);
        Assert.Equal(new ServiceCycleTraceDropRange(
            incompleteRecorder.Session, 1, 2), incompleteDocument.Dropped);
        Assert.Equal(2, incompleteDocument.Count);
        Assert.Equal(3UL, incompleteDocument[0].Id.Sequence);
        Assert.Equal(4UL, incompleteDocument[1].Id.Sequence);
        Assert.Equal(ServiceCycleTraceCodec.GetEncodedLength(2), incompleteBytes.Length);
        incomplete.Stop();
        WaitForStatus(incomplete, ServiceCycleTraceExportStatus.Stopped);
    }

    [Fact]
    public void BlockedWorkerUsesExactlyTwoSlotsThenRejectsWithoutStorageWork()
    {
        var recorder = Recorder(capacity: 4);
        Emit(recorder, 1);
        var storage = new RecordingStorage(Environment.CurrentManagedThreadId, blockAppend: true);
        var exporter = EnabledExporter(recorder, storage);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(storage.AppendEntered.Wait(TestTimeout));
        Emit(recorder, 2);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());

        var beforeBegin = storage.BeginCalls;
        var beforeAppend = storage.AppendCalls;
        var beforeCount = recorder.Count;
        var rejectionTimer = Stopwatch.StartNew();
        var rejection = exporter.RequestSnapshot();
        rejectionTimer.Stop();
        Assert.Equal(ServiceCycleTraceExportRequestResult.Backpressured, rejection);
        Assert.True(
            rejectionTimer.Elapsed < TimeSpan.FromMilliseconds(100),
            $"Backpressure claim blocked for {rejectionTimer.Elapsed}.");
        Assert.Equal(beforeBegin, storage.BeginCalls);
        Assert.Equal(beforeAppend, storage.AppendCalls);
        Assert.Equal(beforeCount, recorder.Count);

        var blockedMetrics = exporter.Metrics();
        Assert.Equal(2, blockedMetrics.AcceptedSnapshots);
        Assert.Equal(1, blockedMetrics.BackpressureRejections);
        Assert.Equal(2, blockedMetrics.PendingSnapshots);

        storage.ReleaseAppend();
        WaitForExported(exporter, 2);
        exporter.Stop();
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Stopped);
        Assert.Equal(2, storage.BeginCalls);
        Assert.Equal(2, storage.AppendCalls);
        Assert.Equal(2, storage.CommitCalls);
        var completedMetrics = exporter.Metrics();
        Assert.Equal(ServiceCycleTraceExportStatus.Stopped, completedMetrics.Status);
        Assert.Equal(2, completedMetrics.AcceptedSnapshots);
        Assert.Equal(2, completedMetrics.ExportedSnapshots);
        Assert.Equal(0, completedMetrics.DiscardedSnapshots);
        Assert.Equal(1, completedMetrics.RejectedSnapshots);
        Assert.Equal(0, completedMetrics.PendingSnapshots);
        Assert.Equal(
            storage.FinalDocuments()[0].Length + storage.FinalDocuments()[1].Length,
            completedMetrics.BytesWritten);
    }

    [Fact]
    public void RetentionDeletesOldestCommittedSnapshotsAndMetricsStayBounded()
    {
        var recorder = Recorder(capacity: 4);
        var storage = new RecordingStorage(Environment.CurrentManagedThreadId);
        var exporter = EnabledExporter(recorder, storage, maximumCommittedSnapshots: 2);

        for (var snapshot = 1; snapshot <= 5; snapshot++)
        {
            Emit(recorder, snapshot);
            Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
            WaitForExported(exporter, snapshot);
        }

        exporter.Stop();
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Stopped);
        var metrics = exporter.Metrics();

        Assert.Equal(5, storage.BeginCalls);
        Assert.Equal(5, storage.AppendCalls);
        Assert.Equal(5, storage.CommitCalls);
        Assert.Equal(3, storage.DeleteCalls);
        var retained = storage.FinalDocuments();
        Assert.Equal(2, retained.Length);
        var penultimate = ServiceCycleTraceCodec.Decode(retained[0]);
        var latest = ServiceCycleTraceCodec.Decode(retained[1]);
        Assert.Equal(4UL, penultimate[penultimate.Count - 1].Id.Sequence);
        Assert.Equal(5UL, latest[latest.Count - 1].Id.Sequence);
        Assert.Equal(5, metrics.ExportedSnapshots);
        Assert.Equal(2, metrics.RetainedSnapshots);
        Assert.InRange(metrics.RetainedSnapshots, 0, 2);
        Assert.Equal(0, metrics.PendingSnapshots);
    }

    [Fact]
    public void RetentionCapOnePreservesFirstCommitWhenSecondAppendFaults()
    {
        var recorder = Recorder(capacity: 4);
        Emit(recorder, 1);
        var storage = new RecordingStorage(
            Environment.CurrentManagedThreadId,
            throwOnAppendCall: 2);
        var exporter = EnabledExporter(recorder, storage, maximumCommittedSnapshots: 1);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        WaitForExported(exporter, 1);
        var firstCommitted = Assert.Single(storage.FinalDocuments());
        Emit(recorder, 2);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(storage.Discarded.Wait(TestTimeout));
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Faulted);

        var retained = Assert.Single(storage.FinalDocuments());
        Assert.Equal(firstCommitted, retained);
        Assert.Equal(2, storage.BeginCalls);
        Assert.Equal(2, storage.AppendCalls);
        Assert.Equal(1, storage.CommitCalls);
        Assert.Equal(0, storage.DeleteCalls);
        Assert.Equal(1, storage.DiscardCalls);
        var metrics = exporter.Metrics();
        Assert.Equal(1, metrics.ExportedSnapshots);
        Assert.Equal(1, metrics.DiscardedSnapshots);
        Assert.Equal(1, metrics.RetainedSnapshots);
        Assert.Equal(1, metrics.FaultCount);
    }

    [Fact]
    public void RetentionDeletionFaultPreservesBothCommittedDocumentsThenClosesAdmission()
    {
        var recorder = Recorder(capacity: 4);
        var storage = new RecordingStorage(
            Environment.CurrentManagedThreadId,
            throwOnDeleteCall: 1);
        var exporter = EnabledExporter(recorder, storage, maximumCommittedSnapshots: 1);

        Emit(recorder, 1);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        WaitForExported(exporter, 1);
        Emit(recorder, 2);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Faulted);

        Assert.Equal(2, storage.FinalDocuments().Length);
        Assert.Equal(2, storage.CommitCalls);
        Assert.Equal(1, storage.DeleteCalls);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Faulted, exporter.RequestSnapshot());
        var metrics = exporter.Metrics();
        Assert.Equal(2, metrics.ExportedSnapshots);
        Assert.Equal(2, metrics.RetainedSnapshots);
        Assert.Equal(1, metrics.FaultCount);
        Assert.Equal(0, metrics.PendingSnapshots);
    }

    [Fact]
    public void EmissionFaultRejectsNewSnapshotWithoutPublishingFalseCompleteness()
    {
        var recorder = Recorder(capacity: 4);
        Emit(recorder, 1);
        var source = new ServiceCycleSemanticTraceSource(recorder);
        var storage = new RecordingStorage(Environment.CurrentManagedThreadId);
        var exporter = new ServiceCycleTraceExporter(
            source,
            storage,
            new ServiceCycleTraceExportOptions(enabled: true));
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Running);
        source.RecordEmissionFault();

        Assert.Equal(ServiceCycleTraceExportRequestResult.Faulted, exporter.RequestSnapshot());
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Faulted);
        Assert.Equal(0, storage.BeginCalls);
        Assert.Equal(0, storage.AppendCalls);
        Assert.Equal(0, storage.CommitCalls);
        Assert.Empty(storage.FinalDocuments());
        var metrics = exporter.Metrics();
        Assert.Equal(0, metrics.AcceptedSnapshots);
        Assert.Equal(1, metrics.UnavailableRejections);
        Assert.Equal(1, metrics.FaultCount);
        Assert.Equal(0, metrics.RetainedSnapshots);
        exporter.Dispose();
    }

    [Fact]
    public void EncodingAndAllStorageCallsStayOffOwnerThread()
    {
        var owner = Environment.CurrentManagedThreadId;
        var recorder = Recorder(capacity: 4);
        Emit(recorder, 1);
        var storage = new RecordingStorage(owner, decodeDuringAppend: true);
        var exporter = EnabledExporter(recorder, storage);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(storage.Committed.Wait(TestTimeout));
        exporter.Stop();
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Stopped);

        Assert.False(storage.OwnerThreadObserved);
        Assert.True(storage.DecodeSucceeded);
        Assert.NotEqual(owner, storage.LastStorageThreadId);
        Assert.Equal(1, storage.BeginCalls);
        Assert.Equal(1, storage.AppendCalls);
        Assert.Equal(1, storage.CommitCalls);
    }

    [Fact]
    public void StorageFaultDiscardsTemporaryFileLatchesAndNeverRetries()
    {
        var recorder = Recorder(capacity: 4);
        Emit(recorder, 1);
        var storage = new RecordingStorage(Environment.CurrentManagedThreadId, throwOnAppend: true);
        var exporter = EnabledExporter(recorder, storage);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Faulted);
        WaitForDiscarded(exporter, 1);
        Assert.True(storage.Discarded.Wait(TestTimeout));

        var beforeBegin = storage.BeginCalls;
        Assert.Equal(ServiceCycleTraceExportRequestResult.Faulted, exporter.RequestSnapshot());
        Assert.Equal(beforeBegin, storage.BeginCalls);
        Assert.Equal(1, storage.AppendCalls);
        Assert.Equal(0, storage.CommitCalls);
        Assert.Equal(1, storage.DiscardCalls);
        Assert.Empty(storage.FinalDocuments());

        var metrics = exporter.Metrics();
        Assert.Equal(ServiceCycleTraceExportStatus.Faulted, metrics.Status);
        Assert.Equal(1, metrics.AcceptedSnapshots);
        Assert.Equal(1, metrics.DiscardedSnapshots);
        Assert.Equal(1, metrics.UnavailableRejections);
        Assert.Equal(1, metrics.FaultCount);
        Assert.Equal(0, metrics.PendingSnapshots);
        exporter.Dispose();
        Assert.Equal(ServiceCycleTraceExportStatus.Faulted, exporter.Metrics().Status);
    }

    [Fact]
    public void StopAndDisposeOnlySignalWhileStorageIsBlocked()
    {
        var recorder = Recorder(capacity: 2);
        Emit(recorder, 1);
        var stopStorage = new RecordingStorage(Environment.CurrentManagedThreadId, blockAppend: true);
        var stopping = EnabledExporter(recorder, stopStorage);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, stopping.RequestSnapshot());
        Assert.True(stopStorage.AppendEntered.Wait(TestTimeout));

        var stopwatch = Stopwatch.StartNew();
        stopping.Stop();
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100), $"Stop blocked for {stopwatch.Elapsed}.");
        Assert.Equal(ServiceCycleTraceExportStatus.Stopping, stopping.Metrics().Status);
        stopStorage.ReleaseAppend();
        WaitForStatus(stopping, ServiceCycleTraceExportStatus.Stopped);

        var disposeRecorder = Recorder(capacity: 2);
        Emit(disposeRecorder, 1);
        var disposeStorage = new RecordingStorage(Environment.CurrentManagedThreadId, blockAppend: true);
        var disposing = EnabledExporter(disposeRecorder, disposeStorage);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, disposing.RequestSnapshot());
        Assert.True(disposeStorage.AppendEntered.Wait(TestTimeout));

        stopwatch.Restart();
        disposing.Dispose();
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100), $"Dispose blocked for {stopwatch.Elapsed}.");
        Assert.Equal(ServiceCycleTraceExportStatus.Stopping, disposing.Metrics().Status);
        disposeStorage.ReleaseAppend();
        WaitForStatus(disposing, ServiceCycleTraceExportStatus.Stopped);
    }

    [Fact]
    public void StopRacingWorkerFaultRemainsNonblockingAndFaultWinsStably()
    {
        var recorder = Recorder(capacity: 2);
        Emit(recorder, 1);
        var storage = new RecordingStorage(
            Environment.CurrentManagedThreadId,
            blockAppend: true,
            throwOnAppend: true);
        var exporter = EnabledExporter(recorder, storage);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(storage.AppendEntered.Wait(TestTimeout));

        var stopwatch = Stopwatch.StartNew();
        exporter.Stop();
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100), $"Stop blocked for {stopwatch.Elapsed}.");
        storage.ReleaseAppend();
        Assert.True(storage.Discarded.Wait(TestTimeout));
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Faulted);

        Assert.Equal(1, storage.BeginCalls);
        Assert.Equal(1, storage.AppendCalls);
        Assert.Equal(0, storage.CommitCalls);
        Assert.Equal(1, storage.DiscardCalls);
        Assert.Equal(1, exporter.Metrics().FaultCount);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Faulted, exporter.RequestSnapshot());
    }

    [Fact]
    public void WorkerFaultAdjacentToAdmissionNeverUsesDisposedSynchronizationHandles()
    {
        var recorder = Recorder(ServiceCycleTraceExporter.MaximumSupportedEventCapacity);
        for (var generation = 1; generation <= recorder.Capacity; generation++) Emit(recorder, generation);
        var storage = new RecordingStorage(
            Environment.CurrentManagedThreadId,
            blockAppend: true,
            throwOnAppend: true);
        var exporter = EnabledExporter(recorder, storage);
        var worker = (Thread)typeof(ServiceCycleTraceExporter)
            .GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(exporter)!;
        var secondSlot = (ServiceCycleTraceExportSlot)typeof(ServiceCycleTraceExporter)
            .GetField("_second", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(exporter)!;
        storage.SetAppendFaultCondition(() =>
            Volatile.Read(ref secondSlot.State) != ServiceCycleTraceExportSlot.Free);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        Assert.True(storage.AppendEntered.Wait(TestTimeout));
        storage.ReleaseAppend();
        Assert.True(storage.WaitingForFaultCondition.Wait(TestTimeout));

        var result = default(ServiceCycleTraceExportRequestResult);
        var requestException = Record.Exception(() => result = exporter.RequestSnapshot());

        Assert.Null(requestException);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, result);
        Assert.True(storage.FaultConditionObserved.Wait(TestTimeout));
        Assert.True(storage.Discarded.Wait(TestTimeout));
        WaitForDiscarded(exporter, 2);
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Faulted);
        Assert.True(worker.Join(TestTimeout));
        Assert.Null(Record.Exception(() => exporter.RequestSnapshot()));
        var stopException = Record.Exception(exporter.Stop);
        Assert.Null(stopException);
        Assert.Equal(1, storage.DiscardCalls);
        Assert.Empty(storage.FinalDocuments());
        Assert.Equal(1, exporter.Metrics().FaultCount);
    }

    [Fact]
    public void CompletedAndFaultedExportersReclaimTheirWorkerWaitHandle()
    {
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var completedRecorder = Recorder(capacity: 2);
            var completed = EnabledExporter(
                completedRecorder,
                new RecordingStorage(Environment.CurrentManagedThreadId));
            var completedWake = WorkerWakeHandle(completed);
            completed.Stop();
            WaitForStatus(completed, ServiceCycleTraceExportStatus.Stopped);
            WaitForClosed(completedWake);

            var faultedRecorder = Recorder(capacity: 2);
            Emit(faultedRecorder, iteration + 1);
            var faulted = EnabledExporter(
                faultedRecorder,
                new RecordingStorage(Environment.CurrentManagedThreadId, throwOnAppend: true));
            var faultedWake = WorkerWakeHandle(faulted);
            Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, faulted.RequestSnapshot());
            WaitForStatus(faulted, ServiceCycleTraceExportStatus.Faulted);
            WaitForClosed(faultedWake);
        }
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void WarmAcceptedAndBackpressuredOwnerPathsAllocateNothing()
    {
        var recorder = Recorder(capacity: 4);
        Emit(recorder, 1);
        var storage = new RecordingStorage(Environment.CurrentManagedThreadId);
        var exporter = EnabledExporter(recorder, storage);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, exporter.RequestSnapshot());
        WaitForExported(exporter, 1);

        _ = exporter.Metrics();
        var beforeAccepted = GC.GetAllocatedBytesForCurrentThread();
        var accepted = exporter.RequestSnapshot();
        var acceptedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAccepted;

        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, accepted);
        Assert.Equal(0, acceptedBytes);
        WaitForExported(exporter, 2);
        exporter.Stop();
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Stopped);

        var blockedRecorder = Recorder(capacity: 4);
        Emit(blockedRecorder, 1);
        var blockedStorage = new RecordingStorage(Environment.CurrentManagedThreadId, blockAppend: true);
        var blocked = EnabledExporter(blockedRecorder, blockedStorage);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, blocked.RequestSnapshot());
        Assert.True(blockedStorage.AppendEntered.Wait(TestTimeout));
        Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, blocked.RequestSnapshot());
        Assert.Equal(ServiceCycleTraceExportRequestResult.Backpressured, blocked.RequestSnapshot());

        var beforeRejected = GC.GetAllocatedBytesForCurrentThread();
        var rejected = blocked.RequestSnapshot();
        var rejectedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRejected;

        Assert.Equal(ServiceCycleTraceExportRequestResult.Backpressured, rejected);
        Assert.Equal(0, rejectedBytes);
        blockedStorage.ReleaseAppend();
        WaitForExported(blocked, 2);
        blocked.Stop();
        WaitForStatus(blocked, ServiceCycleTraceExportStatus.Stopped);
    }

    [Fact]
    public void ExportIsDisabledUnlessExplicitlyEnabled()
    {
        var recorder = Recorder(capacity: 2);
        Emit(recorder, 1);
        var storage = new RecordingStorage(Environment.CurrentManagedThreadId);
        var exporter = new ServiceCycleTraceExporter(new ServiceCycleSemanticTraceSource(recorder), storage);

        Assert.Equal(ServiceCycleTraceExportRequestResult.Disabled, exporter.RequestSnapshot());
        Assert.Equal(ServiceCycleTraceExportStatus.Disabled, exporter.Metrics().Status);
        Assert.Equal(0, storage.BeginCalls);
        var stopwatch = Stopwatch.StartNew();
        exporter.Stop();
        exporter.Dispose();
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        Assert.Equal(ServiceCycleTraceExportStatus.Disabled, exporter.Metrics().Status);
        Assert.Equal(0, storage.BeginCalls);
    }

    [Fact]
    public void DisabledExporterWithRealStorageDoesNotCreateItsDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "service-trace-disabled-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorder = Recorder(capacity: 2);
            var storage = new FileTraceSegmentStorage(directory, "snapshot", ".osce");
            var exporter = new ServiceCycleTraceExporter(
                new ServiceCycleSemanticTraceSource(recorder),
                storage);

            Assert.Equal(ServiceCycleTraceExportStatus.Disabled, exporter.Metrics().Status);
            Assert.Equal(ServiceCycleTraceExportRequestResult.Disabled, exporter.RequestSnapshot());
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RealStorageRestartResumesOrdinalAndRetainsAcrossExporterLifetimes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "service-trace-restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var firstRecorder = Recorder(capacity: 4);
            var first = EnabledExporter(
                firstRecorder,
                new FileTraceSegmentStorage(directory, "snapshot", ".osce"),
                maximumCommittedSnapshots: 2);
            Emit(firstRecorder, 1);
            Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, first.RequestSnapshot());
            WaitForExported(first, 1);
            Emit(firstRecorder, 2);
            Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, first.RequestSnapshot());
            WaitForExported(first, 2);
            first.Stop();
            WaitForStatus(first, ServiceCycleTraceExportStatus.Stopped);

            var secondRecorder = Recorder(capacity: 4);
            Emit(secondRecorder, 3);
            var second = EnabledExporter(
                secondRecorder,
                new FileTraceSegmentStorage(directory, "snapshot", ".osce"),
                maximumCommittedSnapshots: 2);
            Assert.Equal(2, second.Metrics().RetainedSnapshots);
            Assert.Equal(ServiceCycleTraceExportRequestResult.Accepted, second.RequestSnapshot());
            WaitForExported(second, 1);
            second.Stop();
            WaitForStatus(second, ServiceCycleTraceExportStatus.Stopped);

            var files = Directory.GetFiles(directory, "snapshot-*.osce");
            Array.Sort(files, StringComparer.Ordinal);
            Assert.Equal(2, files.Length);
            Assert.EndsWith("snapshot-000001.osce", files[0], StringComparison.Ordinal);
            Assert.EndsWith("snapshot-000002.osce", files[1], StringComparison.Ordinal);
            Assert.All(files, path => Assert.NotNull(ServiceCycleTraceCodec.Decode(File.ReadAllBytes(path))));
            Assert.Equal(2, second.Metrics().RetainedSnapshots);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RealStorageStartupReconciliationPublishesPruneAndTemporaryCleanupMetrics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "service-trace-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileTraceSegmentStorage(directory, "snapshot", ".osce");
            for (var ordinal = 0; ordinal < 3; ordinal++)
            {
                var segment = storage.BeginSegment(ordinal);
                storage.Append(segment, new byte[] { (byte)ordinal });
                storage.CommitSegment(segment);
            }
            File.WriteAllBytes(
                Path.Combine(directory, "snapshot-000003.osce.tmp-" + Guid.NewGuid().ToString("N")),
                new byte[] { 9 });

            var exporter = EnabledExporter(
                Recorder(capacity: 2),
                new FileTraceSegmentStorage(directory, "snapshot", ".osce"),
                maximumCommittedSnapshots: 2);
            var metrics = exporter.Metrics();

            Assert.Equal(2, metrics.RetainedSnapshots);
            Assert.Equal(1, metrics.StartupPrunedSnapshots);
            Assert.Equal(1, metrics.StaleTemporaryFilesRemoved);
            exporter.Stop();
            WaitForStatus(exporter, ServiceCycleTraceExportStatus.Stopped);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InitializationAndStopRemainNonblockingWhileRecoveryIsBlocked()
    {
        var storage = new RecordingStorage(
            Environment.CurrentManagedThreadId,
            blockReconcile: true);
        var stopwatch = Stopwatch.StartNew();
        var exporter = new ServiceCycleTraceExporter(
            new ServiceCycleSemanticTraceSource(Recorder(capacity: 2)),
            storage,
            new ServiceCycleTraceExportOptions(enabled: true));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        Assert.True(storage.ReconcileEntered.Wait(TestTimeout));
        Assert.Equal(ServiceCycleTraceExportStatus.Initializing, exporter.Metrics().Status);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Initializing, exporter.RequestSnapshot());
        stopwatch.Restart();
        exporter.Stop();
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        Assert.Equal(ServiceCycleTraceExportStatus.Stopping, exporter.Metrics().Status);

        storage.ReleaseReconcile();
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Stopped);
        Assert.Equal(0, storage.BeginCalls);
    }

    [Fact]
    public void RecoveryFaultAndOrdinalExhaustionFailClosedWithoutStorageWrites()
    {
        var recoveryFault = new RecordingStorage(
            Environment.CurrentManagedThreadId,
            throwOnReconcile: true);
        var faulted = new ServiceCycleTraceExporter(
            new ServiceCycleSemanticTraceSource(Recorder(capacity: 2)),
            recoveryFault,
            new ServiceCycleTraceExportOptions(enabled: true));
        WaitForStatus(faulted, ServiceCycleTraceExportStatus.Faulted);
        Assert.Equal(1, faulted.Metrics().FaultCount);
        Assert.Equal(0, recoveryFault.BeginCalls);

        var exhaustedStorage = new RecordingStorage(
            Environment.CurrentManagedThreadId,
            recoveryNextOrdinal: int.MaxValue);
        var exhausted = EnabledExporter(Recorder(capacity: 2), exhaustedStorage);
        Assert.Equal(ServiceCycleTraceExportRequestResult.Faulted, exhausted.RequestSnapshot());
        WaitForStatus(exhausted, ServiceCycleTraceExportStatus.Faulted);
        Assert.Equal(0, exhaustedStorage.BeginCalls);
        Assert.Equal(0, exhausted.Metrics().AcceptedSnapshots);
        Assert.Equal(0, exhausted.Metrics().PendingSnapshots);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static ServiceCycleTraceExporter EnabledExporter(
        ServiceCycleSemanticRecorder recorder,
        IRestartAwareTraceSegmentStorage storage,
        int maximumCommittedSnapshots = 4)
    {
        var exporter = new ServiceCycleTraceExporter(
            new ServiceCycleSemanticTraceSource(recorder),
            storage,
            new ServiceCycleTraceExportOptions(
                enabled: true,
                maximumCommittedSnapshots: maximumCommittedSnapshots));
        WaitForStatus(exporter, ServiceCycleTraceExportStatus.Running);
        return exporter;
    }

    private static ServiceCycleSemanticRecorder Recorder(int capacity)
    {
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(700), capacity, serviceCapacity: 1);
        recorder.RegisterService(0, new ServiceId("test.export"));
        return recorder;
    }

    private static void Emit(ServiceCycleSemanticRecorder recorder, int generation) =>
        recorder.ConfigurationPublished(
            0,
            new ConfigGeneration((ulong)generation),
            new MonotonicTimestamp(generation));

    private static void WaitForExported(ServiceCycleTraceExporter exporter, long expected)
    {
        var timer = Stopwatch.StartNew();
        while (exporter.Metrics().ExportedSnapshots != expected && timer.Elapsed < TestTimeout)
            Thread.Sleep(1);
        Assert.Equal(expected, exporter.Metrics().ExportedSnapshots);
    }

    private static void WaitForDiscarded(ServiceCycleTraceExporter exporter, long expected)
    {
        var timer = Stopwatch.StartNew();
        while (exporter.Metrics().DiscardedSnapshots != expected && timer.Elapsed < TestTimeout)
            Thread.Sleep(1);
        Assert.Equal(expected, exporter.Metrics().DiscardedSnapshots);
    }

    private static void WaitForStatus(ServiceCycleTraceExporter exporter, ServiceCycleTraceExportStatus expected)
    {
        var timer = Stopwatch.StartNew();
        while (exporter.Metrics().Status != expected && timer.Elapsed < TestTimeout)
            Thread.Sleep(1);
        Assert.Equal(expected, exporter.Metrics().Status);
    }

    private static Microsoft.Win32.SafeHandles.SafeWaitHandle WorkerWakeHandle(
        ServiceCycleTraceExporter exporter) =>
        ((AutoResetEvent)typeof(ServiceCycleTraceExporter)
            .GetField("_wake", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(exporter)!).SafeWaitHandle;

    private static void WaitForClosed(Microsoft.Win32.SafeHandles.SafeWaitHandle handle)
    {
        var timer = Stopwatch.StartNew();
        while (!handle.IsClosed && timer.Elapsed < TestTimeout) Thread.Sleep(1);
        Assert.True(handle.IsClosed);
    }

    private sealed class RecordingStorage : IRestartAwareTraceSegmentStorage
    {
        private readonly object _gate = new();
        private readonly List<byte[]> _finalDocuments = new();
        private readonly int _ownerThreadId;
        private readonly ManualResetEventSlim? _appendRelease;
        private readonly bool _decodeDuringAppend;
        private readonly int _throwOnAppendCall;
        private readonly int _throwOnCommitCall;
        private readonly int _throwOnDeleteCall;
        private readonly ManualResetEventSlim? _reconcileRelease;
        private readonly bool _throwOnReconcile;
        private readonly int _recoveryNextOrdinal;
        private Func<bool>? _appendFaultCondition;
        private int _beginCalls;
        private int _appendCalls;
        private int _commitCalls;
        private int _discardCalls;
        private int _deleteCalls;
        private int _lastStorageThreadId;
        private int _ownerThreadObserved;
        private int _decodeSucceeded;

        internal RecordingStorage(
            int ownerThreadId,
            bool blockAppend = false,
            bool decodeDuringAppend = false,
            bool throwOnAppend = false,
            int throwOnAppendCall = 0,
            int throwOnCommitCall = 0,
            int throwOnDeleteCall = 0,
            bool blockReconcile = false,
            bool throwOnReconcile = false,
            int recoveryNextOrdinal = 0)
        {
            _ownerThreadId = ownerThreadId;
            _appendRelease = blockAppend ? new ManualResetEventSlim(false) : null;
            _decodeDuringAppend = decodeDuringAppend;
            _throwOnAppendCall = throwOnAppendCall != 0 ? throwOnAppendCall : throwOnAppend ? 1 : 0;
            _throwOnCommitCall = throwOnCommitCall;
            _throwOnDeleteCall = throwOnDeleteCall;
            _reconcileRelease = blockReconcile ? new ManualResetEventSlim(false) : null;
            _throwOnReconcile = throwOnReconcile;
            _recoveryNextOrdinal = recoveryNextOrdinal;
        }

        internal ManualResetEventSlim AppendEntered { get; } = new(false);
        internal ManualResetEventSlim Committed { get; } = new(false);
        internal ManualResetEventSlim Discarded { get; } = new(false);
        internal ManualResetEventSlim WaitingForFaultCondition { get; } = new(false);
        internal ManualResetEventSlim FaultConditionObserved { get; } = new(false);
        internal ManualResetEventSlim ReconcileEntered { get; } = new(false);
        internal int BeginCalls => Volatile.Read(ref _beginCalls);
        internal int AppendCalls => Volatile.Read(ref _appendCalls);
        internal int CommitCalls => Volatile.Read(ref _commitCalls);
        internal int DiscardCalls => Volatile.Read(ref _discardCalls);
        internal int DeleteCalls => Volatile.Read(ref _deleteCalls);
        internal int LastStorageThreadId => Volatile.Read(ref _lastStorageThreadId);
        internal bool OwnerThreadObserved => Volatile.Read(ref _ownerThreadObserved) != 0;
        internal bool DecodeSucceeded => Volatile.Read(ref _decodeSucceeded) != 0;

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments)
        {
            ObserveThread();
            ReconcileEntered.Set();
            _reconcileRelease?.Wait();
            if (_throwOnReconcile) throw new IOException("Injected reconciliation fault.");
            lock (_gate)
            {
                while (_finalDocuments.Count > maximumCommittedSegments)
                    _finalDocuments.RemoveAt(0);
                return new TraceSegmentStorageRecovery(
                    _recoveryNextOrdinal == 0 ? _finalDocuments.Count : _recoveryNextOrdinal,
                    _finalDocuments.Count,
                    0,
                    0);
            }
        }

        public object BeginSegment(int ordinal)
        {
            ObserveThread();
            Interlocked.Increment(ref _beginCalls);
            return new Segment(ordinal);
        }

        public void Append(object segment, ReadOnlySpan<byte> record)
        {
            ObserveThread();
            var call = Interlocked.Increment(ref _appendCalls);
            AppendEntered.Set();
            _appendRelease?.Wait();
            var actual = (Segment)segment;
            actual.Bytes = record.ToArray();
            if (_decodeDuringAppend)
            {
                _ = ServiceCycleTraceCodec.Decode(actual.Bytes);
                Volatile.Write(ref _decodeSucceeded, 1);
            }
            if (call == _throwOnAppendCall)
            {
                if (_appendFaultCondition is not null)
                {
                    WaitingForFaultCondition.Set();
                    while (!_appendFaultCondition()) Thread.Sleep(0);
                    FaultConditionObserved.Set();
                }
                throw new InvalidOperationException("Injected append fault.");
            }
        }

        public void CommitSegment(object segment)
        {
            ObserveThread();
            var call = Interlocked.Increment(ref _commitCalls);
            if (call == _throwOnCommitCall) throw new InvalidOperationException("Injected commit fault.");
            var actual = (Segment)segment;
            lock (_gate) _finalDocuments.Add(actual.Bytes!);
            Committed.Set();
        }

        public void DiscardSegment(object segment)
        {
            ObserveThread();
            Interlocked.Increment(ref _discardCalls);
            ((Segment)segment).Bytes = null;
            Discarded.Set();
        }

        public void DeleteOldestCommitted()
        {
            ObserveThread();
            var call = Interlocked.Increment(ref _deleteCalls);
            if (call == _throwOnDeleteCall) throw new InvalidOperationException("Injected delete fault.");
            lock (_gate) _finalDocuments.RemoveAt(0);
        }

        internal void ReleaseAppend() => _appendRelease!.Set();
        internal void ReleaseReconcile() => _reconcileRelease!.Set();

        internal void SetAppendFaultCondition(Func<bool> condition) =>
            _appendFaultCondition = condition ?? throw new ArgumentNullException(nameof(condition));

        internal byte[][] FinalDocuments()
        {
            lock (_gate) return _finalDocuments.ToArray();
        }

        private void ObserveThread()
        {
            var current = Environment.CurrentManagedThreadId;
            Volatile.Write(ref _lastStorageThreadId, current);
            if (current == _ownerThreadId) Volatile.Write(ref _ownerThreadObserved, 1);
        }

        private sealed class Segment
        {
            internal Segment(int ordinal)
            {
                Ordinal = ordinal;
            }

            internal int Ordinal { get; }
            internal byte[]? Bytes { get; set; }
        }
    }
}
