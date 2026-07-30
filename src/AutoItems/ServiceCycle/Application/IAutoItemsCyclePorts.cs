using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal interface IAutoItemsCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoItemsCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}
