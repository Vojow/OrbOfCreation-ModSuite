using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>Translates execution facts into semantic events.</summary>
internal sealed partial class ServiceCycleSemanticExecutionEvents
{
    private readonly ServiceCycleSemanticRecorder _recorder;
    private readonly ServiceCycleSemanticPublicationEvents _publications;
    private readonly ServiceCycleSemanticTraceState _state;

    internal ServiceCycleSemanticExecutionEvents(
        ServiceCycleSemanticRecorder recorder,
        ServiceCycleSemanticPublicationEvents publications,
        ServiceCycleSemanticTraceState state)
    {
        _recorder = recorder;
        _publications = publications;
        _state = state;
    }
}
