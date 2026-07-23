using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;

internal sealed class ServiceCycleTraceExportSlot
{
    internal const int Free = 0;
    internal const int OwnerClaimed = 1;
    internal const int Ready = 2;
    internal const int WorkerOwned = 3;

    internal ServiceCycleTraceExportSlot(int eventCapacity)
    {
        Events = new ServiceCycleSemanticEvent[eventCapacity];
    }

    internal readonly ServiceCycleSemanticEvent[] Events;
    internal int State;
    internal int EventCount;
    internal int ServiceCapacity;
    internal int Ordinal;
    internal ServiceCycleTraceSessionId Session;
    internal ServiceCycleTraceDropRange Dropped;
}
