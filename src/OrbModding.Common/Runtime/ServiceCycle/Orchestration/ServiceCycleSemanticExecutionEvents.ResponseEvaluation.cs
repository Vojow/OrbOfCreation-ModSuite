using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed partial class ServiceCycleSemanticExecutionEvents
{
    internal void ResponseAcquired(int ordinal, in ServiceResponseAcquisition acquisition) =>
        EmitResponse(ordinal, in acquisition, emitTerminalReceipt: true);

    internal void EmitResponse(
        int ordinal,
        in ServiceResponseAcquisition acquisition,
        bool emitTerminalReceipt)
    {
        if (!acquisition.Acquired) return;
        var response = acquisition.Response;
        var cycle = response.Cycle;
        var duration = Duration(response.EvaluationStartedAt, response.EvaluationCompletedAt);
        _recorder.CycleStarted(ordinal, in cycle, response.EvaluationStartedAt, default);

        if (response.TransientContention)
        {
            _recorder.EvaluationDeferred(
                ordinal,
                in cycle,
                response.EvaluationCompletedAt,
                duration,
                response.RetryDue);
            _recorder.CycleCompleted(ordinal, in cycle, response.EvaluationCompletedAt, duration);
            _recorder.ClearRetainedEmergencyForService(ordinal);
            return;
        }

        _recorder.EvaluationStarted(ordinal, in cycle, response.EvaluationStartedAt);
        if (response.Succeeded)
        {
            var publication = new ServiceProjectionPublication(
                response.ProjectionContext,
                response.Projection,
                response.Cycle.Config);
            _recorder.StatePublished(ordinal, in publication);
            _recorder.EvaluationCompleted(
                ordinal,
                in cycle,
                response.ActionCount,
                response.WakePolicy,
                response.EvaluationCompletedAt,
                duration);
            _recorder.BatchPublished(
                ordinal,
                in cycle,
                response.Batch,
                response.ActionCount,
                response.PublishedAt);
            var recoveredFault = response.RecoveredFault;
            EmitRecovery(ordinal, cycle.Lifecycle, in recoveredFault);
            if (!emitTerminalReceipt) return;
            var terminalReceipt = acquisition.TerminalReceipt;
            if (terminalReceipt.HasEmergencyStopContext)
                EmergencyRejected(ordinal, in terminalReceipt);
            else
                EmitTerminalReceipt(ordinal, in terminalReceipt);
            return;
        }

        var fault = response.Fault;
        if (fault.Category == ServiceFaultCategory.StateProjection && response.HasEvaluationOutcome)
        {
            _recorder.EvaluationCompleted(
                ordinal,
                in cycle,
                response.EvaluatedActionCount,
                response.EvaluationWakePolicy,
                response.EvaluationCompletedAt,
                duration);
            _recorder.ProjectionFaulted(
                ordinal,
                in cycle,
                response.EvaluatedActionCount,
                response.EvaluationWakePolicy,
                in fault,
                response.EvaluationCompletedAt,
                duration);
        }
        else
        {
            _recorder.EvaluationFaulted(
                ordinal,
                in cycle,
                in fault,
                response.EvaluationCompletedAt,
                duration);
        }
        _recorder.CycleFaulted(
            ordinal,
            in cycle,
            in fault,
            response.EvaluationCompletedAt,
            duration);
        EmitFault(ordinal, cycle.Lifecycle, in fault, response.RetryDue);
        _recorder.ClearRetainedEmergencyForService(ordinal);
    }
}
