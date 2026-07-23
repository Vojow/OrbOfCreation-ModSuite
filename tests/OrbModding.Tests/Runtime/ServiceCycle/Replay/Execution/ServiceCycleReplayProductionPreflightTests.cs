using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayProductionPreflightTests
{
    [Fact]
    public void LifecycleConstructionEvidenceIsRejectedBeforeFeatureFactoryCallbacks()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var artifact = WithLifecycleConstructionDeferral(captured.Artifact);
        var factory = new Factory();
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var result = ServiceCycleReplayProductionDriver.Run(
            artifact, registration, factory, TimeSpan.FromSeconds(1));

        Assert.False(result.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.LifecycleConstructionEvidenceUnsupported,
            result.Failure.Fault.DetailCode);
        Assert.Equal(0, factory.CreationCount);
    }

    private static ServiceCycleReplayArtifactDocument WithLifecycleConstructionDeferral(
        ServiceCycleReplayArtifactDocument source)
    {
        var trace = source.SemanticTrace;
        var events = new ServiceCycleSemanticEvent[trace.Count + 1];
        for (var index = 0; index < trace.Count; index++) events[index] = trace[index];
        var cycle = source.GetCycle(0).Key;
        var service = new ServiceCycleTraceServiceId(checked((ulong)cycle.TraceServiceKey));
        var payload = ServiceCycleSemanticPayload.LifecycleConstructionDeferred(
            service,
            cycle.Lifecycle,
            CommonServiceDecisionCodes.TransientContention.Value,
            100,
            101);
        var id = new ServiceCycleTraceEventId(trace.Session, checked((ulong)events.Length));
        var parent = trace.Count == 0 ? default : trace[^1].Id;
        events[^1] = new ServiceCycleSemanticEvent(
            id, parent, ServiceCycleSemanticEventKind.LifecycleConstructionDeferred, in payload);
        var semantic = new ServiceCycleTraceDocument(
            trace.SchemaVersion, trace.Session, trace.Dropped, trace.ServiceCapacity, events);
        var codecs = new ServiceCycleReplayCodecManifestEntry[source.CodecCount];
        for (var index = 0; index < codecs.Length; index++) codecs[index] = source.GetCodec(index);
        var cycles = new ServiceCycleReplayArtifactCycle[source.CycleCount];
        for (var index = 0; index < cycles.Length; index++) cycles[index] = source.GetCycle(index);
        return new ServiceCycleReplayArtifactDocument(
            source.Prepared,
            source.Fence,
            semantic,
            source.Recording,
            codecs,
            ServiceCycleReplayCodecIndex.Build(codecs),
            cycles,
            source.Eligibility);
    }
}
