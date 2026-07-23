using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class ServiceCyclePumpTestWait
{
    internal static void PrepareBatch(
        SuiteFramePump pump,
        ServiceRegistration<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction> registration,
        ref long frame)
    {
        WaitForResponse(pump, registration, ref frame);
        PumpUntil(pump, ref frame, () => registration.Runner.Snapshot.ActionCount != 0);
    }

    internal static void WaitForResponse(
        SuiteFramePump pump,
        ServiceRegistration<LifecycleFrame, LifecycleConfig, LifecycleState, LifecycleAction> registration,
        ref long frame)
    {
        var timeout = TimeSpan.FromSeconds(3);
        var deadline = Stopwatch.StartNew();
        while (registration.Runner.HandoffPhaseHint != ServiceHandoffPhase.ResponseReady)
        {
            if (registration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
                pump.PumpFrame(frame++);
            else
                Thread.Yield();
            if (deadline.Elapsed > timeout)
                throw new TimeoutException("The lifecycle fixture did not publish its response.");
        }
        var remaining = timeout - deadline.Elapsed;
        if (remaining <= TimeSpan.Zero || !registration.WaitForResponseReady(remaining))
            throw new TimeoutException("The lifecycle fixture response did not settle.");
    }

    internal static void PumpUntil(
        SuiteFramePump pump,
        ref long frame,
        Func<bool> condition,
        ThreadSafeTestClock? progressClock = null)
    {
        var progressStep = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(16));
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            pump.PumpFrame(frame++);
            progressClock?.Advance(progressStep);
            if (deadline.Elapsed > TimeSpan.FromSeconds(3))
                throw new TimeoutException("The lifecycle fixture did not reach its expected phase.");
            Thread.Yield();
        }
    }
}
