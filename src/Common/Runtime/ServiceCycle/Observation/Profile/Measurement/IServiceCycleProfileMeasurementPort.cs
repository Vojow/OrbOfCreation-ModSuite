#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal interface IServiceCycleProfileMeasurementPort
{
    bool TryBegin(
        in ServiceCycleProfileContext context,
        out ServiceCycleProfileMeasurementToken token);

    ServiceCycleProfileMeasurementResult Complete(
        in ServiceCycleProfileMeasurementToken token,
        in ServiceCycleProfileOperationCounters operations);

    ServiceCycleProfileMeasurementResult Abandon(
        in ServiceCycleProfileMeasurementToken token);
}
#endif
