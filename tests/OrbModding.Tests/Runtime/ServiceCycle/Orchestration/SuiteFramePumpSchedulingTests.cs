using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpSchedulingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeFrameIdentityIsRejectedBeforeWorkWithOrWithoutTracing(bool tracing)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.negative-frame");
        using var registration = registry.Register(
            definition,
            new ExecutionConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = tracing
            ? new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(51), 32, 1)
            : null;
        using var pump = new SuiteFramePump(registry, recorder);

        Assert.Throws<ArgumentOutOfRangeException>(() => pump.PumpFrame(-1));

        Assert.Equal(0, pump.AcceptedFrameCount);
        Assert.Equal(0, definition.CaptureCount);
        Assert.False(pump.SemanticTrace?.EmissionFaulted ?? false);
    }

    [Fact]
    public void DuplicateAndStaleFramesHaveNoEffectsAndBackloggedServicesActOnceEach()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(3, clock);
        var first = new ExecutionServiceDefinition("pump.once.first") { ActionCount = 3 };
        var second = new ExecutionServiceDefinition("pump.once.second") { ActionCount = 3 };
        var third = new ExecutionServiceDefinition("pump.once.third") { ActionCount = 3 };
        using var firstRegistration = registry.Register(first, new ExecutionConfig(1), new LifecycleGeneration(1));
        using var secondRegistration = registry.Register(second, new ExecutionConfig(1), new LifecycleGeneration(1));
        using var thirdRegistration = registry.Register(third, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        first.ActionCount = 0;
        second.ActionCount = 0;
        third.ActionCount = 0;
        PrimeWorkerState(firstRegistration.Runner, clock);
        PrimeWorkerState(secondRegistration.Runner, clock);
        PrimeWorkerState(thirdRegistration.Runner, clock);
        first.ActionCount = 3;
        second.ActionCount = 3;
        third.ActionCount = 3;
        using var pump = new SuiteFramePump(registry);

        var frame = 10L;
        Assert.Equal(3, pump.PumpFrame(frame++).CapturesAttempted);
        while (firstRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty ||
               secondRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty ||
               thirdRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
            pump.PumpFrame(frame++);
        ServiceRunnerTestWait.ForPhase(firstRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(secondRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(thirdRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        var responseFrame = pump.PumpFrame(frame++);
        Assert.Equal(3, responseFrame.ResponsesAcquired);
        Assert.Equal(0, responseFrame.ActionsAttempted);

        var actionFrame = pump.PumpFrame(frame++);
        Assert.Equal(3, actionFrame.ActionsAttempted);
        Assert.Equal(1, first.ActionExecutionCount);
        Assert.Equal(1, second.ActionExecutionCount);
        Assert.Equal(1, third.ActionExecutionCount);

        var lastAccepted = frame - 1;
        var duplicate = pump.PumpFrame(lastAccepted);
        var stale = pump.PumpFrame(lastAccepted - 1);
        Assert.False(duplicate.Accepted);
        Assert.False(stale.Accepted);
        Assert.Equal(1, first.ActionExecutionCount);
        Assert.Equal(1, second.ActionExecutionCount);
        Assert.Equal(1, third.ActionExecutionCount);
        Assert.Equal(frame - 10, pump.AcceptedFrameCount);
    }

    private static void PrimeWorkerState(
        ServiceRunner<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock)
    {
        Assert.True(runner.TryStartCycle(clock.Now).CaptureAttempted);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.False(runner.Snapshot.Fault.IsValid);
    }

    [Fact]
    public void ResponseActionAndTerminalRecaptureRemainOnSeparateFrames()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.boundaries") { ActionCount = 1 };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = 1L;
        var capture = pump.PumpFrame(frame++);
        while (capture.CapturesAttempted == 0)
            capture = pump.PumpFrame(frame++);
        Assert.Equal(1, capture.CapturesAttempted);
        Assert.Equal(0, capture.ActionsAttempted);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);

        var response = pump.PumpFrame(frame++);
        while (response.ResponsesAcquired == 0)
            response = pump.PumpFrame(frame++);
        Assert.Equal(1, response.ResponsesAcquired);
        Assert.Equal(0, response.ActionsAttempted);
        Assert.Equal(0, response.CapturesAttempted);

        var terminalAction = pump.PumpFrame(frame++);
        Assert.Equal(1, terminalAction.ActionsAttempted);
        Assert.Equal(0, terminalAction.CapturesAttempted);
        Assert.Equal(1, definition.CaptureCount);

        var nextCapture = pump.PumpFrame(frame++);
        while (nextCapture.CapturesAttempted == 0)
            nextCapture = pump.PumpFrame(frame++);
        Assert.Equal(1, nextCapture.CapturesAttempted);
        Assert.Equal(2, definition.CaptureCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void FirstOrMiddleTerminalOutcomeAbortsUntouchedSuffix(int terminalIndex, bool fault)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition($"pump.abort.{terminalIndex}.{fault}")
        {
            ActionCount = 4,
            RejectAtIndex = fault ? -1 : terminalIndex,
            FaultAtIndex = fault ? terminalIndex : -1,
        };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = 1L;
        while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        pump.PumpFrame(frame++);
        for (var index = 0; index <= terminalIndex; index++)
            Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);

        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(fault ? BatchTerminalDisposition.Faulted : BatchTerminalDisposition.Rejected, receipt.Disposition);
        Assert.Equal(terminalIndex, receipt.TerminalIndex);
        Assert.Equal(4 - terminalIndex - 1, receipt.UntouchedSuffixCount);
        Assert.Equal(terminalIndex + 1, definition.ActionExecutionCount);
        Assert.Equal(0, pump.PumpFrame(frame).ActionsAttempted);
    }

    [Fact]
    public void RotationAdvancesAcrossZeroActionRejectedAndTombstoneSlots()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(4, clock);
        var active = new ExecutionServiceDefinition("pump.rotate.active") { ActionCount = 2 };
        var empty = new ExecutionServiceDefinition("pump.rotate.empty") { ActionCount = 0 };
        var rejected = new ExecutionServiceDefinition("pump.rotate.rejected") { ActionCount = 2, RejectAtIndex = 0 };
        using var activeRegistration = registry.Register(active, new ExecutionConfig(1), new LifecycleGeneration(1));
        using var emptyRegistration = registry.Register(empty, new ExecutionConfig(1), new LifecycleGeneration(1));
        using var rejectedRegistration = registry.Register(rejected, new ExecutionConfig(1), new LifecycleGeneration(1));
        var tombstone = registry.Register(
            new ExecutionServiceDefinition("pump.rotate.tombstone"),
            new ExecutionConfig(1),
            new LifecycleGeneration(1));
        tombstone.Dispose();
        registry.Seal();
        active.ActionCount = 0;
        rejected.ActionCount = 0;
        PrimeWorkerState(activeRegistration.Runner, clock);
        PrimeWorkerState(emptyRegistration.Runner, clock);
        PrimeWorkerState(rejectedRegistration.Runner, clock);
        active.ActionCount = 2;
        rejected.ActionCount = 2;
        using var pump = new SuiteFramePump(registry);

        var frame = 1L;
        Assert.Equal(0, pump.PumpFrame(frame++).StartingOrdinal);
        while (activeRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty ||
               emptyRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty ||
               rejectedRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
            pump.PumpFrame(frame++);
        ServiceRunnerTestWait.ForPhase(activeRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(emptyRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(rejectedRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        var seenStarts = new bool[4];
        for (var index = 0; index < 4; index++)
            seenStarts[pump.PumpFrame(frame++).StartingOrdinal] = true;
        Assert.All(seenStarts, Assert.True);
        Assert.Equal(2, active.ActionExecutionCount);
        Assert.Equal(1, rejected.ActionExecutionCount);
    }

    [Fact]
    public void SealedRegistryCanBeClaimedByExactlyOneOwnerThreadPump()
    {
        using var unsealed = new ServiceCycleRegistry(1);
        Assert.Throws<InvalidOperationException>(() => new SuiteFramePump(unsealed));

        using var registry = new ServiceCycleRegistry(1);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        Assert.Throws<InvalidOperationException>(() => new SuiteFramePump(registry));

        Exception? observed = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { pump.PumpFrame(1); }
            catch (Exception ex) { observed = ex; }
        });
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(2)),
            "The foreign-thread pump probe did not complete.");
        Assert.IsType<InvalidOperationException>(observed);
    }
}
