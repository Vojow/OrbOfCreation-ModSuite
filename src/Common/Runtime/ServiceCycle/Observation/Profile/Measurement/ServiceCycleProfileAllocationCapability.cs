#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal readonly struct ServiceCycleProfileAllocationCapability
{
    private const int WitnessBytes = 256;
    private readonly IServiceCycleProfileAllocationCounter? _counter;

    private ServiceCycleProfileAllocationCapability(
        IServiceCycleProfileAllocationCounter counter,
        int ownerThreadId,
        bool isAvailable)
    {
        _counter = counter;
        OwnerThreadId = ownerThreadId;
        IsAvailable = isAvailable;
    }

    internal int OwnerThreadId { get; }
    internal bool IsAvailable { get; }
    internal bool IsValid => _counter is not null && OwnerThreadId > 0;

    internal static ServiceCycleProfileAllocationCapability Probe(
        IServiceCycleProfileAllocationCounter counter)
    {
        if (counter is null) throw new ArgumentNullException(nameof(counter));
        var ownerThreadId = Environment.CurrentManagedThreadId;
        try
        {
            if (counter.ReadAllocatedBytes() < 0)
                throw new InvalidOperationException("The allocation counter returned a negative value during warmup.");
            var before = counter.ReadAllocatedBytes();
            if (before < 0)
                throw new InvalidOperationException("The allocation counter returned a negative baseline.");
            var witness = new byte[WitnessBytes];
            witness[0] = 1;
            var after = counter.ReadAllocatedBytes();
            GC.KeepAlive(witness);
            if (after < 0)
                throw new InvalidOperationException("The allocation counter returned a negative calibrated value.");
            if (after < before)
                throw new InvalidOperationException("The allocation counter moved backwards during calibration.");
            return new ServiceCycleProfileAllocationCapability(
                counter,
                ownerThreadId,
                checked(after - before) >= WitnessBytes);
        }
        catch (PlatformNotSupportedException)
        {
            return new ServiceCycleProfileAllocationCapability(counter, ownerThreadId, isAvailable: false);
        }
        catch (NotImplementedException)
        {
            return new ServiceCycleProfileAllocationCapability(counter, ownerThreadId, isAvailable: false);
        }
    }

    internal long ReadAllocatedBytes()
    {
        if (!IsValid || !IsAvailable)
            throw new InvalidOperationException("The allocation capability is unavailable.");
        return _counter!.ReadAllocatedBytes();
    }
}
#endif
