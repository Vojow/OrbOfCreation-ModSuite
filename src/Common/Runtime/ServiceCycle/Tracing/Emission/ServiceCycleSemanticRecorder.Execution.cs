using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

public sealed partial class ServiceCycleSemanticRecorder
{
    public void EvaluationStarted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt) =>
        _evaluation.EvaluationStarted(ordinal, in cycle, observedAt);

    public void EvaluationCompleted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        int actionCount,
        WakePolicy returnedWake,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _evaluation.EvaluationCompleted(ordinal, in cycle, actionCount, returnedWake, observedAt, duration);

    public void EvaluationFaulted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _evaluation.EvaluationFaulted(ordinal, in cycle, in fault, observedAt, duration);

    public void ProjectionFaulted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        int actionCount,
        WakePolicy returnedWake,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _evaluation.ProjectionFaulted(
            ordinal,
            in cycle,
            actionCount,
            returnedWake,
            in fault,
            observedAt,
            duration);

    public void EvaluationDeferred(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration,
        MonotonicTimestamp retryDue) =>
        _evaluation.EvaluationDeferred(ordinal, in cycle, observedAt, duration, retryDue);

    public void StatePublished(int ordinal, in ServiceProjectionPublication publication) =>
        _evaluation.StatePublished(ordinal, in publication);

    public void BatchPublished(
        int ordinal,
        in ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        MonotonicTimestamp observedAt) =>
        _batches.BatchPublished(ordinal, in cycle, batch, actionCount, observedAt);

    public void ActionAttempted(int ordinal, in ServiceActionContext context) =>
        _batches.ActionAttempted(ordinal, in context);

    public void ActionCompleted(
        int ordinal,
        in ServiceActionContext context,
        in ServiceActionResult result,
        MonotonicTimestamp completedAt,
        MonotonicDuration duration) =>
        _batches.ActionCompleted(ordinal, in context, in result, completedAt, duration);

    public void ActionRejectedForEmergency(
        int ordinal,
        in ServiceActionContext context,
        in ServiceActionResult result,
        in EmergencyStopContext emergency,
        MonotonicTimestamp completedAt,
        MonotonicDuration duration) =>
        _batches.ActionRejectedForEmergency(
            ordinal,
            in context,
            in result,
            in emergency,
            completedAt,
            duration);

    public void BatchTerminal(int ordinal, in BatchReceipt receipt) =>
        _batches.BatchTerminal(ordinal, in receipt);
}
