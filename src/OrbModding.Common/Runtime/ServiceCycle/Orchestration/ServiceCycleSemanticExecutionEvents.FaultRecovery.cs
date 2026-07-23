using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed partial class ServiceCycleSemanticExecutionEvents
{
    internal void EmitFault(
        int ordinal,
        LifecycleGeneration lifecycle,
        in ServiceFault fault,
        MonotonicTimestamp retryDue)
    {
        if (!fault.IsValid) return;
        _recorder.FaultObserved(ordinal, lifecycle, in fault);
        _recorder.RetryScheduled(ordinal, lifecycle, in fault, retryDue);
    }

    internal void EmitRecovery(
        int ordinal,
        LifecycleGeneration lifecycle,
        in ServiceFaultRecoveryFact recovery)
    {
        if (!recovery.IsPresent) return;
        var fault = recovery.Fault;
        _recorder.FaultRecovered(ordinal, lifecycle, in fault, recovery.RecoveredAt);
    }

    private static MonotonicDuration Duration(
        MonotonicTimestamp startedAt,
        MonotonicTimestamp completedAt) =>
        new(completedAt.Ticks >= startedAt.Ticks ? completedAt.Ticks - startedAt.Ticks : 0);
}
