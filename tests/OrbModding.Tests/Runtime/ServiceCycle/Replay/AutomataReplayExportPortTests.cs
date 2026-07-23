using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutomataReplayExportPortTests
{
    [Fact]
    public void FrozenCopyAdvancesOnlyThroughBoundedPerFrameSlices()
    {
        var fixture = ServiceCycleReplayArtifactCodecTests.ArtifactFixture.Create();
        var recorder = ServiceCycleReplayArtifactExporterTests.Recorder();
        using var storage = new MemoryStorage();
        using var exporter = new ServiceCycleReplayArtifactExporter(
            new ServiceCycleSemanticTraceSource(recorder),
            fixture.Session,
            storage,
            new ServiceCycleReplayExportOptions(true, 1));
        var port = new AutomataReplayExportPort(exporter, maximumSemanticEventsPerFrame: 3);
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
            TimeSpan.FromSeconds(2)));

        AutomataReplayExportStepResult result;
        do
        {
            result = port.ContinueSnapshot();
        }
        while (result == AutomataReplayExportStepResult.Pending);

        Assert.Equal(AutomataReplayExportStepResult.Accepted, result);
        Assert.Equal(recorder.Count, exporter.Metrics().SemanticEventsCopied);
        Assert.Equal(3, exporter.Metrics().PeakSemanticEventsCopiedPerRequest);
        Assert.True(SpinWait.SpinUntil(
            () => exporter.Metrics().ExportedArtifacts == 1,
            TimeSpan.FromSeconds(2)));
        Assert.True(ServiceCycleReplayArtifactCodec.Decode(storage.Latest).IsComplete);
        port.Stop();
    }

    private sealed class MemoryStorage : IRestartAwareTraceSegmentStorage, IDisposable
    {
        private readonly object _gate = new();
        private byte[] _latest = Array.Empty<byte>();

        internal byte[] Latest
        {
            get { lock (_gate) return _latest; }
        }

        public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments) => default;
        public object BeginSegment(int ordinal) => new List<byte>();
        public void Append(object segment, ReadOnlySpan<byte> record) =>
            ((List<byte>)segment).AddRange(record.ToArray());
        public void CommitSegment(object segment)
        {
            lock (_gate) _latest = ((List<byte>)segment).ToArray();
        }
        public void DiscardSegment(object segment) { }
        public void DeleteOldestCommitted() { }
        public void Dispose() { }
    }
}
