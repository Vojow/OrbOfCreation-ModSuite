using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal interface IAutoHarvestCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoHarvestCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}
