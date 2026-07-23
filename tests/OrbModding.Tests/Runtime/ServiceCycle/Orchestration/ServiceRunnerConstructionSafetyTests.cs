using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCyclePumpTestWait;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class ServiceRunnerConstructionSafetyTests
{
    [Fact]
    public void ReplacementConstructionFailureBacksOffAndCoalescesNewestGeneration()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.backoff");
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        definition.FailNextWorkerFactories(2);

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(110, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(3)));
        pump.PumpFrame(1);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);

        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(2);
        Assert.Equal(2, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(130, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        clock.Advance(new MonotonicDuration(20));
        pump.PumpFrame(3);
        Assert.Equal((ulong)3, registration.Runner.Lifecycle.Value);
        Assert.Equal(3, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.False(registration.LifecycleSnapshot.ConstructionFault.IsValid);
    }

    [Fact]
    public void PumpEpochAllowsOnlyOneConstructionAttemptWhenFactoryAdvancesPastRetryDue()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.epoch-backoff");
        using var firstGate = definition.BlockEvaluation(1);
        using var secondGate = definition.BlockEvaluation(2);
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        PumpUntil(pump, ref frame, () => firstGate.Entered.IsSet);
        var first = registration.Runner;
        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        PumpUntil(pump, ref frame, () => secondGate.Entered.IsSet);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(3));

        firstGate.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => first.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            TimeSpan.FromSeconds(2)));
        var advanced = false;
        definition.WorkerDefinitionFactoryCallback = () =>
        {
            if (advanced) return;
            advanced = true;
            clock.Advance(new MonotonicDuration(10));
        };
        definition.FailNextWorkerFactories(1);
        var attemptsBeforeFrame = registration.LifecycleSnapshot.ConstructionAttemptCount;

        var failedFrame = pump.PumpFrame(frame++);
        Assert.Equal(1, failedFrame.LifecyclePositionTransitions);
        Assert.Equal(attemptsBeforeFrame + 1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(110, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        Assert.Equal(110, clock.Now.Ticks);
        Assert.Equal(3, definition.WorkerDefinitionCreateCount);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);

        var retryFrame = pump.PumpFrame(frame++);
        Assert.Equal(1, retryFrame.LifecyclePositionTransitions);
        Assert.Equal(attemptsBeforeFrame + 2, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal((ulong)3, registration.Runner.Lifecycle.Value);
        Assert.Equal(4, definition.WorkerDefinitionCreateCount);
        secondGate.Release.Set();
    }

    [Fact]
    public void FrameConstructionFailureIsAtomicAndRetriesAtDueTime()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.frame-failure");
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        definition.FailNextFrameFactories(1);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.True(registration.LifecycleSnapshot.ConstructionFault.IsValid);
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);
        clock.Advance(new MonotonicDuration(9));
        pump.PumpFrame(1);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        clock.Advance(new MonotonicDuration(1));
        pump.PumpFrame(2);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
        Assert.Equal(3, definition.WorkerDefinitionCreateCount);
    }

    [Fact]
    public void ThreadStartConstructionFailureIsAtomicAndRetriesAtDueTime()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new FailSecondThreadStart();
        using var registry = new ServiceCycleRegistry(
            1, clock, measureWorkerAllocations: false, workerStarter: starter);
        var definition = new LifecycleServiceDefinition("lifecycle.thread-start-failure");
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.Equal(2, starter.AttemptCount);
        Assert.True(registration.LifecycleSnapshot.ConstructionFault.IsValid);
        Assert.Equal(110, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(1);
        Assert.Equal(3, starter.AttemptCount);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
    }

    [Fact]
    public void ReplacementAllocatesFreshRunnerResourceGraphAndLifecycleState()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.resources");
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        PumpUntil(pump, ref frame, () => definition.StateSerial(1) != 0);
        var oldRunner = registration.Runner;
        var old = oldRunner.ResourceIdentity;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        var current = registration.Runner;
        var fresh = current.ResourceIdentity;
        Assert.NotSame(old.WorkerDefinition, fresh.WorkerDefinition);
        Assert.NotSame(old.Frame, fresh.Frame);
        Assert.NotSame(old.ActionStore, fresh.ActionStore);
        Assert.NotSame(old.Handoff, fresh.Handoff);
        Assert.NotSame(old.Worker, fresh.Worker);
        Assert.NotSame(old.MainState, fresh.MainState);
        Assert.NotSame(old.StartCoordinator, fresh.StartCoordinator);
        Assert.NotSame(old.BatchCompletion, fresh.BatchCompletion);
        PumpUntil(pump, ref frame, () => definition.StateSerial(2) != 0);
        Assert.NotEqual(definition.StateSerial(1), definition.StateSerial(2));
    }

    [Fact]
    public void LiveFrameAliasingIsRejectedBeforeSecondWorkerStarts()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.alias");
        using var gate = definition.BlockEvaluation(1);
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        PumpUntil(pump, ref frame, () => gate.Entered.IsSet);
        definition.ReuseFrame = true;

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        var snapshot = registration.LifecycleSnapshot;
        Assert.True(snapshot.ConstructionFault.IsValid);
        Assert.Equal(ServiceFaultCategory.LifecycleConstruction, snapshot.ConstructionFault.Category);
        Assert.Equal(1, snapshot.LivePositionCount);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);
        gate.Release.Set();
    }

    [Fact]
    public void WorkerDefinitionAliasIsRejectedBeforeFrameAllocationOrThreadStart()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(1, clock, false, starter);
        var definition = new LifecycleServiceDefinition("lifecycle.worker-alias");
        using var gate = definition.BlockEvaluation(1);
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        PumpUntil(pump, ref frame, () => gate.Entered.IsSet);
        definition.ReuseWorkerDefinition = true;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);
        Assert.Equal(1, definition.FrameCreateCount);
        Assert.Equal(1, starter.AttemptCount);
        pump.PumpFrame(frame++);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(frame++);
        Assert.Equal(2, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(3, definition.WorkerDefinitionCreateCount);
        Assert.Equal(1, definition.FrameCreateCount);
        Assert.Equal(1, starter.AttemptCount);
        gate.Release.Set();
    }

    [Fact]
    public void CaptureCannotReplaceReferenceFrameWithAnotherServicesLiveFrame()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var firstDefinition = new LifecycleServiceDefinition("lifecycle.frame-owner");
        var secondDefinition = new LifecycleServiceDefinition("lifecycle.frame-replacer");
        using var first = registry.Register(
            firstDefinition, new LifecycleConfig(1), new LifecycleGeneration(1));
        using var second = registry.Register(
            secondDefinition, new LifecycleConfig(1), new LifecycleGeneration(1));
        var secondOriginalFrame = second.Runner.ResourceIdentity.Frame;
        secondDefinition.CaptureReplacementFrame =
            (LifecycleFrame)first.Runner.ResourceIdentity.Frame!;
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        pump.PumpFrame(1);
        Assert.Equal(ServiceHandoffPhase.Empty, second.Runner.HandoffPhaseHint);
        Assert.Equal(ServiceFaultCategory.Capture, second.Runner.Snapshot.Fault.Category);
        Assert.Same(secondOriginalFrame, second.Runner.ResourceIdentity.Frame);
        Assert.Equal(0, secondDefinition.EvaluationCount(1));
    }

    [Fact]
    public void RegistryRejectsCrossServiceWorkerDefinitionAndFrameAliases()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(3, clock);
        var ownerDefinition = new LifecycleServiceDefinition("lifecycle.cross-owner");
        using var owner = registry.Register(
            ownerDefinition, new LifecycleConfig(1), new LifecycleGeneration(1));

        var workerAlias = new LifecycleServiceDefinition("lifecycle.cross-worker")
        {
            SharedWorkerDefinition =
                (IServiceCycleWorkerDefinition<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>)
                owner.Runner.ResourceIdentity.WorkerDefinition,
        };
        Assert.Throws<ServiceRunnerResourceAliasingException>(() => registry.Register(
            workerAlias, new LifecycleConfig(1), new LifecycleGeneration(1)));

        var frameAlias = new LifecycleServiceDefinition("lifecycle.cross-frame")
        {
            SharedFrame = (LifecycleFrame)owner.Runner.ResourceIdentity.Frame!,
        };
        Assert.Throws<ServiceRunnerResourceAliasingException>(() => registry.Register(
            frameAlias, new LifecycleConfig(1), new LifecycleGeneration(1)));
        Assert.Equal(0, frameAlias.FrameReleaseCount);
        Assert.Equal((ulong)1, owner.Runner.Lifecycle.Value);
    }

    [Fact]
    public void CallbackCanObserveConcurrentStateFactoryFailFastWithoutDeadlock()
    {
        var clock = new ThreadSafeTestClock(100);
        var ledger = new ServiceResourceClaimLedger(1);
        var definition = new LifecycleServiceDefinition("lifecycle.frame-callback-state-claim");
        using var configuration = new ServiceConfigurationPublisher<LifecycleConfig>(
            new LifecycleConfig(1));
        using var completed = new ManualResetEventSlim(false);
        var stateAdmission = default(ServiceResourceClaimResult);
        definition.FrameFactoryCallback = () =>
        {
            var claimant = new Thread(() =>
            {
                stateAdmission = ledger.TryClaim(
                    new object(),
                    ServiceResourceRole.State,
                    out _);
                completed.Set();
            }) { IsBackground = true };
            claimant.Start();
            Assert.True(completed.Wait(TimeSpan.FromSeconds(2)));
        };

        using var runner = ServiceRunnerFactory<
            LifecycleFrame,
            LifecycleConfig,
            LifecycleState,
            LifecycleAction>.CreateRequired(
            definition,
            configuration,
            new LifecycleGeneration(1),
            definition.ServiceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            clock,
            measureWorkerAllocations: false,
            resourceClaims: ledger);

        Assert.True(completed.IsSet);
        Assert.Equal(ServiceResourceClaimResult.Contended, stateAdmission);
        Assert.Equal(1, definition.FrameCreateCount);
        Assert.Equal(2, ledger.LiveClaimCount);
    }

    [Fact]
    public void UniqueReservedFrameRollsBackExactlyOnceAfterLaterConstructionFailure()
    {
        var starter = new FailSecondThreadStart();
        using var registry = new ServiceCycleRegistry(
            2,
            new ThreadSafeTestClock(100),
            false,
            starter);
        using var owner = registry.Register(
            new LifecycleServiceDefinition("lifecycle.frame-rollback-owner"),
            new LifecycleConfig(1),
            new LifecycleGeneration(1));
        var definition = new LifecycleServiceDefinition("lifecycle.frame-rollback-contender");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition,
            new LifecycleConfig(1),
            new LifecycleGeneration(1)));
        Assert.Equal(1, definition.FrameCreateCount);
        Assert.Equal(1, definition.FrameReleaseCount);
        Assert.Equal(1, registry.Count);
        Assert.Equal(2, starter.AttemptCount);

        using var registration = registry.Register(
            definition,
            new LifecycleConfig(2),
            new LifecycleGeneration(1));
        Assert.Equal(2, definition.FrameCreateCount);
        Assert.Equal(1, definition.FrameReleaseCount);
        Assert.Equal(3, starter.AttemptCount);
        Assert.Equal((ulong)1, registration.Runner.Lifecycle.Value);
    }

    [Fact]
    public void CapacityPreventsReferenceFrameFactoryCall()
    {
        var clock = new ThreadSafeTestClock(100);
        var ledger = new ServiceResourceClaimLedger(1);
        for (var index = 0; index < ServiceResourceClaimLedger.ClaimsPerService - 1; index++)
            ledger.Claim(new object(), ServiceResourceRole.State);
        var definition = new LifecycleServiceDefinition("lifecycle.frame-reservation-capacity");
        using var configuration = new ServiceConfigurationPublisher<LifecycleConfig>(
            new LifecycleConfig(1));

        Assert.Throws<InvalidOperationException>(() => ServiceRunnerFactory<
            LifecycleFrame,
            LifecycleConfig,
            LifecycleState,
            LifecycleAction>.CreateRequired(
            definition,
            configuration,
            new LifecycleGeneration(1),
            definition.ServiceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            clock,
            measureWorkerAllocations: false,
            resourceClaims: ledger));
        Assert.Equal(0, definition.FrameCreateCount);
        Assert.Equal(0, definition.FrameReleaseCount);
        Assert.Equal(ledger.Capacity - 1, ledger.LiveClaimCount);
    }

    [Fact]
    public void CapacityPreventsWorkerDefinitionFactoryCall()
    {
        var clock = new ThreadSafeTestClock(100);
        var ledger = new ServiceResourceClaimLedger(1);
        for (var index = 0; index < ledger.Capacity; index++)
            ledger.Claim(new object(), ServiceResourceRole.State);
        var definition = new LifecycleServiceDefinition("lifecycle.worker-capacity");
        using var configuration = new ServiceConfigurationPublisher<LifecycleConfig>(
            new LifecycleConfig(1));

        Assert.Throws<InvalidOperationException>(() => ServiceRunnerFactory<
            LifecycleFrame,
            LifecycleConfig,
            LifecycleState,
            LifecycleAction>.CreateRequired(
            definition,
            configuration,
            new LifecycleGeneration(1),
            definition.ServiceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            clock,
            measureWorkerAllocations: false,
            resourceClaims: ledger));
        Assert.Equal(0, definition.WorkerDefinitionCreateCount);
        Assert.Equal(0, definition.FrameCreateCount);
        Assert.Equal(ledger.Capacity, ledger.LiveClaimCount);
    }

    [Fact]
    public void FrameFactoryExceptionReleasesReservedSlotForRetry()
    {
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        var definition = new LifecycleServiceDefinition("lifecycle.frame-reservation-callback");
        definition.FrameFactoryCallback = () =>
            throw new InvalidOperationException("synthetic frame callback failure");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition,
            new LifecycleConfig(1),
            new LifecycleGeneration(1)));
        Assert.Equal(1, definition.FrameCreateCount);
        Assert.Equal(0, definition.FrameReleaseCount);
        Assert.Equal(0, registry.Count);

        definition.FrameFactoryCallback = null;
        using var registration = registry.Register(
            definition,
            new LifecycleConfig(2),
            new LifecycleGeneration(1));
        Assert.Equal(2, definition.FrameCreateCount);
        Assert.Equal(0, definition.FrameReleaseCount);
        Assert.Equal((ulong)1, registration.Runner.Lifecycle.Value);
    }

    [Fact]
    public void ConstructionFactoryCannotReenterLifecycleReconciliation()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(1, clock, false, starter);
        var definition = new LifecycleServiceDefinition("lifecycle.factory-reentrant");
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        Exception? rejected = null;
        definition.WorkerDefinitionFactoryCallback = () =>
        {
            try { pump.RequestLifecycleReplacement(new LifecycleGeneration(3)); }
            catch (Exception ex) { rejected = ex; throw; }
        };

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.Equal((ulong)2, registration.LifecycleSnapshot.DesiredLifecycle.Value);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(1, starter.AttemptCount);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);

        definition.WorkerDefinitionFactoryCallback = null;
        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(1);
        Assert.Equal(2, starter.AttemptCount);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
    }

    [Theory]
    [InlineData(false, InitialConstructionMutation.Register)]
    [InlineData(false, InitialConstructionMutation.Seal)]
    [InlineData(false, InitialConstructionMutation.Dispose)]
    [InlineData(false, InitialConstructionMutation.Release)]
    [InlineData(false, InitialConstructionMutation.ReconcileLifecycle)]
    [InlineData(true, InitialConstructionMutation.Register)]
    [InlineData(true, InitialConstructionMutation.Seal)]
    [InlineData(true, InitialConstructionMutation.Dispose)]
    [InlineData(true, InitialConstructionMutation.Release)]
    [InlineData(true, InitialConstructionMutation.ReconcileLifecycle)]
    public void InitialFactoryCannotMutateCompositionAndFailedRegistrationIsRetryable(
        bool fromFrameFactory,
        InitialConstructionMutation mutation)
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(2, clock, false, starter);
        ServiceRegistration<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>? owner = null;
        if (mutation == InitialConstructionMutation.Release)
        {
            owner = registry.Register(
                new LifecycleServiceDefinition("lifecycle.initial-reentrant.release-owner"),
                new LifecycleConfig(1),
                new LifecycleGeneration(1));
        }
        var baselineCount = owner is null ? 0 : 1;
        var definition = new LifecycleServiceDefinition(
            $"lifecycle.initial-reentrant.{fromFrameFactory}.{mutation}")
        {
            ReuseWorkerDefinition = fromFrameFactory,
        };
        Exception? rejected = null;
        var nestedDefinition = new LifecycleServiceDefinition(
            $"lifecycle.initial-reentrant.nested.{fromFrameFactory}.{mutation}");
        Action callback = () =>
        {
            try
            {
                switch (mutation)
                {
                    case InitialConstructionMutation.Register:
                        registry.Register(
                            nestedDefinition,
                            new LifecycleConfig(2),
                            new LifecycleGeneration(1));
                        break;
                    case InitialConstructionMutation.Seal:
                        registry.Seal();
                        break;
                    case InitialConstructionMutation.Dispose:
                        registry.Dispose();
                        break;
                    case InitialConstructionMutation.Release:
                        owner!.Dispose();
                        break;
                    case InitialConstructionMutation.ReconcileLifecycle:
                        registry.ReconcileLifecycle(clock.Now);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            }
            catch (Exception ex)
            {
                rejected = ex;
                throw;
            }
        };
        if (fromFrameFactory)
            definition.FrameFactoryCallback = callback;
        else
            definition.WorkerDefinitionFactoryCallback = callback;

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition,
            new LifecycleConfig(1),
            new LifecycleGeneration(1)));

        Assert.IsType<InvalidOperationException>(rejected);
        Assert.Equal(baselineCount, registry.Count);
        Assert.Equal(baselineCount, registry.OrdinalCount);
        Assert.False(registry.IsSealed);
        Assert.Equal(baselineCount, starter.AttemptCount);
        Assert.Equal(fromFrameFactory ? 1 : 0, definition.FrameCreateCount);

        definition.WorkerDefinitionFactoryCallback = null;
        definition.FrameFactoryCallback = null;
        using var registration = registry.Register(
            definition,
            new LifecycleConfig(3),
            new LifecycleGeneration(1));
        Assert.Equal(baselineCount + 1, registry.Count);
        Assert.Equal(baselineCount + 1, registry.OrdinalCount);
        Assert.Equal(baselineCount + 1, starter.AttemptCount);
        Assert.Equal((ulong)1, registration.Runner.Lifecycle.Value);
        owner?.Dispose();
    }

    [Theory]
    [InlineData("worker", "registration")]
    [InlineData("worker", "registry")]
    [InlineData("worker", "pump")]
    [InlineData("worker", "register")]
    [InlineData("worker", "seal")]
    [InlineData("frame", "registration")]
    [InlineData("frame", "registry")]
    [InlineData("frame", "pump")]
    [InlineData("frame", "register")]
    [InlineData("frame", "seal")]
    public void ConstructionCallbacksCannotMutateComposition(
        string factoryStage,
        string mutation)
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        var registry = new ServiceCycleRegistry(2, clock, false, starter);
        var definition = new LifecycleServiceDefinition(
            $"lifecycle.composition-guard.{factoryStage}.{mutation}");
        var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        var pump = new SuiteFramePump(registry);
        Exception? rejected = null;
        Action callback = () =>
        {
            try
            {
                switch (mutation)
                {
                    case "registration":
                        registration.Dispose();
                        break;
                    case "registry":
                        registry.Dispose();
                        break;
                    case "pump":
                        pump.Dispose();
                        break;
                    case "register":
                        registry.Register(
                            new LifecycleServiceDefinition("lifecycle.illegal-register"),
                            new LifecycleConfig(1),
                            new LifecycleGeneration(2));
                        break;
                    case "seal":
                        registry.Seal();
                        break;
                    default:
                        throw new InvalidOperationException("Unknown composition mutation fixture.");
                }
            }
            catch (Exception ex)
            {
                rejected = ex;
            }
        };
        if (factoryStage == "worker")
            definition.WorkerDefinitionFactoryCallback = callback;
        else
            definition.FrameFactoryCallback = callback;

        try
        {
            Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
            Assert.IsType<InvalidOperationException>(rejected);
            Assert.False(pump.IsDisposed);
            Assert.Equal(1, registry.Count);
            Assert.Equal(2, starter.AttemptCount);
            Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
            Assert.Equal(2, registration.LifecycleSnapshot.LivePositionCount);

            definition.WorkerDefinitionFactoryCallback = null;
            definition.FrameFactoryCallback = null;
            registration.Dispose();
            Assert.Equal(0, registry.Count);
            pump.Dispose();
            Assert.True(pump.IsDisposed);
        }
        finally
        {
            definition.WorkerDefinitionFactoryCallback = null;
            definition.FrameFactoryCallback = null;
            try { registration.Dispose(); }
            catch { }
            try { pump.Dispose(); }
            catch { }
            try { registry.Dispose(); }
            catch { }
        }
    }

    public enum InitialConstructionMutation
    {
        Register = 1,
        Seal = 2,
        Dispose = 3,
        Release = 4,
        ReconcileLifecycle = 5,
    }

    private sealed class FailSecondThreadStart : IServiceCycleWorkerStarter
    {
        internal int AttemptCount { get; private set; }

        public void Start(Thread thread)
        {
            AttemptCount++;
            if (AttemptCount == 2)
                throw new InvalidOperationException("synthetic replacement thread-start failure");
            thread.Start();
        }
    }

}
