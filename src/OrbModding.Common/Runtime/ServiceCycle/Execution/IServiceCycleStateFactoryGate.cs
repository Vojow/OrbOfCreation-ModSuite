namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Optional offline-tooling gate around reference-state construction. Ordinary runtime clocks do
/// not implement this contract, so gameplay retains nonblocking factory contention semantics.
/// </summary>
internal interface IServiceCycleStateFactoryGate
{
    void EnterStateFactory();
    void ExitStateFactory();
}
