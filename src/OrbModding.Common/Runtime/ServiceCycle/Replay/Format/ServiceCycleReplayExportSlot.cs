using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal sealed class ServiceCycleReplayExportSlot
{
    internal const int Free = 0;
    internal const int OwnerClaimed = 1;
    internal const int Ready = 2;
    internal const int WorkerOwned = 3;

    internal ServiceCycleReplayExportSlot(int semanticEventCapacity) =>
        Events = new ServiceCycleSemanticEvent[semanticEventCapacity];

    internal readonly ServiceCycleSemanticEvent[] Events;
    internal int State;
    internal int EventCount;
    internal int Ordinal;
    internal ServiceCycleTraceSessionId SemanticSession;
    internal ServiceCycleTraceDropRange Dropped;
    internal ServiceCycleReplayRecordingSnapshot Recording;
}
