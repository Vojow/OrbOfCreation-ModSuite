#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal readonly struct ServiceCycleProfileContext
{
    internal ServiceCycleProfileContext(
        int stageCode,
        int serviceOrdinal,
        ulong lifecycle,
        ulong cycle,
        ulong frame,
        ServiceCycleProfileTemperature temperature)
    {
        if (stageCode <= 0) throw new ArgumentOutOfRangeException(nameof(stageCode));
        if (serviceOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(serviceOrdinal));
        if (temperature is < ServiceCycleProfileTemperature.ColdProcess or > ServiceCycleProfileTemperature.Warm)
            throw new ArgumentOutOfRangeException(nameof(temperature));
        StageCode = stageCode;
        ServiceOrdinal = serviceOrdinal;
        Lifecycle = lifecycle;
        Cycle = cycle;
        Frame = frame;
        Temperature = temperature;
    }

    internal int StageCode { get; }
    internal int ServiceOrdinal { get; }
    internal ulong Lifecycle { get; }
    internal ulong Cycle { get; }
    internal ulong Frame { get; }
    internal ServiceCycleProfileTemperature Temperature { get; }
    internal bool IsValid => StageCode > 0 && ServiceOrdinal >= 0 &&
        Temperature is >= ServiceCycleProfileTemperature.ColdProcess and <= ServiceCycleProfileTemperature.Warm;
}
#endif
