#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbAutomata.Runtime.ServiceCycle.Profile;

internal struct AutoHarvestProfileTemperatureTracker
{
    private bool _initialized;
    private bool _requiresRebind;

    internal ServiceCycleProfileTemperature Current => !_initialized
        ? ServiceCycleProfileTemperature.ColdProcess
        : _requiresRebind
            ? ServiceCycleProfileTemperature.LifecycleRebind
            : ServiceCycleProfileTemperature.Warm;

    internal void InvalidateLifecycle()
    {
        if (_initialized) _requiresRebind = true;
    }

    internal void ObserveUnexpectedDrift()
    {
        if (_initialized) _requiresRebind = true;
    }

    internal bool TryComplete(ServiceCycleProfileTemperature observed)
    {
        if (Current != observed) return false;
        _initialized = true;
        _requiresRebind = false;
        return true;
    }
}
#endif
