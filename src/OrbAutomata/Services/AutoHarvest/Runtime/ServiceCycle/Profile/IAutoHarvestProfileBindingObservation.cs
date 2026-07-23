#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbAutomata.Runtime.ServiceCycle.Profile;

internal interface IAutoHarvestProfileBindingObservation
{
    ServiceCycleProfileTemperature CurrentTemperature { get; }
    ServiceCycleProfileTemperature PrepareTemperature();
    bool TryComplete(ServiceCycleProfileTemperature observed);
}
#endif
