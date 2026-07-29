#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;

public sealed class PerformanceProfileControlRegistration : IDisposable
{
    private PerformanceProfileControlRegistry? _registry;

    internal PerformanceProfileControlRegistration(PerformanceProfileControlRegistry registry) =>
        _registry = registry;

    public bool Publish(PerformanceProfileControlStatus status) => Registry().Publish(this, status);

    public bool TryTakeCommand(out PerformanceProfileCommand command) =>
        Registry().TryTakeCommand(this, out command);

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        registry.Remove(this);
        _registry = null;
    }

    private PerformanceProfileControlRegistry Registry() =>
        _registry ?? throw new ObjectDisposedException(nameof(PerformanceProfileControlRegistration));
}
#endif
