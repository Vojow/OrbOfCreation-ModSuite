using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestReplayWindowIntegrationTests
{
    [Fact]
    public void PendingPublicationFinishesItsReplayFooterBeforeTheWindowFreezes()
    {
        var traceSession = new ServiceCycleTraceSessionId(907);
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(
                true,
                byteCapacity: 64 * 1024,
                recordCapacity: 64,
                cycleFooterCapacity: 16));
        var capturePort = new ContendedFruitCapture();
        var definition = AutoHarvestService.Define(capturePort, new CommittingActions());
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(7),
            new ThreadSafeTestClock(100));
        using var registration = registry.RegisterReplay(definition, Configuration(), recording);
        registry.Seal();
        var semantic = new ServiceCycleSemanticRecorder(traceSession, 256, 1);
        using var pump = new SuiteFramePump(registry, semantic);
        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);
        var exporter = new CapturingExporter(source, recording);
        using var replayCapture = new AutomataReplayCapture(
            recording,
            exporter,
            new AutomataReplayWindow(source, pump),
            new AutomataReplayTestObserver(),
            captureLimit: 200,
            failureLimit: 250);
        using var contention = new HandoffGateContention(registration.Runner);
        capturePort.BeforeReturn = contention.Acquire;

        Assert.Equal(1, pump.PumpFrame(1).CapturesAttempted);
        replayCapture.ObserveLifecycleBoundary();
        Assert.False(recording.RecordingAdmissionClosed);

        contention.Release();
        capturePort.BeforeReturn = null;
        replayCapture.ObserveFrame(pump.PumpFrame(2));
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        replayCapture.ObserveFrame(pump.PumpFrame(3));
        replayCapture.ObserveFrame(pump.PumpFrame(4));
        replayCapture.ObserveFrame(pump.PumpFrame(5));

        var artifact = Assert.IsType<ServiceCycleReplayArtifactDocument>(exporter.Artifact);
        Assert.True(recording.RecordingAdmissionClosed);
        Assert.True(artifact.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.Complete, artifact.Eligibility);
        Assert.Equal(1, artifact.CycleCount);
    }

    private static AutomataConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: false,
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

    private sealed class ContendedFruitCapture : IAutoHarvestCycleCapturePort
    {
        internal Action? BeforeReturn { get; set; }

        public AutoHarvestCycleCaptureDisposition Capture(
            in AutomataConfiguration config,
            LifecycleGeneration lifecycle,
            out AutoHarvestCycleFrame frame)
        {
            var facts = new AutoHarvestPairFacts(
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified);
            frame = new AutoHarvestCycleFrame(
                AutoHarvestPairCapture.Captured(AutoHarvestPair.FruitTree, facts),
                AutoHarvestPairCapture.NotSelected(AutoHarvestPair.TreasureTree),
                ownsActionFamily: true);
            BeforeReturn?.Invoke();
            return AutoHarvestCycleCaptureDisposition.Captured;
        }
    }

    private sealed class CommittingActions : IAutoHarvestCycleActionPort
    {
        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in AutomataConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
    }

    private sealed class CapturingExporter : IAutomataReplayExportPort
    {
        private readonly ServiceCycleSemanticTraceSource _source;
        private readonly ServiceCycleReplaySession _recording;

        internal CapturingExporter(
            ServiceCycleSemanticTraceSource source,
            ServiceCycleReplaySession recording)
        {
            _source = source;
            _recording = recording;
        }

        internal ServiceCycleReplayArtifactDocument? Artifact { get; private set; }

        public AutomataReplayExportStepResult ContinueSnapshot()
        {
            var events = new ServiceCycleSemanticEvent[_source.Capacity];
            var drain = _source.DrainSince(default, events);
            Assert.False(drain.HasMore);
            Assert.True(_recording.TryReadSnapshot(out var snapshot));
            var buffer = new byte[ServiceCycleReplayArtifactCodec.GetMaximumEncodedLength(
                _source.Capacity,
                _recording)];
            var written = ServiceCycleReplayArtifactCodec.Encode(
                drain.Dropped,
                events.AsSpan(0, drain.Copied),
                _recording,
                in snapshot,
                buffer);
            Artifact = ServiceCycleReplayArtifactCodec.Decode(buffer.AsSpan(0, written));
            return AutomataReplayExportStepResult.Accepted;
        }

        public void Stop() { }
    }
}
