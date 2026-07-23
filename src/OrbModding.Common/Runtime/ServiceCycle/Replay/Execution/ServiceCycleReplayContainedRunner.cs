using System;
using System.Runtime.ExceptionServices;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>One production-only exception and cleanup boundary around feature-owned replay work.</summary>
internal static class ServiceCycleReplayContainedRunner
{
    internal static ServiceCycleReplayExecutionResult Run(
        ServiceCycleReplayArtifactDocument artifact,
        IServiceCycleReplayExecutionRegistration?[] registrations,
        TimeSpan workerBoundaryTimeout)
    {
        var capacity = artifact.SemanticTrace.ServiceCapacity;
        var participants = new IServiceCycleReplayProductionParticipant?[capacity];
        var cursor = new ServiceCycleReplayFailureCursor(
            ServiceCycleReplayProductionPreflight.FirstCycle(artifact));
        ServiceCycleReplayExecutionResult result = default;
        var hasResult = false;
        ExceptionDispatchInfo? firstFatal = null;
        try
        {
            var plan = new ServiceCycleReplayProductionArtifactPlan(artifact);
            var preflight = ServiceCycleReplayProductionPreflight.Validate(plan, registrations);
            if (preflight.HasValue)
            {
                result = preflight.Value;
                hasResult = true;
            }

            for (var index = 0; !hasResult && index < capacity; index++)
            {
                var cycle = ServiceCycleReplayProductionPreflight.FirstCycle(
                    artifact, index + 1, cursor.Cycle);
                cursor.Enter(ServiceCycleReplayExecutionDetailCode.ProductionPreparationRejected, cycle);
                var participant = registrations[index]!.PrepareProduction(plan);
                participants[index] = participant;
                if (participant.Preparation.Succeeded) continue;
                result = participant.Preparation;
                hasResult = true;
                break;
            }
            if (!hasResult)
            {
                var prepared = new IServiceCycleReplayProductionParticipant[capacity];
                for (var index = 0; index < capacity; index++) prepared[index] = participants[index]!;
                result = ServiceCycleReplayProductionCoordinator.Run(
                    plan, prepared, workerBoundaryTimeout, cursor);
                hasResult = true;
            }
        }
        catch (Exception exception)
        {
            if (IsContainable(exception))
            {
                cursor.CapturePrimaryException();
                result = cursor.TranslateException();
                hasResult = true;
            }
            else
            {
                firstFatal = ExceptionDispatchInfo.Capture(exception);
            }
        }
        finally
        {
            for (var index = participants.Length - 1; index >= 0; index--)
            {
                var participant = participants[index];
                if (participant is null) continue;
                cursor.Enter(
                    ServiceCycleReplayExecutionDetailCode.ProductionCleanupRejected,
                    participant.FirstCycle.IsValid ? participant.FirstCycle : cursor.Cycle);
                try
                {
                    participant.DisposeAndWait(workerBoundaryTimeout);
                }
                catch (Exception exception)
                {
                    if (IsContainable(exception))
                    {
                        if (!hasResult || result.Succeeded)
                        {
                            result = cursor.TranslateException();
                            hasResult = true;
                        }
                    }
                    else
                        firstFatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }
        }
        firstFatal?.Throw();
        return hasResult
            ? result
            : throw new InvalidOperationException("The contained production replay returned no result.");
    }

    internal static bool IsContainable(Exception exception) =>
        exception is not StackOverflowException and not OutOfMemoryException and not AccessViolationException;
}

/// <summary>Mutable phase cursor used only inside the synchronous contained production boundary.</summary>
internal sealed class ServiceCycleReplayFailureCursor
{
    private ServiceCycleReplayExecutionResult _completed;
    private bool _hasCompleted;

    internal ServiceCycleReplayFailureCursor(ServiceCycleReplayCycleKey cycle)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid replay cycle is required.", nameof(cycle));
        Cycle = cycle;
        Detail = ServiceCycleReplayExecutionDetailCode.ProductionPreparationRejected;
    }

    internal ServiceCycleReplayCycleKey Cycle { get; private set; }
    internal ServiceCycleReplayExecutionDetailCode Detail { get; private set; }

    internal void Enter(ServiceCycleReplayExecutionDetailCode detail, ServiceCycleReplayCycleKey cycle)
    {
        Detail = detail;
        if (cycle.IsValid) Cycle = cycle;
    }

    internal ServiceCycleReplayExecutionResult Complete(ServiceCycleReplayExecutionResult result)
    {
        _completed = result;
        _hasCompleted = true;
        return result;
    }

    internal void CapturePrimaryException()
    {
        if (_hasCompleted) return;
        _completed = ServiceCycleReplayProductionResult.Fault(Cycle, Detail);
        _hasCompleted = true;
    }

    internal ServiceCycleReplayExecutionResult TranslateException() =>
        _hasCompleted && !_completed.Succeeded
            ? _completed
            : ServiceCycleReplayProductionResult.Fault(Cycle, Detail);
}
