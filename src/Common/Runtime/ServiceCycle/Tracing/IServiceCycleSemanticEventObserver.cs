namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

internal interface IServiceCycleSemanticEventObserver
{
    void Observe(in ServiceCycleSemanticEvent item);
}
