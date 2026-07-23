using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplayAllocationTests
{
    [Fact]
    public void OptInCapacityBuffersAreNotAllocatedOnOwnerConstruction()
    {
        var traceSession = new ServiceCycleTraceSessionId(904);
        var recorder = new ServiceCycleSemanticRecorder(traceSession, 4_096, 1);
        recorder.RegisterService(0, new ServiceId("test.replay-allocation"));
        using var storage = new BlockingReconcileStorage();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 4 * 1024 * 1024, 65_536, 8_192));
        using var exporter = new ServiceCycleReplayArtifactExporter(
            new ServiceCycleSemanticTraceSource(recorder),
            recording,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 1, 1024 * 1024 - 1);
        Assert.Equal(4 * 1024 * 1024, recording.ByteCapacity);
        Assert.Equal(65_536, recording.RecordCapacity);
        Assert.Equal(8_192, recording.CycleFooterCapacity);
        Assert.True(storage.Entered.Wait(TimeSpan.FromSeconds(2)));

        exporter.Stop();
        storage.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Stopped,
            TimeSpan.FromSeconds(2)));
    }

    private sealed class BlockingReconcileStorage : IRestartAwareTraceSegmentStorage, IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Release { get; } = new(false);

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
