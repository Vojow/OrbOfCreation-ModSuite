using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal interface IAutoConceptCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoConceptCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}
