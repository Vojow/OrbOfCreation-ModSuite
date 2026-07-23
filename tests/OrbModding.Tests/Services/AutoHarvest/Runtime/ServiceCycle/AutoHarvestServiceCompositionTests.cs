using System;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestServiceCompositionTests
{
    [Fact]
    public void CommonPumpExecutesTheReplayableServiceThroughMainThreadPorts()
    {
        var ownerThread = Thread.CurrentThread.ManagedThreadId;
        var capture = new CapturePort(ownerThread);
        var actions = new ActionPort(ownerThread);
        var definition = AutoHarvestService.Define(capture, actions);
        var config = Configuration();
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        var session = new ServiceCycleReplaySession(
            new ServiceCycleTraceSessionId(1),
            new ServiceCycleReplaySessionOptions(false, 0, 0, 0));
        using var registration = registry.RegisterReplay(definition, config, session);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        pump.PumpFrame(1);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.Equal(1, actions.ExecutionCount);
        Assert.Equal(AutoHarvestPair.FruitTree, actions.LastPair);
        Assert.True(capture.CaptureCount > 0);
    }

    private static AutomataConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: false,
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(10)));

    private sealed class CapturePort : IAutoHarvestCycleCapturePort
    {
        private readonly int _ownerThread;

        public CapturePort(int ownerThread) => _ownerThread = ownerThread;
        public int CaptureCount { get; private set; }

        public AutoHarvestCycleCaptureDisposition Capture(
            in AutomataConfiguration config,
            LifecycleGeneration lifecycle,
            out AutoHarvestCycleFrame frame)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            CaptureCount++;
            frame = ReadyFruitFrame();
            return AutoHarvestCycleCaptureDisposition.Captured;
        }
    }

    private sealed class ActionPort : IAutoHarvestCycleActionPort
    {
        private readonly int _ownerThread;

        public ActionPort(int ownerThread) => _ownerThread = ownerThread;
        public int ExecutionCount { get; private set; }
        public AutoHarvestPair LastPair { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in AutomataConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            LastPair = action.Pair;
            var call = new NativeMutationCallOutcome(1, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call));
        }
    }

    private static AutoHarvestCycleFrame ReadyFruitFrame()
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
        return new AutoHarvestCycleFrame(
            AutoHarvestPairCapture.Captured(AutoHarvestPair.FruitTree, facts),
            AutoHarvestPairCapture.NotSelected(AutoHarvestPair.TreasureTree),
            ownsActionFamily: true);
    }
}
