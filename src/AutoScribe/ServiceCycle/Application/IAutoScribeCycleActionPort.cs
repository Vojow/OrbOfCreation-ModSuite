using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal interface IAutoScribeCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoScribeCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}
