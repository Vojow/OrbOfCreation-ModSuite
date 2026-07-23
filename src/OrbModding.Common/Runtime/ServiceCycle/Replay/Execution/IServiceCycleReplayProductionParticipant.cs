using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal interface IServiceCycleReplayProductionParticipant : IDisposable
{
    int TraceServiceKey { get; }
    int CycleCount { get; }
    ServiceCycleReplayCycleKey FirstCycle { get; }
    ServiceCycleReplayExecutionResult Preparation { get; }
    bool NativeComplete { get; }
    bool CaptureEvidenceComplete { get; }
    bool TryRegister(ServiceCycleRegistry registry, ServiceCycleReplaySession recording);
    void RegisterWorkerSchedules(
        ServiceCycleReplayClockScript clock,
        ServiceCycleReplayProductionArtifactPlan plan,
        LifecycleGeneration initialLifecycle);
    void PreparePump(ServiceCycleReplayPumpPlan pump);
    bool WaitForWorkerReady(TimeSpan timeout);
    bool WaitForResponseReadyAndWorkerSettled(
        ServiceCycleReplayCycleKey expectedCycle,
        TimeSpan timeout);
    bool TryPublishConfiguration(ulong generation);
    bool TryPublishStrategy(ulong generation);
    void DisposeAndWait(TimeSpan workerBoundaryTimeout);
}
