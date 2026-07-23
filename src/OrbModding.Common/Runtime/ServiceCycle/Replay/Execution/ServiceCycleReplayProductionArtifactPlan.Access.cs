using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayProductionArtifactPlan
{
    internal ServiceCycleReplayArtifactDocument Artifact => _artifact;
    internal ServiceCycleTraceDocument Semantic => _artifact.SemanticTrace;
    internal int Capacity { get; }
    internal int TotalCycleCount => _artifact.CycleCount;
    internal LifecycleGeneration InitialLifecycle { get; }
    internal ServiceCycleReplayCycleKey UnsupportedLifecycleConstruction { get; }
    internal ServiceCycleReplayControlBoundaryFailure ControlBoundaryFailure { get; }
    internal ServiceCycleReplayCycleKey DelayedRequestPublication { get; }
    internal ServiceCycleReplayCycleKey PumpTimingFailure { get; }
    internal int CodecVisitCount { get; private set; }
    internal int CycleVisitCount { get; private set; }
    internal int SemanticVisitCount { get; private set; }
    internal int PostProcessVisitCount { get; private set; }
    internal int ControlCount => _controls.Length;
    internal int PumpCount => _pumps.Length;
    internal ServiceCycleReplayControlStep GetControl(int index) => _controls[index];
    internal ServiceCycleReplayPumpPlan GetPump(int index) => _pumps[index];
    internal ServiceCycleReplayServiceEvidence GetService(int traceServiceKey) => _services[traceServiceKey - 1];
    internal int ServiceCycleCount(int traceServiceKey) => _cycleIndices[traceServiceKey - 1].Length;
    internal int GetArtifactCycleIndex(int traceServiceKey, int serviceCycleIndex) =>
        _cycleIndices[traceServiceKey - 1][serviceCycleIndex];
    internal ServiceCycleReplayCycleKey FirstCycle(int traceServiceKey, ServiceCycleReplayCycleKey fallback) =>
        (uint)(traceServiceKey - 1) < (uint)_firstCycles.Length && _firstCycles[traceServiceKey - 1].IsValid
            ? _firstCycles[traceServiceKey - 1] : fallback;
    internal bool HasExactCodecTriplet(int traceServiceKey) => _codecRoles[traceServiceKey - 1] == 7;
    internal int LifecycleCount(int traceServiceKey) => _lifecycles[traceServiceKey - 1].Length;
    internal ulong GetLifecycle(int traceServiceKey, int index) => _lifecycles[traceServiceKey - 1][index];
    internal ServiceCycleReplayNativeOutcomeScript CreateNativeScript(int traceServiceKey) =>
        ServiceCycleReplayNativeOutcomeScript.FromPrepared(_nativeOutcomes[traceServiceKey - 1]);
    internal MonotonicTimestamp[] CopyWorkerSchedule(int traceServiceKey, ulong lifecycle) =>
        _workerSchedules.TryGetValue(new WorkerKey(traceServiceKey, lifecycle), out var values)
            ? (MonotonicTimestamp[])values.Clone() : Array.Empty<MonotonicTimestamp>();
}
