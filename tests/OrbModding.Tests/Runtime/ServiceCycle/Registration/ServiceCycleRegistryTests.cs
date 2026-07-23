using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleRegistryTests
{
    [Fact]
    public void RegistrationIsTypedStableAndStartsOneSleepingWorkerPerService()
    {
        using var registry = new ServiceCycleRegistry(2);
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.first"), new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));
        using var second = registry.Register(
            new SyntheticServiceDefinition("test.second"), new SyntheticConfig(2), new RuntimeLifecycleGeneration(1));

        Assert.IsType<ServiceRunner<SyntheticFrame, SyntheticConfig, SyntheticState, SyntheticAction>>(first.Runner);
        Assert.Equal("test.first", registry.GetServiceId(0).Value);
        Assert.Equal("test.second", registry.GetServiceId(1).Value);
        Assert.Equal(OrbModding.Common.Runtime.ServiceCycle.Contracts.ServiceCyclePhase.Waiting, first.Runner.Phase);
        Assert.True(SpinWait.SpinUntil(
            () => first.Runner.Snapshot.WorkerThreadId != 0,
            TimeSpan.FromSeconds(2)));
        Assert.True(first.Runner.Snapshot.WorkerIsBackground);
    }

    [Fact]
    public void CapacityAndDuplicateRejectionOccurBeforeConstruction()
    {
        using var registry = new ServiceCycleRegistry(2);
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.one"), new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));
        var duplicate = new SyntheticServiceDefinition("test.one");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            duplicate, new SyntheticConfig(2), new RuntimeLifecycleGeneration(1)));
        Assert.Equal(0, duplicate.FrameCreateCount);

        using var second = registry.Register(
            new SyntheticServiceDefinition("test.two"), new SyntheticConfig(2), new RuntimeLifecycleGeneration(1));
        var overflow = new SyntheticServiceDefinition("test.three");
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            overflow, new SyntheticConfig(3), new RuntimeLifecycleGeneration(1)));
        Assert.Equal(0, overflow.FrameCreateCount);
    }

    [Fact]
    public void WorkerDefinitionFailureDoesNotAcquireResources()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = new SyntheticServiceDefinition("test.rollback") { ThrowFromWorkerFactory = true };

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition, new SyntheticConfig(1), new RuntimeLifecycleGeneration(1)));

        Assert.Equal(0, definition.FrameCreateCount);
        Assert.Equal(0, definition.StateCreateCount);
        Assert.Equal(0, definition.FrameReleaseCount);
        Assert.Equal(0, definition.StateReleaseCount);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void WorkerDefinitionFailureIsTheRegistrationFailure()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = new SyntheticServiceDefinition("test.rollback-original")
        {
            ThrowFromWorkerFactory = true,
        };

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition, new SyntheticConfig(1), new RuntimeLifecycleGeneration(1)));

        Assert.Contains("worker construction", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, definition.FrameReleaseCount);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void WorkerStartFailureReleasesTheFrameAndNeverStartedWakeHandle()
    {
        var definition = new SyntheticServiceDefinition("test.start-failure");
        using var configuration = new ServiceConfigurationPublisher<SyntheticConfig>(new SyntheticConfig(1));
        var handoff = new ServiceCycleHandoff<SyntheticConfig>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ServiceRunnerFactory<SyntheticFrame, SyntheticConfig, SyntheticState, SyntheticAction>.CreateRequired(
                definition,
                configuration,
                new RuntimeLifecycleGeneration(1),
                definition.ServiceId,
                WakePolicy.Immediate,
                definition.FaultRecoveryPolicy,
                new ThreadSafeTestClock(100),
                measureWorkerAllocations: false,
                handoff: handoff,
                workerStarter: new ThrowingWorkerStarter()));

        Assert.Contains("thread start failure", exception.Message, StringComparison.Ordinal);
        Assert.True(handoff.WorkerWakeDisposed);
        Assert.True(definition.ResourcesReleased.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, definition.FrameCreateCount);
        Assert.Equal(1, definition.FrameReleaseCount);
        Assert.Equal(0, definition.StateCreateCount);
        Assert.Equal(0, definition.StateReleaseCount);
    }

    private sealed class ThrowingWorkerStarter : IServiceCycleWorkerStarter
    {
        public void Start(Thread thread) =>
            throw new InvalidOperationException("synthetic thread start failure");
    }

    [Fact]
    public void RegistrationAndRegistryDisposalAreIdempotent()
    {
        var registry = new ServiceCycleRegistry(1);
        var definition = new SyntheticServiceDefinition("test.dispose");
        var registration = registry.Register(
            definition, new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));

        registration.Dispose();
        registration.Dispose();
        registry.Dispose();
        registry.Dispose();

        Assert.True(definition.ResourcesReleased.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, definition.FrameReleaseCount);
        Assert.Equal(0, definition.StateReleaseCount);
    }

    [Fact]
    public void SlotOwnedConfigurationSurvivesRunnerDisposalUntilRegistrationEnds()
    {
        using var registry = new ServiceCycleRegistry(1);
        var registration = registry.Register(
            new SyntheticServiceDefinition("test.publisher-owner"),
            new SyntheticConfig(1),
            new RuntimeLifecycleGeneration(1));
        registration.Configuration.CompleteSave(
            OrbModding.Common.Runtime.ServiceCycle.Configuration.ConfigurationSaveResult<SyntheticConfig>.Saved(
                new SyntheticConfig(2)));

        registration.Runner.Dispose();

        Assert.Equal(2, registration.Configuration.ReadLatest().Snapshot.Value);
        registration.Dispose();
        Assert.Throws<ObjectDisposedException>(() => registration.Configuration.ReadLatest());
    }

    [Fact]
    public void SlotDisposesPublisherEvenWhenRunnerReleaseFaults()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = new SyntheticServiceDefinition("test.release-fault") { ThrowFromStateRelease = true };
        var registration = registry.Register(
            definition, new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));
        var publisher = registration.Configuration;

        registration.Dispose();

        Assert.Throws<ObjectDisposedException>(() => publisher.ReadLatest());
        Assert.True(definition.ResourcesReleased.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, definition.FrameReleaseCount);
    }

    [Fact]
    public void ReleaseAndReregisterPreserveRelativeOrderAndFixedCapacity()
    {
        using var registry = new ServiceCycleRegistry(3);
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.order.a"), new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));
        var second = registry.Register(
            new SyntheticServiceDefinition("test.order.b"), new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));
        using var third = registry.Register(
            new SyntheticServiceDefinition("test.order.c"), new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));

        second.Dispose();
        using var fourth = registry.Register(
            new SyntheticServiceDefinition("test.order.d"), new SyntheticConfig(1), new RuntimeLifecycleGeneration(1));

        Assert.Equal("test.order.a", registry.GetServiceId(0).Value);
        Assert.Equal("test.order.d", registry.GetServiceId(1).Value);
        Assert.Equal("test.order.c", registry.GetServiceId(2).Value);
        Assert.Equal(0, first.Ordinal);
        Assert.Equal(1, fourth.Ordinal);
        Assert.Equal(2, third.Ordinal);
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new SyntheticServiceDefinition("test.order.e"),
            new SyntheticConfig(1),
            new RuntimeLifecycleGeneration(1)));
    }

    [Fact]
    public void SealedCompositionRejectsRegistrationAndLeavesStableTombstoneOrdinals()
    {
        using var registry = new ServiceCycleRegistry(3);
        var first = registry.Register(
            new SyntheticServiceDefinition("test.sealed.a"),
            new SyntheticConfig(1),
            new RuntimeLifecycleGeneration(1));
        using var second = registry.Register(
            new SyntheticServiceDefinition("test.sealed.b"),
            new SyntheticConfig(2),
            new RuntimeLifecycleGeneration(1));

        registry.Seal();
        registry.Seal();
        first.Dispose();

        Assert.True(registry.IsSealed);
        Assert.Equal(1, registry.Count);
        Assert.Equal(2, registry.OrdinalCount);
        Assert.Equal(0, first.Ordinal);
        Assert.Equal(1, second.Ordinal);
        Assert.Equal("test.sealed.a", registry.GetServiceId(0).Value);
        Assert.Equal("test.sealed.b", registry.GetServiceId(1).Value);
        Assert.True(registry.GetSlot(0).IsDisposed);
        Assert.False(registry.GetSlot(1).IsDisposed);

        var late = new SyntheticServiceDefinition("test.sealed.late");
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            late,
            new SyntheticConfig(3),
            new RuntimeLifecycleGeneration(1)));
        Assert.Equal(0, late.FrameCreateCount);
    }

    [Fact]
    public void RegistrationMutationsAssertTheOwnerThread()
    {
        using var registry = new ServiceCycleRegistry(1);
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try
            {
                registry.Register(
                    new SyntheticServiceDefinition("test.foreign"),
                    new SyntheticConfig(1),
                    new RuntimeLifecycleGeneration(1));
            }
            catch (Exception ex)
            {
                observed = ex;
            }
        });

        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(2)),
            "The foreign-thread registration probe did not complete.");

        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void DisposedRegistryStillAssertsOwnerThreadBeforeIdempotentReturn()
    {
        var registry = new ServiceCycleRegistry(1);
        registry.Dispose();
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try { registry.Dispose(); }
            catch (Exception ex) { observed = ex; }
        });

        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(2)),
            "The foreign-thread disposal probe did not complete.");

        Assert.IsType<InvalidOperationException>(observed);
        registry.Dispose();
    }
}
