using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayLifecycleIdentityTests
{
    [Fact]
    public void ResponseBoundaryWaitsOnlyForExactAcquisitionFact()
    {
        Assert.True(ServiceCycleReplayProductionCoordinator.RequiresResponseReady(
            ServiceCycleSemanticEventKind.CycleStarted));
        Assert.False(ServiceCycleReplayProductionCoordinator.RequiresResponseReady(
            ServiceCycleSemanticEventKind.EvaluationCompleted));
        Assert.False(ServiceCycleReplayProductionCoordinator.RequiresResponseReady(
            ServiceCycleSemanticEventKind.CycleOrphaned));
        Assert.False(ServiceCycleReplayProductionCoordinator.RequiresResponseReady(
            ServiceCycleSemanticEventKind.LifecycleRetired));
    }

    [Fact]
    public void DelayedRequestPublicationIsRejectedAtItsExactCycle()
    {
        var trace = PublicationTrace(queueAfterPump: true);

        var rejected = ServiceCycleReplayProductionCoordinator.FindDelayedRequestPublication(
            trace, new[] { 1 });

        Assert.True(rejected.IsValid);
        Assert.Equal(1, rejected.TraceServiceKey);
        Assert.Equal(1UL, rejected.Lifecycle);
        Assert.Equal(1UL, rejected.Cycle);
    }

    [Fact]
    public void SamePumpRequestPublicationIsSupported()
    {
        var trace = PublicationTrace(queueAfterPump: false);

        var rejected = ServiceCycleReplayProductionCoordinator.FindDelayedRequestPublication(
            trace, new[] { 1 });

        Assert.False(rejected.IsValid);
    }

    [Fact]
    public void InitialLifecycleComesFromSharedActivationBeforeAnyCycle()
    {
        var trace = LifecycleTrace(7, 7);

        var accepted = ServiceCycleReplayProductionCoordinator.TryInitialLifecycle(
            trace, new[] { 1, 2 }, out var lifecycle);

        Assert.True(accepted);
        Assert.Equal(7UL, lifecycle.Value);
    }

    [Fact]
    public void InitialLifecycleRejectsInconsistentServiceActivation()
    {
        var trace = LifecycleTrace(7, 8);

        Assert.False(ServiceCycleReplayProductionCoordinator.TryInitialLifecycle(
            trace, new[] { 1, 2 }, out _));
    }

    [Fact]
    public void ExactResponseWaitRejectsAnotherCycleAtTheSamePhysicalHandoff()
    {
        var clock = new ThreadSafeTestClock(100);
        var definition = new ExecutionServiceDefinition("test.replay.lifecycle-wait");
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            clock);
        using var registration = registry.Register(definition, new ExecutionConfig(7));

        Assert.True(registration.Runner.TryStartCycle(clock.Now).Queued);
        var expected = Identity(definition.ServiceId, lifecycle: 1, configuration: 1, cycle: 1);
        var wrong = Identity(definition.ServiceId, lifecycle: 1, configuration: 2, cycle: 1);

        Assert.False(registration.WaitForResponseReady(wrong, TimeSpan.FromSeconds(2)));
        Assert.True(registration.WaitForResponseReady(expected, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void WorkerIdentitySeparatesOverlappingLifecycleSchedules()
    {
        const string service = "test.replay.lifecycle-worker";
        using var firstRegistry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            new ThreadSafeTestClock(100));
        using var first = firstRegistry.Register(
            new ExecutionServiceDefinition(service),
            new ExecutionConfig(1));
        using var secondRegistry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(2),
            new ThreadSafeTestClock(100));
        using var second = secondRegistry.Register(
            new ExecutionServiceDefinition(service),
            new ExecutionConfig(1));

        Assert.Equal("Orb.ServiceCycle.test.replay.lifecycle-worker.lifecycle-1", first.Runner.WorkerName);
        Assert.Equal("Orb.ServiceCycle.test.replay.lifecycle-worker.lifecycle-2", second.Runner.WorkerName);
        Assert.NotEqual(first.Runner.WorkerName, second.Runner.WorkerName);
    }

    private static ServiceCycleIdentity Identity(
        ServiceId service,
        ulong lifecycle,
        ulong configuration,
        ulong cycle) => new(
            service,
            new LifecycleGeneration(lifecycle),
            new ConfigGeneration(configuration),
            new StrategyGeneration(1),
            new CaptureSequence(cycle),
            new CycleId(cycle));

    private static ServiceCycleTraceDocument PublicationTrace(bool queueAfterPump)
    {
        var session = new ServiceCycleTraceSessionId(991);
        var service = new ServiceCycleTraceServiceId(1);
        var capture = new ServiceCycleTraceCaptureIdentity(service, 1, 1, 1, 1);
        var cycle = new ServiceCycleTraceCycleIdentity(service, 1, 1, 1, 1, 1);
        var completedPayload = ServiceCycleSemanticPayload.CaptureFact(
            in capture, 1, CommonServiceDecisionCodes.Captured.Value, 10, 1);
        var queuedPayload = ServiceCycleSemanticPayload.CycleFact(
            in cycle, CommonServiceDecisionCodes.Ready.Value, 11, 1);
        var pumpPayload = ServiceCycleSemanticPayload.Pump(
            1, true, 0, 0, 0, 1, 0, 0, 0, 0, 1, 1, 11);
        var completed = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 1),
            default,
            ServiceCycleSemanticEventKind.CaptureCompleted,
            in completedPayload);
        ServiceCycleSemanticEvent[] events;
        if (queueAfterPump)
        {
            var pump = new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(session, 2),
                default,
                ServiceCycleSemanticEventKind.PumpCompleted,
                in pumpPayload);
            var queued = new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(session, 3),
                completed.Id,
                ServiceCycleSemanticEventKind.CycleQueued,
                in queuedPayload);
            events = new[] { completed, pump, queued };
        }
        else
        {
            var queued = new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(session, 2),
                completed.Id,
                ServiceCycleSemanticEventKind.CycleQueued,
                in queuedPayload);
            var pump = new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(session, 3),
                default,
                ServiceCycleSemanticEventKind.PumpCompleted,
                in pumpPayload);
            events = new[] { completed, queued, pump };
        }
        return new ServiceCycleTraceDocument(
            ServiceCycleTraceCodec.SchemaVersion,
            session,
            default,
            1,
            events);
    }

    private static ServiceCycleTraceDocument LifecycleTrace(ulong firstLifecycle, ulong secondLifecycle)
    {
        var session = new ServiceCycleTraceSessionId(992);
        var firstPayload = ServiceCycleSemanticPayload.LifecycleFact(
            new ServiceCycleTraceServiceId(1), firstLifecycle, 0, 10);
        var secondPayload = ServiceCycleSemanticPayload.LifecycleFact(
            new ServiceCycleTraceServiceId(2), secondLifecycle, 0, 10);
        var pumpPayload = ServiceCycleSemanticPayload.Pump(
            1, true, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 10);
        var first = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 1), default,
            ServiceCycleSemanticEventKind.LifecycleActivated, in firstPayload);
        var second = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 2), default,
            ServiceCycleSemanticEventKind.LifecycleActivated, in secondPayload);
        var pump = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 3), default,
            ServiceCycleSemanticEventKind.PumpCompleted, in pumpPayload);
        return new ServiceCycleTraceDocument(
            ServiceCycleTraceCodec.SchemaVersion,
            session,
            default,
            2,
            new[] { first, second, pump });
    }
}
