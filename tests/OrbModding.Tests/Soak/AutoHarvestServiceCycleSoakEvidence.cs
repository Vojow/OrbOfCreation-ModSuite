using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbAutomata.Tests.Soak;

public sealed class AutoHarvestServiceCycleSoakEvidence
{
    private const int CycleCount = 5_000;
    private static readonly MonotonicDuration Step =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));

    [Fact]
    public void SameInstanceSurvivesConfigurationAndLifecycleChurn()
    {
        var clock = new ThreadSafeTestClock(100);
        var capture = new ReadyCapture();
        var actions = new CommittingActions();
        var definition = AutoHarvestService.Define(capture, actions);
        using var registry = new ServiceCycleRegistry(1, clock);
        var recording = new ServiceCycleReplaySession(
            new ServiceCycleTraceSessionId(830),
            new ServiceCycleReplaySessionOptions(false, 0, 0, 0));
        using var registration = registry.RegisterReplay(
            definition,
            Configuration(Step),
            recording,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var deadline = Stopwatch.GetTimestamp() + 20 * Stopwatch.Frequency;
        var frame = 0L;
        var lifecycle = 2UL;
        var maximumLivePositions = 1;
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(lifecycle)));
        maximumLivePositions = Math.Max(
            maximumLivePositions,
            registration.Slot.LifecycleSnapshot.LivePositionCount);

        for (var cycle = 1; cycle <= CycleCount; cycle++)
        {
            RunCycle(pump, registration, clock, ref frame, deadline);
            if (cycle == CycleCount / 3)
                PublishConfiguration(registration, MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(750)));
            if (cycle == CycleCount * 2 / 3)
                PublishConfiguration(registration, MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(500)));
            if (cycle % 500 != 0 || cycle == CycleCount) continue;

            lifecycle++;
            Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(lifecycle)));
            maximumLivePositions = Math.Max(
                maximumLivePositions,
                registration.Slot.LifecycleSnapshot.LivePositionCount);
        }

        Assert.Equal(CycleCount / 2, actions.FruitCount);
        Assert.Equal(CycleCount / 2, actions.TreasureCount);
        Assert.Equal(CycleCount, capture.CaptureCount);
        Assert.Equal(3UL, registration.Configuration.ReadLatest().Generation.Value);
        Assert.InRange(maximumLivePositions, 1, 2);
        Assert.Equal(1, registration.Slot.LifecycleSnapshot.LivePositionCount);
        Assert.Equal(lifecycle, registration.Runner.Lifecycle.Value);
    }

    private static void RunCycle(
        SuiteFramePump pump,
        ServiceCycleReplayRegistration<
            AutoHarvestCycleFrame,
            AutomataConfiguration,
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        ThreadSafeTestClock clock,
        ref long frame,
        long deadline)
    {
        clock.Advance(Step);
        Assert.Equal(1, pump.PumpFrame(++frame).CapturesAttempted);
        Assert.True(registration.WaitForResponseReady(Remaining(deadline)));
        Assert.Equal(1, pump.PumpFrame(++frame).ResponsesAcquired);
        Assert.Equal(1, pump.PumpFrame(++frame).ActionsAttempted);
        pump.PumpFrame(++frame);
        WaitForCleanup(registration, deadline);
    }

    private static void PublishConfiguration(
        ServiceCycleReplayRegistration<
            AutoHarvestCycleFrame,
            AutomataConfiguration,
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        MonotonicDuration interval) =>
        Assert.True(registration.Configuration.CompleteSave(
            ConfigurationSaveResult<AutomataConfiguration>.Saved(Configuration(interval))));

    private static void WaitForCleanup(
        ServiceCycleReplayRegistration<
            AutoHarvestCycleFrame,
            AutomataConfiguration,
            AutoHarvestCycleState,
            AutoHarvestCycleAction> registration,
        long deadline)
    {
        var spin = new SpinWait();
        while (registration.Runner.Snapshot.Handoff.CleanupPending)
        {
            if (Stopwatch.GetTimestamp() >= deadline)
                throw new TimeoutException("Auto Harvest action cleanup did not settle.");
            spin.SpinOnce();
        }
    }

    private static TimeSpan Remaining(long deadline)
    {
        var ticks = deadline - Stopwatch.GetTimestamp();
        if (ticks <= 0) throw new TimeoutException("Auto Harvest soak exceeded its deadline.");
        return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
    }

    private static AutomataConfiguration Configuration(MonotonicDuration interval) => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: true,
        interval);

    private sealed class ReadyCapture : IAutoHarvestCycleCapturePort
    {
        public int CaptureCount { get; private set; }

        public bool TryCapture(
            in AutomataConfiguration config,
            LifecycleGeneration lifecycle,
            out AutoHarvestCycleFrame frame)
        {
            CaptureCount++;
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
                AutoHarvestPairCapture.Captured(AutoHarvestPair.TreasureTree, facts),
                ownsActionFamily: true);
            return true;
        }
    }

    private sealed class CommittingActions : IAutoHarvestCycleActionPort
    {
        public int FruitCount { get; private set; }
        public int TreasureCount { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in AutomataConfiguration config,
            in ServiceActionContext context)
        {
            if (action.Pair == AutoHarvestPair.FruitTree) FruitCount++;
            else TreasureCount++;
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
        }
    }
}
