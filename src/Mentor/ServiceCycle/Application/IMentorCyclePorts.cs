using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbMentor;

internal interface IMentorCycleActionPort
{
    ServiceActionResult TryExecute(
        in MentorCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}
