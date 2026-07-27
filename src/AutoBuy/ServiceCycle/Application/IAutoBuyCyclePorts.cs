using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// The native execution boundary for Auto Buy. The worker plans exactly one purchase per
/// cycle as an <see cref="AutoBuyCycleAction"/>; the adapter re-resolves and revalidates that
/// candidate identity natively and submits a single level, returning a neutral action result.
/// </summary>
internal interface IAutoBuyCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoBuyCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}
