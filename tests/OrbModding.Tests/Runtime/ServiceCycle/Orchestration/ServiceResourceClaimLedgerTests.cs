using System;
using System.Diagnostics;
using System.Reflection;
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

public sealed class ServiceResourceClaimLedgerTests
{
    [Fact]
    public void ResourceClaimLedgerIsBoundedAndExactReleaseCannotClearAReusedHole()
    {
        var ledger = new ServiceResourceClaimLedger(1);
        var claims = new ServiceResourceClaim[ledger.Capacity];
        for (var index = 0; index < claims.Length; index++)
        {
            claims[index] = ledger.Claim(
                new object(),
                (ServiceResourceRole)((index % 3) + 1));
        }
        Assert.Equal(ServiceResourceClaimLedger.ClaimsPerService, ledger.Capacity);
        Assert.Equal(ledger.Capacity, ledger.LiveClaimCount);
        Assert.Equal(
            ServiceResourceClaimResult.CapacityExhausted,
            ledger.TryClaim(
                new object(),
                ServiceResourceRole.State,
                out _));

        var stale = claims[0];
        Assert.True(ledger.Release(stale));
        var replacementIdentity = new object();
        var replacement = ledger.Claim(replacementIdentity, ServiceResourceRole.Frame);
        Assert.NotEqual(stale.Token, replacement.Token);
        Assert.False(ledger.Release(stale));
        Assert.True(ledger.Release(claims[1]));
        Assert.Equal(
            ServiceResourceClaimResult.Aliased,
            ledger.TryClaim(
                replacementIdentity,
                ServiceResourceRole.WorkerDefinition,
                out _));
        Assert.Equal(ledger.Capacity - 1, ledger.LiveClaimCount);
        Assert.True(ledger.Release(replacement));
    }

    [Fact]
    public void RegistryRejectsFrameToWorkerCrossRoleAlias()
    {
        using var registry = new ServiceCycleRegistry(2, new ThreadSafeTestClock(100));
        var shared = new CrossRoleResource();
        using var owner = registry.Register(
            new CrossRoleFrameOwnerDefinition("lifecycle.cross-role.frame-owner", shared),
            new CrossRoleConfig(1),
            new LifecycleGeneration(1));
        var contender = new CrossRoleServiceDefinition("lifecycle.cross-role.worker-contender")
        {
            WorkerResource = shared,
        };

        Assert.Throws<ServiceRunnerResourceAliasingException>(() => registry.Register(
            contender,
            new CrossRoleConfig(1),
            new LifecycleGeneration(1)));
        Assert.Equal(1, registry.Count);
        Assert.Same(shared, owner.Runner.ResourceIdentity.Frame);
    }

    [Fact]
    public void RegistryRejectsFrameToStateCrossRoleAlias()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var shared = new CrossRoleResource();
        using var owner = registry.Register(
            new CrossRoleFrameOwnerDefinition("lifecycle.cross-role.frame-state-owner", shared),
            new CrossRoleConfig(1),
            new LifecycleGeneration(1));
        var contenderDefinition = new CrossRoleServiceDefinition(
            "lifecycle.cross-role.state-contender")
        {
            StateResource = shared,
        };
        using var contender = registry.Register(
            contenderDefinition,
            new CrossRoleConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;

        PumpUntil(pump, ref frame, () =>
            contender.Runner.Snapshot.Fault.Category == ServiceFaultCategory.StateFactory,
            clock);
        Assert.Same(shared, owner.Runner.ResourceIdentity.Frame);
    }

    [Fact]
    public void RegistryRejectsWorkerToStateCrossRoleAlias()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var shared = new CrossRoleResource();
        var ownerDefinition = new CrossRoleServiceDefinition(
            "lifecycle.cross-role.worker-state-owner")
        {
            WorkerResource = shared,
            Ready = false,
        };
        var contenderDefinition = new CrossRoleServiceDefinition(
            "lifecycle.cross-role.worker-state-contender")
        {
            StateResource = shared,
        };
        using var owner = registry.Register(
            ownerDefinition,
            new CrossRoleConfig(1),
            new LifecycleGeneration(1));
        using var contender = registry.Register(
            contenderDefinition,
            new CrossRoleConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;

        PumpUntil(pump, ref frame, () =>
            contender.Runner.Snapshot.Fault.Category == ServiceFaultCategory.StateFactory,
            clock);
        Assert.Same(shared, owner.Runner.ResourceIdentity.WorkerDefinition);
    }

    [Fact]
    public void CapacityPreventsReferenceStateFactoryCall()
    {
        var clock = new ThreadSafeTestClock(100);
        var ledger = new ServiceResourceClaimLedger(1);
        for (var index = 0; index < ServiceResourceClaimLedger.ClaimsPerService - 2; index++)
            ledger.Claim(new object(), ServiceResourceRole.State);
        var definition = new CrossRoleServiceDefinition("lifecycle.claim-capacity");
        using var configuration = new ServiceConfigurationPublisher<CrossRoleConfig>(
            new CrossRoleConfig(1));
        using var runner = ServiceRunnerFactory<
            CrossRoleFrame,
            CrossRoleConfig,
            CrossRoleResource,
            CrossRoleAction>.CreateRequired(
            definition,
            configuration,
            new LifecycleGeneration(1),
            definition.ServiceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            clock,
            measureWorkerAllocations: false,
            resourceClaims: ledger);

        Assert.Equal(ledger.Capacity, ledger.LiveClaimCount);
        Assert.True(runner.TryStartCycle(clock.Now).CaptureAttempted);
        Assert.True(SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.ResponseReady,
            TimeSpan.FromSeconds(2)));
        Assert.True(runner.TryAcquireResponse());
        Assert.Equal(ServiceFaultCategory.StateFactory, runner.Snapshot.Fault.Category);
        Assert.Equal(0, definition.StateCreateCount);
        Assert.Equal(0, definition.StateReleaseCount);
        Assert.Equal(ledger.Capacity, ledger.LiveClaimCount);
    }

    [Fact]
    public void TombstonedRegistrationReleasesClaimsForExactResourceReuse()
    {
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(
            1,
            new ThreadSafeTestClock(100),
            false,
            starter);
        var definition = new LifecycleServiceDefinition("lifecycle.claim-tombstone");
        var first = registry.Register(
            definition,
            new LifecycleConfig(1),
            new LifecycleGeneration(1));
        var oldRunner = first.Runner;
        var workerDefinition = oldRunner.ResourceIdentity.WorkerDefinition;
        var frame = (LifecycleFrame)oldRunner.ResourceIdentity.Frame!;

        first.Dispose();
        Assert.True(SpinWait.SpinUntil(
            () => oldRunner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            TimeSpan.FromSeconds(2)));
        definition.SharedWorkerDefinition =
            (IServiceCycleWorkerDefinition<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction>)
            workerDefinition;
        definition.SharedFrame = frame;

        using var second = registry.Register(
            definition,
            new LifecycleConfig(2),
            new LifecycleGeneration(1));
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, registry.OrdinalCount);
        Assert.Equal(0, second.Ordinal);
        Assert.Equal(2, starter.AttemptCount);
        Assert.Same(workerDefinition, second.Runner.ResourceIdentity.WorkerDefinition);
        Assert.Same(frame, second.Runner.ResourceIdentity.Frame);
    }

    [Fact]
    public void RegistryAllowsOnlyOneCrossServiceClaimOfSharedState()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var shared = new LifecycleState(900, 1);
        var firstDefinition = new LifecycleServiceDefinition("lifecycle.state-owner")
        {
            SharedState = shared,
            DefaultWakePolicy = WakePolicy.AfterBatch(new MonotonicDuration(1_000)),
        };
        var secondDefinition = new LifecycleServiceDefinition("lifecycle.state-contender")
        {
            SharedState = shared,
            DefaultWakePolicy = WakePolicy.AfterBatch(new MonotonicDuration(1_000)),
        };
        using var first = registry.Register(
            firstDefinition, new LifecycleConfig(1), new LifecycleGeneration(1));
        using var second = registry.Register(
            secondDefinition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;

        PumpUntil(pump, ref frame, () =>
            first.Runner.Snapshot.Fault.Category == ServiceFaultCategory.StateFactory ||
            second.Runner.Snapshot.Fault.Category == ServiceFaultCategory.StateFactory,
            clock);
        Assert.True((firstDefinition.EvaluationCount(1) == 0) ^
                    (secondDefinition.EvaluationCount(1) == 0));
    }

    [Fact]
    public void StateClaimRemainsOwnedUntilBlockingReleaseStateCompletes()
    {
        var clock = new ThreadSafeTestClock(100);
        using var release = new StateReleaseGate();
        var shared = new LifecycleState(901, 1);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.state-release")
        {
            SharedState = shared,
            StateReleaseGate = release,
        };
        using var registration = registry.Register(
            definition, new LifecycleConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        WaitForResponse(pump, registration, ref frame);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        // Signal-driven; five seconds is only the failure deadline under host scheduling pressure.
        Assert.True(release.Entered.Wait(TimeSpan.FromSeconds(5)));
        PumpUntil(pump, ref frame, () =>
            registration.Runner.Snapshot.Fault.Category == ServiceFaultCategory.StateFactory,
            clock);
        Assert.Equal(0, definition.EvaluationCount(2));

        release.Release.Set();
        clock.Advance(new MonotonicDuration(10));
        PumpUntil(pump, ref frame, () => definition.EvaluationCount(2) != 0, clock);
    }

    [Fact]
    public void ReentrantFactoryTokenFailsFastAndStaleHandleCannotClearNewOwner()
    {
        var ledger = new ServiceResourceClaimLedger(1);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var first));
        Assert.Equal(
            ServiceResourceClaimResult.Contended,
            ledger.TryBeginFactory(ServiceResourceRole.State, out _));
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.FinalizeFactory(first, new object()));
        ledger.EndFactory(first);

        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.State, out var second));
        Assert.Throws<InvalidOperationException>(() => ledger.EndFactory(first));
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.FinalizeFactory(second, new object()));
        ledger.EndFactory(second);
        Assert.Equal(2, ledger.LiveClaimCount);
        Assert.True(ledger.Release(first));
        Assert.True(ledger.Release(second));
        Assert.Equal(0, ledger.LiveClaimCount);
    }

    [Fact]
    public void SlowFactorySerializesSameIdentityToOneOwnerAndOneAlias()
    {
        var ledger = new ServiceResourceClaimLedger(1);
        var identity = new object();
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var owner));
        Assert.Equal(
            ServiceResourceClaimResult.Contended,
            ledger.TryClaim(identity, ServiceResourceRole.State, out _));
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.FinalizeFactory(owner, identity));
        ledger.EndFactory(owner);
        Assert.Equal(
            ServiceResourceClaimResult.Aliased,
            ledger.TryClaim(identity, ServiceResourceRole.State, out _));
        Assert.Equal(1, ledger.LiveClaimCount);
        Assert.True(ledger.Release(owner));
    }

    [Fact]
    public void BusyFactoryTokenRejectsEveryReferenceRoleBeforeCallback()
    {
        foreach (var role in new[]
                 {
                     ServiceResourceRole.WorkerDefinition,
                     ServiceResourceRole.Frame,
                     ServiceResourceRole.State,
                 })
        {
            var ledger = new ServiceResourceClaimLedger(1);
            Assert.Equal(
                ServiceResourceClaimResult.Claimed,
                ledger.TryBeginFactory(ServiceResourceRole.Frame, out var owner));
            var callbackCount = 0;

            var admission = ledger.TryBeginFactory(role, out _);
            if (admission == ServiceResourceClaimResult.Claimed) callbackCount++;

            Assert.Equal(ServiceResourceClaimResult.Contended, admission);
            Assert.Equal(0, callbackCount);
            Assert.Equal(2, ledger.ClaimAllocationCount);
            ledger.EndFactory(owner);
            Assert.Equal(0, ledger.LiveClaimCount);
        }
    }

    [Fact]
    public void BusyTokenReturnsTypedRunnerContentionBeforeWorkerDefinitionFactoryCall()
    {
        var clock = new ThreadSafeTestClock(100);
        var ledger = new ServiceResourceClaimLedger(1);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.State, out var blocker));
        var definition = new LifecycleServiceDefinition("lifecycle.token-worker-admission");
        using var configuration = new ServiceConfigurationPublisher<LifecycleConfig>(
            new LifecycleConfig(1));

        var construction = ServiceRunnerFactory<
            LifecycleFrame,
            LifecycleConfig,
            LifecycleState,
            LifecycleAction>.TryCreate(
            definition,
            configuration,
            new LifecycleGeneration(1),
            definition.ServiceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            clock,
            measureWorkerAllocations: false,
            resourceClaims: ledger);
        Assert.True(construction.Contended);
        Assert.Null(construction.Runner);
        Assert.Equal(0, definition.WorkerDefinitionCreateCount);
        Assert.Equal(0, definition.FrameCreateCount);
        ledger.EndFactory(blocker);
        Assert.Equal(0, ledger.LiveClaimCount);
    }

    [Fact]
    public void ReplacementContentionRetainsDesiredLifecycleBehindSeparateBackoff()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.token-replacement-backoff");
        using var registration = registry.Register(
            definition,
            new LifecycleConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.State, out var blocker));

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        var frame = 1L;
        PumpUntil(pump, ref frame, () =>
            registration.LifecycleSnapshot.ConstructionContentionCount == 1);
        var snapshot = registration.LifecycleSnapshot;
        Assert.Equal((ulong)2, snapshot.DesiredLifecycle.Value);
        Assert.False(snapshot.ConstructionFault.IsValid);
        Assert.Equal(100 + TimeSpan.FromMilliseconds(16).Ticks, snapshot.ConstructionRetryDue.Ticks);
        Assert.Equal(1, snapshot.ConstructionAttemptCount);

        ledger.EndFactory(blocker);
        pump.PumpFrame(frame++);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);
        clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(16)));
        PumpUntil(pump, ref frame, () =>
        {
            var current = registration.LifecycleSnapshot;
            return (current.Position0.State == ServiceRunnerPositionState.Current &&
                    current.Position0.Lifecycle == new LifecycleGeneration(2)) ||
                   (current.Position1.State == ServiceRunnerPositionState.Current &&
                    current.Position1.Lifecycle == new LifecycleGeneration(2));
        });
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionContentionCount);
        Assert.False(registration.LifecycleSnapshot.ConstructionFault.IsValid);
    }

    [Fact]
    public void WorkerStateFactoryRetriesTokenContentionWithoutSpinningOrFaulting()
    {
        var clock = new ThreadSafeTestClock(100);
        var ledger = new ServiceResourceClaimLedger(1);
        var definition = new CrossRoleServiceDefinition("lifecycle.token-state-retry");
        using var configuration = new ServiceConfigurationPublisher<CrossRoleConfig>(
            new CrossRoleConfig(1));
        using var runner = ServiceRunnerFactory<
            CrossRoleFrame,
            CrossRoleConfig,
            CrossRoleResource,
            CrossRoleAction>.CreateRequired(
            definition,
            configuration,
            new LifecycleGeneration(1),
            definition.ServiceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            clock,
            measureWorkerAllocations: false,
            resourceClaims: ledger);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var blocker));

        Assert.True(runner.TryStartCycle(clock.Now).CaptureAttempted);
        Assert.True(SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.ResponseReady,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(0, definition.StateCreateCount);
        Assert.True(runner.TryAcquireResponse());
        Assert.False(runner.Snapshot.Fault.IsValid);
        Assert.Equal(1, runner.Snapshot.WorkerStateConstructionContentionCount);
        Assert.Equal(
            100 + TimeSpan.FromMilliseconds(16).Ticks,
            runner.Snapshot.NextWakeDue.Ticks);
        Assert.False(runner.TryStartCycle(clock.Now).CaptureAttempted);

        ledger.EndFactory(blocker);
        Assert.False(runner.TryStartCycle(clock.Now).CaptureAttempted);
        clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(16)));
        Assert.True(runner.TryStartCycle(clock.Now).CaptureAttempted);
        Assert.True(SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.ResponseReady,
            TimeSpan.FromSeconds(2)));
        Assert.True(runner.TryAcquireResponse());
        Assert.False(runner.Snapshot.Fault.IsValid);
        Assert.Equal(1, definition.StateCreateCount);
        Assert.Equal(1, runner.Snapshot.WorkerStateConstructionContentionCount);
    }

    [Fact]
    public void StopRemainsPromptAfterWorkerStateFactoryContention()
    {
        var clock = new ThreadSafeTestClock(100);
        var ledger = new ServiceResourceClaimLedger(1);
        var definition = new CrossRoleServiceDefinition("lifecycle.token-state-stop");
        using var configuration = new ServiceConfigurationPublisher<CrossRoleConfig>(
            new CrossRoleConfig(1));
        var runner = ServiceRunnerFactory<
            CrossRoleFrame,
            CrossRoleConfig,
            CrossRoleResource,
            CrossRoleAction>.CreateRequired(
            definition,
            configuration,
            new LifecycleGeneration(1),
            definition.ServiceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            clock,
            measureWorkerAllocations: false,
            resourceClaims: ledger);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var blocker));
        Assert.True(runner.TryStartCycle(clock.Now).CaptureAttempted);
        Assert.True(SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.ResponseReady,
            TimeSpan.FromSeconds(2)));

        var stopped = Stopwatch.StartNew();
        runner.Dispose();
        ledger.EndFactory(blocker);
        Assert.True(SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            TimeSpan.FromSeconds(2)));
        stopped.Stop();
        Assert.True(stopped.Elapsed < TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void RetiredIdentityRemainsVisibleUntilActiveFactoryClearsAndSweeps()
    {
        var ledger = new ServiceResourceClaimLedger(1);
        var identity = new object();
        var owner = ledger.Claim(identity, ServiceResourceRole.State);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var contender));

        Assert.True(ledger.Release(owner));
        Assert.Equal(2, ledger.LiveClaimCount);
        Assert.Equal(
            ServiceResourceClaimResult.Aliased,
            ledger.FinalizeFactory(contender, identity));
        ledger.EndFactory(contender);

        Assert.Equal(0, ledger.LiveClaimCount);
        Assert.False(ledger.Release(owner));
    }

    [Fact]
    public void RetirementAfterFactoryClearRemovesExactClaimImmediately()
    {
        var ledger = new ServiceResourceClaimLedger(1);
        var owner = ledger.Claim(new object(), ServiceResourceRole.State);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var factory));
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.FinalizeFactory(factory, new object()));
        ledger.EndFactory(factory);

        Assert.True(ledger.Release(owner));
        Assert.Equal(1, ledger.LiveClaimCount);
        Assert.True(ledger.Release(factory));
        Assert.Equal(0, ledger.LiveClaimCount);
    }

    [Fact]
    public void RetirementImmediatelyBeforeFactoryClosingIsSweptBeforeTokenClear()
    {
        var ledger = new ServiceResourceClaimLedger(1);
        var owner = ledger.Claim(new object(), ServiceResourceRole.State);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var factory));

        Assert.True(ledger.Release(owner));
        Assert.Equal(2, ledger.LiveClaimCount);
        ledger.BeginFactoryClose(factory);
        Assert.Equal(
            ServiceResourceClaimResult.Contended,
            ledger.TryBeginFactory(ServiceResourceRole.State, out _));
        ledger.CompleteFactoryClose(factory);

        Assert.Equal(0, ledger.LiveClaimCount);
    }

    [Fact]
    public void RetirementAfterFactoryClosingSelfRemovesBeforeSuccessorCanStart()
    {
        var ledger = new ServiceResourceClaimLedger(1);
        var owner = ledger.Claim(new object(), ServiceResourceRole.State);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.Frame, out var factory));
        ledger.BeginFactoryClose(factory);

        Assert.True(ledger.Release(owner));
        Assert.Equal(1, ledger.LiveClaimCount);
        Assert.Equal(
            ServiceResourceClaimResult.Contended,
            ledger.TryBeginFactory(ServiceResourceRole.State, out _));
        ledger.CompleteFactoryClose(factory);
        Assert.Equal(0, ledger.LiveClaimCount);

        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.State, out var successor));
        ledger.EndFactory(successor);
        Assert.Equal(0, ledger.LiveClaimCount);
    }

}
