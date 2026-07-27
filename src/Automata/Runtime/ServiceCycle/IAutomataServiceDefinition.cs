using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

internal interface IAutomataServiceDefinition<TState, TAction> :
    IServiceCycleDefinition<
        TState,
        TAction>
{
}
