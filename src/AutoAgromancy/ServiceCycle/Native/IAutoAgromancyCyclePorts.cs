using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal interface IAutoAgromancyCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoAgromancyCycleAction action,
        in SuiteRuntimeConfiguration configuration,
        in ServiceActionContext context);
}
