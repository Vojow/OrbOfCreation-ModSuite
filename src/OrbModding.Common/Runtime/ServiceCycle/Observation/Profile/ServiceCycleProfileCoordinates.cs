#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal readonly struct ServiceCycleProfileCoordinates
{
    private readonly bool _initialized;

    internal ServiceCycleProfileCoordinates(int serviceOrdinal, long frameIdentity)
    {
        if (serviceOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(serviceOrdinal));
        if (frameIdentity < 0) throw new ArgumentOutOfRangeException(nameof(frameIdentity));
        ServiceOrdinal = serviceOrdinal;
        Frame = checked((ulong)frameIdentity);
        _initialized = true;
    }

    internal int ServiceOrdinal { get; }
    internal ulong Frame { get; }
    internal bool IsValid => _initialized;

    internal bool TryCreateContext(
        int stageCode,
        ulong lifecycle,
        ulong cycle,
        ServiceCycleProfileTemperature temperature,
        out ServiceCycleProfileContext context)
    {
        if (!_initialized ||
            stageCode <= 0 ||
            temperature is < ServiceCycleProfileTemperature.ColdProcess or
                > ServiceCycleProfileTemperature.Warm)
        {
            context = default;
            return false;
        }
        context = new ServiceCycleProfileContext(
            stageCode,
            ServiceOrdinal,
            lifecycle,
            cycle,
            Frame,
            temperature);
        return true;
    }
}
#endif
