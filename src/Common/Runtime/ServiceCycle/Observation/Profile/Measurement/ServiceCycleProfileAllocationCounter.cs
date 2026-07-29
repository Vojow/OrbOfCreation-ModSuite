#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal interface IServiceCycleProfileAllocationCounter
{
    long ReadAllocatedBytes();
}

internal sealed class GcServiceCycleProfileAllocationCounter : IServiceCycleProfileAllocationCounter
{
    internal static readonly GcServiceCycleProfileAllocationCounter Instance = new();

    private GcServiceCycleProfileAllocationCounter()
    {
    }

    public long ReadAllocatedBytes() => GC.GetAllocatedBytesForCurrentThread();
}
#endif
