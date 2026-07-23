using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.ServiceCycleTrace;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestFeatureReplayTests
{
    private static readonly TimeSpan WorkerReadyDeadline = TimeSpan.FromMilliseconds(250);

    [Theory]
    [InlineData(0, "Fruit tree")]
    [InlineData(1, "Treasure tree")]
    public void RecordedCommonCycleReplaysAndReportsTheAutoHarvestPair(
        int pairValue,
        string expectedAction)
    {
        var pair = (AutoHarvestPair)pairValue;
        var artifact = CaptureCommittedCycle(pair, overwriteSemanticTrace: false);
        var report = ServiceCycleTraceReport.Render(
            "auto-harvest.oscr",
            artifact,
            ServiceCycleTraceProfile.AutoHarvest);
        var genericReport = ServiceCycleTraceReport.Render("auto-harvest.oscr", artifact);
        var replayFactory = new AutoHarvestReplayExecutionFactory();
        var replay = new ServiceCycleReplayExecutionRegistration<
            AutoHarvestCycleFrame,
            AutomataConfiguration,
            AutoHarvestCycleState,
            AutoHarvestCycleAction,
            AutoHarvestCycleInputRecord,
            AutoHarvestStateRecord,
            AutoHarvestActionRecord>(1, replayFactory);
        var evaluatorResult = replay.VerifyEvaluator(artifact);
        var productionResult = ServiceCycleReplayProductionDriver.Run(
            artifact,
            replay,
            replayFactory,
            TimeSpan.FromMilliseconds(500));

        Assert.True(artifact.IsComplete);
        Assert.Equal(1, artifact.CycleCount);
        Assert.Contains("## Auto Harvest cycle timing", report);
        Assert.Contains("- Feature profile: Auto Harvest (explicitly selected)", report);
        Assert.Contains(
            $"| 7 | 1 | {expectedAction} | Committed | 0.000 | 0.000 | 10.000 | 1.000 | 11.000 |",
            report);
        Assert.Contains("Publish-to-action is elapsed wall time", report);
        Assert.DoesNotContain("Auto Harvest cycle timing", genericReport);
        Assert.True(evaluatorResult.Succeeded);
        Assert.Equal(1, evaluatorResult.CompletedCycles);
        Assert.True(productionResult.Succeeded);
        Assert.Equal(1, productionResult.CompletedCycles);
    }

    [Fact]
    public void IncompleteSemanticJoinSuppressesAllAutoHarvestTiming()
    {
        var artifact = CaptureCommittedCycle(
            AutoHarvestPair.FruitTree,
            overwriteSemanticTrace: true);

        var report = ServiceCycleTraceReport.Render(
            "incomplete-auto-harvest.oscr",
            artifact,
            ServiceCycleTraceProfile.AutoHarvest);

        Assert.Equal(1, artifact.CycleCount);
        Assert.False(artifact.GetCycle(0).IsComplete);
        Assert.Matches(
            @"\| 7 \| 1 \| Fruit tree \| Unavailable \([^)]+\) \| — \| — \| — \| — \| — \|",
            report);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OneServiceSemanticOperationsFitTheFinalWindowReserve(int actionOutcome)
    {
        var traceSession = new ServiceCycleTraceSessionId((ulong)(811 + actionOutcome));
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(
                true,
                byteCapacity: 64 * 1024,
                recordCapacity: 64,
                cycleFooterCapacity: 16));
        var semantic = new ServiceCycleSemanticRecorder(traceSession, 512, 1);
        var definition = AutoHarvestService.Define(
            new ReadyPairCapture(AutoHarvestPair.FruitTree),
            new FixedOutcomeActions(actionOutcome));
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(7),
            new ThreadSafeTestClock(100));
        using var registration = registry.RegisterReplay(definition, Configuration(), recording);
        registry.Seal();
        Assert.True(registration.Slot.WaitForCurrentWorkerReady(WorkerReadyDeadline));
        using var pump = new SuiteFramePump(registry, semantic);
        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);

        var capture = PumpWithinReserve(pump, source, 1);
        Assert.Equal(1, capture.CapturesAttempted);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(1, PumpWithinReserve(pump, source, 2).ResponsesAcquired);
        Assert.Equal(1, PumpWithinReserve(pump, source, 3).ActionsAttempted);
        PumpWithinReserve(pump, source, 4);
        AssertOperationWithinReserve(source, () => pump.SetEmergencyStop(true));
        AssertOperationWithinReserve(source, () => pump.SetEmergencyStop(false));
        AssertOperationWithinReserve(
            source,
            () => Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(8))));
    }

    [Fact]
    public void EmergencyTransitionAndActiveBatchRejectionShareOneReservedFrame()
    {
        var traceSession = new ServiceCycleTraceSessionId(814);
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(
                true,
                byteCapacity: 64 * 1024,
                recordCapacity: 64,
                cycleFooterCapacity: 16));
        var semantic = new ServiceCycleSemanticRecorder(traceSession, 512, 1);
        var definition = AutoHarvestService.Define(
            new ReadyPairCapture(AutoHarvestPair.FruitTree),
            new CommittingActions());
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(7),
            new ThreadSafeTestClock(100));
        using var registration = registry.RegisterReplay(definition, Configuration(), recording);
        registry.Seal();
        Assert.True(registration.Slot.WaitForCurrentWorkerReady(WorkerReadyDeadline));
        using var pump = new SuiteFramePump(registry, semantic);
        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);

        Assert.Equal(1, PumpWithinReserve(pump, source, 1).CapturesAttempted);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(1, PumpWithinReserve(pump, source, 2).ResponsesAcquired);

        var before = source.Count;
        pump.SetEmergencyStop(true);
        pump.PumpFrame(3);

        Assert.InRange(
            source.Count - before,
            0,
            AutoHarvestServiceCycleFactory.ReplayMaximumSemanticEventsPerFrame);
    }

    private static SuiteFramePumpReport PumpWithinReserve(
        SuiteFramePump pump,
        ServiceCycleSemanticTraceSource source,
        long frame)
    {
        var before = source.Count;
        var report = pump.PumpFrame(frame);
        Assert.InRange(
            source.Count - before,
            0,
            AutoHarvestServiceCycleFactory.ReplayMaximumSemanticEventsPerFrame);
        return report;
    }

    private static void AssertOperationWithinReserve(
        ServiceCycleSemanticTraceSource source,
        Action operation)
    {
        var before = source.Count;
        operation();
        Assert.InRange(
            source.Count - before,
            0,
            AutoHarvestServiceCycleFactory.ReplayMaximumSemanticEventsPerFrame);
    }

    private static ServiceCycleReplayArtifactDocument CaptureCommittedCycle(
        AutoHarvestPair pair,
        bool overwriteSemanticTrace)
    {
        var traceSession = new ServiceCycleTraceSessionId(810);
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(
                true,
                byteCapacity: 64 * 1024,
                recordCapacity: 64,
                cycleFooterCapacity: 16));
        var semantic = new ServiceCycleSemanticRecorder(traceSession, 256, 1);
        var clock = new ThreadSafeTestClock(100);
        var definition = AutoHarvestService.Define(
            new ReadyPairCapture(pair),
            new CommittingActions(() => clock.Advance(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(1)))));
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(7),
            clock);
        using var registration = registry.RegisterReplay(definition, Configuration(pair), recording);
        registry.Seal();
        Assert.True(registration.Slot.WaitForCurrentWorkerReady(WorkerReadyDeadline));
        using var pump = new SuiteFramePump(registry, semantic);

        Assert.Equal(1, pump.PumpFrame(1).CapturesAttempted);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(1, pump.PumpFrame(2).ResponsesAcquired);
        clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)));
        Assert.Equal(1, pump.PumpFrame(3).ActionsAttempted);
        if (overwriteSemanticTrace)
            for (var frame = 4; frame < 304; frame++) pump.PumpFrame(frame);
        return CaptureArtifact(pump, recording, requireCompleteTrace: !overwriteSemanticTrace);
    }

    private static ServiceCycleReplayArtifactDocument CaptureArtifact(
        SuiteFramePump pump,
        ServiceCycleReplaySession recording,
        bool requireCompleteTrace = true)
    {
        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);
        var events = new ServiceCycleSemanticEvent[source.Capacity];
        var drain = source.DrainSince(default, events);
        if (requireCompleteTrace) Assert.True(drain.IsComplete);
        Assert.False(drain.HasMore);
        Assert.True(recording.TryReadSnapshot(out var snapshot));
        var buffer = new byte[ServiceCycleReplayArtifactCodec.GetMaximumEncodedLength(
            source.Capacity,
            recording)];
        var written = ServiceCycleReplayArtifactCodec.Encode(
            drain.Dropped,
            events.AsSpan(0, drain.Copied),
            recording,
            in snapshot,
            buffer);
        return ServiceCycleReplayArtifactCodec.Decode(buffer.AsSpan(0, written));
    }

    private static AutomataConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: false,
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

    private static AutomataConfiguration Configuration(AutoHarvestPair pair) => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: pair == AutoHarvestPair.FruitTree,
        treasureSelected: pair == AutoHarvestPair.TreasureTree,
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));

    private sealed class ReadyPairCapture : IAutoHarvestCycleCapturePort
    {
        private readonly AutoHarvestPair _pair;

        internal ReadyPairCapture(AutoHarvestPair pair) => _pair = pair;

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
                _pair == AutoHarvestPair.FruitTree
                    ? AutoHarvestPairCapture.Captured(AutoHarvestPair.FruitTree, facts)
                    : AutoHarvestPairCapture.NotSelected(AutoHarvestPair.FruitTree),
                _pair == AutoHarvestPair.TreasureTree
                    ? AutoHarvestPairCapture.Captured(AutoHarvestPair.TreasureTree, facts)
                    : AutoHarvestPairCapture.NotSelected(AutoHarvestPair.TreasureTree),
                ownsActionFamily: true);
            return AutoHarvestCycleCaptureDisposition.Captured;
        }
    }

    private sealed class CommittingActions : IAutoHarvestCycleActionPort
    {
        private readonly Action? _beforeCommit;

        internal CommittingActions(Action? beforeCommit = null) => _beforeCommit = beforeCommit;

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in AutomataConfiguration config,
            in ServiceActionContext context)
        {
            _beforeCommit?.Invoke();
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
        }
    }

    private sealed class FixedOutcomeActions : IAutoHarvestCycleActionPort
    {
        private readonly int _outcome;

        internal FixedOutcomeActions(int outcome) => _outcome = outcome;

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in AutomataConfiguration config,
            in ServiceActionContext context) => _outcome switch
            {
                0 => ServiceActionResult.Committed(
                    CommonActionResultCodes.Committed,
                    ServiceNativeMutationEvidence.Observed(
                        NativeMutationOutcome.Verified,
                        new NativeMutationCallOutcome(1, 1, 1))),
                1 => ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected),
                _ => ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault),
            };
    }
}
