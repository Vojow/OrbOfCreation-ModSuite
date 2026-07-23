using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum AutoHarvestCycleCaptureDisposition
{
    Captured = 1,
    Unavailable = 2,
}

internal interface IAutoHarvestCycleCapturePort
{
    AutoHarvestCycleCaptureDisposition Capture(
        in AutomataConfiguration config,
        LifecycleGeneration lifecycle,
#if SERVICE_CYCLE_PROFILE
        in ServiceCaptureContext profileContext,
#endif
        out AutoHarvestCycleFrame frame);
}

internal interface IAutoHarvestCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoHarvestCycleAction action,
        in AutomataConfiguration config,
        in ServiceActionContext context);
}
