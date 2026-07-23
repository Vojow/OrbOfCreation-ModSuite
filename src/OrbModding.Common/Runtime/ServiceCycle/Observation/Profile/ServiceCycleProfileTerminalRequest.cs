#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal sealed class ServiceCycleProfileTerminalRequest
{
    private ServiceCycleProfileTerminalReason _reason;

    internal void Set(ServiceCycleProfileTerminalReason reason)
    {
        if (reason is not (ServiceCycleProfileTerminalReason.UserStopped or
            ServiceCycleProfileTerminalReason.RuntimeShutdown or
            ServiceCycleProfileTerminalReason.ProbeFailed))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (_reason != 0) throw new InvalidOperationException("The profile terminal reason is already set.");
        _reason = reason;
    }

    internal ServiceCycleProfileTerminalReason GetRequired() => _reason == 0
        ? throw new InvalidOperationException("A complete profile requires a terminal reason.")
        : _reason;
}
#endif
