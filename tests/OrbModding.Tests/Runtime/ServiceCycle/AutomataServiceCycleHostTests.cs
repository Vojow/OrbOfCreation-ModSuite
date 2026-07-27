using System;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle;

public sealed class AutomataServiceCycleHostTests
{
    [Fact]
    public void OneHostPumpsDifferentlyTypedServicesAndOwnsSuiteLifecycle()
    {
        var frame = 1L;
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(
            2,
            new LifecycleGeneration(7),
            clock);
        var execution = new ExecutionServiceDefinition("host.execution");
        // A second service with a different state and action, reading the same suite
        // configuration: there is one publication, so there is one snapshot type.
        var synthetic = new TypeSafetyDefinition<SyntheticState, SyntheticAction>(
            new SyntheticState(), "host.synthetic");
        using var executionRegistration = registry.Register(
            execution,
            new LifecycleGeneration(7));
        using var syntheticRegistration = registry.Register(
            synthetic,
            new LifecycleGeneration(7));
        using var host = new AutomataServiceCycleHost(
            registry,
            () => frame,
            pumpTiming: null,
            semanticTrace: null);
        TestWorldCollector.CollectedAtActivation(registry);

        var first = host.Tick();

        Assert.True(first.Accepted);
        Assert.Equal(2, first.CyclesStarted);
        Assert.Equal(1, execution.StartCount);
        Assert.Equal(2, host.Pump.ServiceCapacity);
        Assert.False(host.Tick().Accepted);
        Assert.Equal(1, execution.StartCount);
        Assert.True(host.TryReplaceLifecycle(8));
        Assert.Equal(new LifecycleGeneration(8), host.CurrentLifecycle);
        Assert.False(host.TryReplaceLifecycle(8));
    }

    [Fact]
    public void SemanticRecorderSizedToTheRegistryOrdinalCountInitializesTheMultiServiceHost()
    {
        var frame = 1L;
        using var registry = new ServiceCycleRegistry(
            2,
            new LifecycleGeneration(3),
            new ThreadSafeTestClock(100));
        using var executionRegistration = registry.Register(
            new ExecutionServiceDefinition("host.semantic.execution"),
            new LifecycleGeneration(3));
        using var syntheticRegistration = registry.Register(
            new TypeSafetyDefinition<SyntheticState, SyntheticAction>(
                new SyntheticState(), "host.semantic.synthetic"),
            new LifecycleGeneration(3));
        Assert.Equal(2, registry.OrdinalCount);
        var semantic = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(9),
            eventCapacity: 32,
            serviceCapacity: registry.OrdinalCount);

        using var host = new AutomataServiceCycleHost(
            registry,
            () => frame,
            pumpTiming: null,
            semanticTrace: semantic);

        Assert.Equal(2, host.Pump.ServiceCapacity);
        Assert.True(host.Tick().Accepted);
    }

    [Fact]
    public void SemanticRecorderUndersizedForTheRegistryFailsHostInitialization()
    {
        using var registry = new ServiceCycleRegistry(
            2,
            new LifecycleGeneration(3),
            new ThreadSafeTestClock(100));
        using var executionRegistration = registry.Register(
            new ExecutionServiceDefinition("host.undersized.execution"),
            new LifecycleGeneration(3));
        using var syntheticRegistration = registry.Register(
            new TypeSafetyDefinition<SyntheticState, SyntheticAction>(
                new SyntheticState(), "host.undersized.synthetic"),
            new LifecycleGeneration(3));
        var undersized = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(9),
            eventCapacity: 32,
            serviceCapacity: 1);

        var error = Assert.Throws<ArgumentException>(() => new AutomataServiceCycleHost(
            registry,
            () => 1L,
            pumpTiming: null,
            semanticTrace: undersized));
        Assert.Equal("semanticRecorder", error.ParamName);
    }

    [Fact]
    public void ClaimedPumpShutdownRunsExactlyOnce()
    {
        var calls = 0;
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            new ThreadSafeTestClock(100));
        using var registration = registry.Register(
            new SyntheticServiceDefinition("host.shutdown"),
            new LifecycleGeneration(1));
        var host = new AutomataServiceCycleHost(
            registry,
            () => 1,
            pumpTiming: null,
            semanticTrace: null);
        host.ClaimPumpShutdown(() =>
        {
            calls++;
            host.Pump.Dispose();
            return true;
        });

        host.Dispose();
        host.Dispose();

        Assert.Equal(1, calls);
    }
}
