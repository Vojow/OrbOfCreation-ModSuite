using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed class ServiceCycleReplayPumpPlan
{
    internal ServiceCycleReplayPumpPlan(
        int start,
        int end,
        ServiceCycleSemanticEvent pump,
        ServiceCycleReplayCycleKey first,
        ServiceCycleReplayCycleKey[] responses,
        ulong[] startSequences,
        MonotonicTimestamp[] ownerClock,
        ServiceCycleReplayControlBoundaryFailure boundary,
        ServiceCycleReplayCycleKey delayed,
        ServiceCycleReplayCycleKey timing)
    {
        SemanticStartIndex = start;
        SemanticEndIndex = end;
        Pump = pump;
        FirstCycle = first;
        ResponseCycles = responses;
        StartSequences = startSequences;
        OwnerClock = ownerClock;
        BoundaryFailure = boundary;
        DelayedPublication = delayed;
        TimingFailure = timing;
    }

    internal int SemanticStartIndex { get; }
    internal int SemanticEndIndex { get; }
    internal ServiceCycleSemanticEvent Pump { get; }
    internal ServiceCycleReplayCycleKey FirstCycle { get; }
    internal ServiceCycleReplayCycleKey[] ResponseCycles { get; }
    private ulong[] StartSequences { get; }
    private MonotonicTimestamp[] OwnerClock { get; }
    internal ServiceCycleReplayControlBoundaryFailure BoundaryFailure { get; }
    internal ServiceCycleReplayCycleKey DelayedPublication { get; }
    internal ServiceCycleReplayCycleKey TimingFailure { get; }

    internal ulong StartSequence(int traceServiceKey) => StartSequences[traceServiceKey - 1];
    internal MonotonicTimestamp[] CopyOwnerClock() => (MonotonicTimestamp[])OwnerClock.Clone();
}
