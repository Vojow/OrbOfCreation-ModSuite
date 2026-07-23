using System;
using System.Diagnostics;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal static partial class ServiceCycleReplayProductionCoordinator
{
    private static ControlFailure ApplyControl(
        ServiceCycleReplayControlStep step,
        ServiceCycleReplayPumpPlan? pumpPlan,
        SuiteFramePump pump,
        IServiceCycleReplayProductionParticipant[] participants,
        TimeSpan timeout)
    {
        switch (step.Kind)
        {
            case ServiceCycleReplayControlKind.ConfigurationPublished:
                return TryParticipant(participants, step.TraceServiceKey, out var config) &&
                    config.TryPublishConfiguration(step.Generation)
                    ? default : new ControlFailure(
                        config?.FirstCycle ?? participants[0].FirstCycle,
                        ServiceCycleReplayExecutionDetailCode.ConfigurationEvidenceMissing);
            case ServiceCycleReplayControlKind.StrategyPublished:
                return TryParticipant(participants, step.TraceServiceKey, out var strategy) &&
                    strategy.TryPublishStrategy(step.Generation)
                    ? default : new ControlFailure(
                        strategy?.FirstCycle ?? participants[0].FirstCycle,
                        ServiceCycleReplayExecutionDetailCode.StrategyEvidenceMissing);
            case ServiceCycleReplayControlKind.LifecycleRequested:
                var lifecycleWait = WaitForLifecycleResponses(step, participants, timeout);
                if (lifecycleWait.IsValid) return lifecycleWait;
                pump.RequestLifecycleReplacement(new LifecycleGeneration(step.Generation));
                return default;
            case ServiceCycleReplayControlKind.EmergencyEntered:
                pump.SetEmergencyStop(true, (EmergencyStopReason)step.Code);
                return default;
            case ServiceCycleReplayControlKind.EmergencyCleared:
                pump.SetEmergencyStop(false, (EmergencyStopReason)step.Code);
                return default;
            case ServiceCycleReplayControlKind.PumpCompleted:
                if (pumpPlan is null)
                    return new ControlFailure(participants[0].FirstCycle,
                        ServiceCycleReplayExecutionDetailCode.ControlOrderRejected);
                var timer = Stopwatch.StartNew();
                var readyFailure = WaitForWorkersReady(participants, timeout, timer);
                if (readyFailure.IsValid) return readyFailure;
                var waitFailure = WaitForCycleResponses(pumpPlan, participants, timeout, timer);
                if (waitFailure.IsValid) return waitFailure;
                for (var index = 0; index < participants.Length; index++)
                    participants[index].PreparePump(pumpPlan);
                pump.PumpFrame(step.FrameIdentity);
                return default;
            default:
                return new ControlFailure(
                    participants[0].FirstCycle,
                    ServiceCycleReplayExecutionDetailCode.ControlOrderRejected);
        }
    }

    private static ControlFailure WaitForWorkersReady(
        IServiceCycleReplayProductionParticipant[] participants,
        TimeSpan timeout,
        Stopwatch timer)
    {
        for (var index = 0; index < participants.Length; index++)
        {
            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero || !participants[index].WaitForWorkerReady(remaining))
            {
                return new ControlFailure(
                    participants[index].FirstCycle,
                    ServiceCycleReplayExecutionDetailCode.EvaluatorDidNotFinish);
            }
        }
        return default;
    }

    private static ControlFailure WaitForCycleResponses(
        ServiceCycleReplayPumpPlan pumpPlan,
        IServiceCycleReplayProductionParticipant[] participants,
        TimeSpan timeout,
        Stopwatch timer)
    {
        // Worker footer commits may interleave. CycleStarted identifies the exact response
        // acquired by this pump, and ResponseReady publishes only after that footer commits.
        for (var index = 0; index < pumpPlan.ResponseCycles.Length; index++)
        {
            var cycle = pumpPlan.ResponseCycles[index];
            if (!TryParticipant(participants, cycle.TraceServiceKey, out var participant)) continue;
            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero ||
                !participant.WaitForResponseReadyAndWorkerSettled(cycle, remaining))
            {
                return new ControlFailure(
                    cycle,
                    ServiceCycleReplayExecutionDetailCode.EvaluatorDidNotFinish);
            }
        }
        return default;
    }

    private static ControlFailure WaitForLifecycleResponses(
        ServiceCycleReplayControlStep step,
        IServiceCycleReplayProductionParticipant[] participants,
        TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < step.LifecycleWaitCount; index++)
        {
            var cycle = step.GetLifecycleWait(index);
            if (!TryParticipant(participants, cycle.TraceServiceKey, out var participant)) continue;
            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero ||
                !participant.WaitForResponseReadyAndWorkerSettled(cycle, remaining))
                return new ControlFailure(cycle, ServiceCycleReplayExecutionDetailCode.EvaluatorDidNotFinish);
        }
        return default;
    }

    private readonly struct ControlFailure
    {
        internal ControlFailure(
            ServiceCycleReplayCycleKey cycle,
            ServiceCycleReplayExecutionDetailCode detail)
        {
            Cycle = cycle;
            Detail = detail;
        }

        internal ServiceCycleReplayCycleKey Cycle { get; }
        internal ServiceCycleReplayExecutionDetailCode Detail { get; }
        internal bool IsValid => Cycle.IsValid && Detail != 0;
    }
}
