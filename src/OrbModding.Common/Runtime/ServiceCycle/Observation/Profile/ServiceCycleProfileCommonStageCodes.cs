#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal static class ServiceCycleProfileCommonStageCodes
{
    internal const int DetachedInputConstruction = 1;
    internal const int DetachedInputBridgePublication = 2;
    internal const int SemanticStart = 3;
    internal const int SemanticTerminal = 4;
    internal const int SemanticPumpSummary = 5;
    internal const int OverallPump = 6;
}
#endif
