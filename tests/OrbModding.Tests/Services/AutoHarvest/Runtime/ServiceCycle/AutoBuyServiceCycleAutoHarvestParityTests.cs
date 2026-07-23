using System;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbAutomata.Tests;

[Trait("Category", "HeadlessIntegration")]
public sealed class AutoBuyServiceCycleAutoHarvestParityTests
{
    [Fact]
    public void IndependentServiceCyclePreservesAutoBuyOutputWithoutJoiningLegacyRotation()
    {
        AutoBuyOutcome standalone;
        using (var simulation = NewStorm(null))
        {
            RunStandalone(simulation);
            standalone = Capture(simulation);
        }

        var coordinator = new SuitePerformanceCoordinator(
            new ManualPerformanceClock(),
            softBudgetMilliseconds: 1,
            hardBudgetMilliseconds: 2,
            metricsWindow: 64);
        using var cohosted = NewStorm(coordinator);
        using var harvest = new AutoHarvestHarness(() => cohosted.Frame);
        var framesWhereBothMutated = 0;

        for (var frame = 0; frame < 900; frame++)
        {
            var autoBuyBefore = cohosted.World.TotalPurchaseCalls;
            cohosted.Step(Completions(frame));
            harvest.Tick();
            if (cohosted.World.TotalPurchaseCalls != autoBuyBefore &&
                harvest.MutatedOnFrame(cohosted.Frame))
            {
                framesWhereBothMutated++;
            }
        }

        var together = Capture(cohosted);
        Assert.Equal(standalone, together);
        Assert.True(together.TotalPurchaseCalls >= 416);
        Assert.True(together.QueueCount >= 295);
        Assert.True(framesWhereBothMutated > 0);
        Assert.True(harvest.MutationCount > 100);

        var registrations = coordinator.GetRegistrationSnapshots();
        Assert.Equal(2, registrations.Length);
        Assert.Contains(registrations,
            item => item.WorkName == SuitePerformanceWorkIdentities.AutoBuyEvaluate.WorkName);
        Assert.Contains(registrations,
            item => item.WorkName == SuitePerformanceWorkIdentities.AutoBuyMutation.WorkName);
        Assert.DoesNotContain(registrations,
            item => item.Subsystem == "OrbAutomata.AutoHarvest");
    }

    private static void RunStandalone(AutoBuySimulation simulation)
    {
        for (var frame = 0; frame < 900; frame++)
            simulation.Step(Completions(frame));
    }

    private static int Completions(int frame) =>
        frame >= 400 && (frame - 400) % 4 == 0 ? 1 : 0;

    private static AutoBuySimulation NewStorm(SuitePerformanceCoordinator? coordinator) => new(
        queueCapacity: 304,
        Enumerable.Range(0, 166)
            .Select(index => new SimulatedCandidateSpec(
                $"candidate-{index:000}",
                index % 2 == 0 ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
                baseCost: 1 + index % 7))
            .ToArray(),
        initialResourceQuantity: 1_000_000_000,
        readObservationCostMilliseconds: 0.02,
        purchaseObservationCostMilliseconds: 1.1,
        externalCoordinator: coordinator);

    private static AutoBuyOutcome Capture(AutoBuySimulation simulation) => new(
        simulation.World.QueueCount,
        simulation.World.QueueHighWater,
        simulation.World.TotalSubmitted,
        simulation.World.TotalCompleted,
        simulation.World.TotalPurchaseCalls,
        simulation.World.TotalCandidateEvaluations,
        simulation.Catalog.EvaluationBatches,
        simulation.Metrics.MaximumEvaluationsInFrame);

    private readonly record struct AutoBuyOutcome(
        int QueueCount,
        int QueueHighWater,
        int TotalSubmitted,
        int TotalCompleted,
        int TotalPurchaseCalls,
        int TotalCandidateEvaluations,
        int EvaluationBatches,
        int MaximumEvaluationsInFrame);

    private sealed class AutoHarvestHarness : IDisposable
    {
        private static readonly MonotonicDuration FrameDuration =
            MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(1_100));

        private readonly Func<long> _readFrame;
        private readonly VirtualMonotonicClock _clock = new();
        private readonly IndependentActions _actions;
        private readonly ServiceCycleRegistry _registry;
        private readonly ServiceCycleReplayRegistration<
            AutoHarvestCycleFrame,
            AutomataConfiguration,
            AutoHarvestCycleState,
            AutoHarvestCycleAction> _registration;
        private readonly SuiteFramePump _pump;

        public AutoHarvestHarness(Func<long> readFrame)
        {
            _readFrame = readFrame;
            _actions = new IndependentActions(readFrame);
            var definition = AutoHarvestService.Define(new ReadyFruitCapture(), _actions);
            _registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), _clock);
            _registration = _registry.RegisterReplay(
                definition,
                Configuration(),
                new ServiceCycleReplaySession(
                    new ServiceCycleTraceSessionId(905),
                    new ServiceCycleReplaySessionOptions(false, 0, 0, 0)));
            _registry.Seal();
            _pump = new SuiteFramePump(_registry);
        }

        public int MutationCount => _actions.MutationCount;

        public bool MutatedOnFrame(long frame) => _actions.MutatedOnFrame(frame);

        public void Tick()
        {
            _clock.Advance(FrameDuration);
            var report = _pump.PumpFrame(_readFrame());
            if (report.CapturesAttempted != 0)
                Assert.True(_registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        }

        public void Dispose()
        {
            _pump.Dispose();
            _registration.Dispose();
        }

        private static AutomataConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: true,
            treasureSelected: false,
            FrameDuration);
    }

    private sealed class ReadyFruitCapture : IAutoHarvestCycleCapturePort
    {
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
            return AutoHarvestCycleCaptureDisposition.Captured;
        }
    }

    private sealed class IndependentActions : IAutoHarvestCycleActionPort
    {
        private readonly Func<long> _readFrame;
        private readonly System.Collections.Generic.HashSet<long> _mutationFrames = new();

        public IndependentActions(Func<long> readFrame)
        {
            _readFrame = readFrame;
        }

        public int MutationCount => _mutationFrames.Count;

        public bool MutatedOnFrame(long frame) => _mutationFrames.Contains(frame);

        public ServiceActionResult TryExecute(
            in AutoHarvestCycleAction action,
            in AutomataConfiguration config,
            in ServiceActionContext context)
        {
            _mutationFrames.Add(_readFrame());
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
        }
    }

    private sealed class ManualPerformanceClock : IPerformanceClock
    {
        public long GetTimestamp() => 0;

        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => 0;
    }
}
