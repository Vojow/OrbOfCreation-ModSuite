using System;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbAutomata;

internal interface IAutomataReplayWindow
{
    int EventCount { get; }
    int EventCapacity { get; }
    bool IsComplete { get; }
    ServiceCycleSemanticTraceCloseResult TryFreezeAtSettledBoundary();
    void Discard();
}

internal sealed class AutomataReplayWindow : IAutomataReplayWindow
{
    private readonly ServiceCycleSemanticTraceSource _source;
    private readonly SuiteFramePump _pump;

    internal AutomataReplayWindow(
        ServiceCycleSemanticTraceSource source,
        SuiteFramePump pump)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
    }

    public int EventCount => _source.Count;
    public int EventCapacity => _source.Capacity;
    public bool IsComplete => _source.OverwrittenTotal == 0 && !_source.EmissionFaulted;
    public ServiceCycleSemanticTraceCloseResult TryFreezeAtSettledBoundary() =>
        _pump.TryCloseSemanticTraceAtSettledBoundary();
    public void Discard() => _pump.DiscardSemanticTrace();
}
