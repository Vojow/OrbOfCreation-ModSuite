using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Runs heterogeneous typed participants through one real suite registry and frame pump.</summary>
internal static partial class ServiceCycleReplayProductionCoordinator
{
    internal static ServiceCycleReplayExecutionResult Run(
        ServiceCycleReplayProductionArtifactPlan plan,
        IServiceCycleReplayProductionParticipant[] participants,
        TimeSpan workerBoundaryTimeout,
        ServiceCycleReplayFailureCursor cursor)
    {
        var artifact = plan.Artifact;
        if (participants.Length == 0) throw new InvalidOperationException("Production replay has no services.");
        for (var index = 0; index < participants.Length; index++)
        {
            var participant = participants[index];
            if (!participant.Preparation.Succeeded) return cursor.Complete(participant.Preparation);
        }
        cursor.Enter(
            ServiceCycleReplayExecutionDetailCode.ProductionRegistrationRejected,
            participants[0].FirstCycle);
        var traceMap = new ServiceCycleReplayTraceMap(participants);
        var boundaryFailure = plan.ControlBoundaryFailure;
        if (boundaryFailure.IsValid)
            return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                plan.FirstCycle(boundaryFailure.TraceServiceKey, participants[0].FirstCycle),
                boundaryFailure.Detail));
        var delayedPublication = plan.DelayedRequestPublication;
        if (delayedPublication.IsValid)
            return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                delayedPublication,
                ServiceCycleReplayExecutionDetailCode.ControlOrderRejected));
        var lifecycle = plan.InitialLifecycle;
        if (lifecycle.Value == 0)
            return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                participants[0].FirstCycle,
                ServiceCycleReplayExecutionDetailCode.ControlOrderRejected));
        var timingFailure = plan.PumpTimingFailure;
        if (timingFailure.IsValid)
            return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                timingFailure,
                ServiceCycleReplayExecutionDetailCode.ClockEvidenceRejected));
        var controls = ServiceCycleReplayControlScript.FromPlan(plan);
        var clock = new ServiceCycleReplayClockScript(plan, workerBoundaryTimeout);
        for (var index = 0; index < participants.Length; index++)
            participants[index].RegisterWorkerSchedules(clock, plan, lifecycle);
        var recording = new ServiceCycleReplaySession(
            artifact.SemanticTrace.Session,
            new ServiceCycleReplaySessionOptions(
                true,
                Math.Max(1, artifact.Recording.HighWater.ByteCount),
                Math.Max(1, artifact.Recording.HighWater.RecordCount),
                Math.Max(1, artifact.Recording.HighWater.FooterCount),
                participants.Length));
        using var registry = new ServiceCycleRegistry(participants.Length, lifecycle, clock);
        {
            for (var index = 0; index < participants.Length; index++)
                if (!participants[index].TryRegister(registry, recording))
                    return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                        participants[index].FirstCycle,
                        ServiceCycleReplayExecutionDetailCode.ConfigurationEvidenceMissing));
            registry.Seal();
            var semantic = new ServiceCycleSemanticRecorder(
                artifact.SemanticTrace.Session,
                Math.Max(1, artifact.SemanticTrace.Count),
                participants.Length);
            clock.PrepareConstructor();
            cursor.Enter(
                ServiceCycleReplayExecutionDetailCode.ProductionPumpRejected,
                participants[0].FirstCycle);
            using var pump = new SuiteFramePump(registry, semantic);
            var pumpIndex = 0;
            for (var index = 0; index < controls.Count; index++)
            {
                var step = controls[index];
                var pumpPlan = step.Kind == ServiceCycleReplayControlKind.PumpCompleted
                    ? plan.GetPump(pumpIndex++) : null;
                cursor.Enter(
                    ServiceCycleReplayExecutionDetailCode.ProductionPumpRejected,
                    pumpPlan?.FirstCycle.IsValid == true
                        ? pumpPlan.FirstCycle : plan.FirstCycle(step.TraceServiceKey, participants[0].FirstCycle));
                clock.PrepareControl(step, pumpPlan);
                var failure = ApplyControl(
                    step, pumpPlan, pump, participants,
                    workerBoundaryTimeout);
                if (failure.IsValid)
                    return cursor.Complete(
                        ServiceCycleReplayProductionResult.Fault(failure.Cycle, failure.Detail));
            }
            for (var index = 0; index < participants.Length; index++)
            {
                if (!participants[index].NativeComplete)
                    return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                        participants[index].FirstCycle,
                        ServiceCycleReplayExecutionDetailCode.NativeScriptRejected));
                if (!participants[index].CaptureEvidenceComplete)
                    return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                        participants[index].FirstCycle,
                        ServiceCycleReplayExecutionDetailCode.CaptureEvidenceMissing));
            }
            if (!clock.IsComplete)
            {
                var clockLocation = clock.IncompleteArtifactTraceServiceKey is { } serviceKey
                    ? CycleForService(artifact, serviceKey, participants[0].FirstCycle)
                    : participants[0].FirstCycle;
                return cursor.Complete(ServiceCycleReplayProductionResult.Fault(
                    clockLocation,
                    ServiceCycleReplayExecutionDetailCode.ClockEvidenceRejected));
            }
            cursor.Enter(
                ServiceCycleReplayExecutionDetailCode.ProductionComparisonRejected,
                participants[0].FirstCycle);
            var productionEvidence = ServiceCycleReplayProductionResult.CompareDetached(
                artifact, recording, traceMap, participants[0].FirstCycle, TotalCycles(participants));
            if (!productionEvidence.Succeeded) return cursor.Complete(productionEvidence);
            var actualSemantic = SnapshotSemantic(semantic, participants[0].FirstCycle, out var snapshotFailure);
            if (snapshotFailure.IsValid) return cursor.Complete(snapshotFailure);
            var expectedSemantic = ServiceCycleReplaySemanticProjection.Create(artifact, traceMap);
            var semanticMismatch = ServiceCycleReplaySemanticComparer.Compare(
                participants[0].FirstCycle, expectedSemantic, actualSemantic!);
            if (semanticMismatch.HasValue)
            {
                var runtimeMismatch = semanticMismatch.Value;
                var runtimeCycle = runtimeMismatch.Cycle;
                var artifactCycle = traceMap.ToArtifact(in runtimeCycle);
                var mismatch = new ServiceCycleReplayCycleMismatch(
                    artifactCycle,
                    runtimeMismatch.Mismatch);
                return cursor.Complete(
                    ServiceCycleReplayExecutionResult.Diverged(TotalCycles(participants), in mismatch));
            }
            return cursor.Complete(productionEvidence);
        }
    }

}
