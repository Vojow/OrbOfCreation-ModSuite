using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed class ServiceRunnerLifetime
{
    private bool _superseded;
    private ServiceCyclePhase _supersededPhase;

    internal bool IsSuperseded => _superseded;
    internal ServiceCyclePhase SupersededPhase => _supersededPhase;

    internal void MarkSuperseded(ServiceCyclePhase phase)
    {
        if (_superseded) return;
        _supersededPhase = phase;
        _superseded = true;
    }
}
