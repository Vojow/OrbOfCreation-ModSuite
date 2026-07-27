using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;

public sealed class HostTraceDumpRegistration : IDisposable
{
    private HostTraceDumpRegistry? _registry;

    internal HostTraceDumpRegistration(HostTraceDumpRegistry registry) => _registry = registry;

    public bool Publish(HostTraceDumpStatus status) => Registry().Publish(this, status);

    public bool TryTakeRequest() => Registry().TryTakeRequest(this);

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        registry.Remove(this);
        _registry = null;
    }

    private HostTraceDumpRegistry Registry() =>
        _registry ?? throw new ObjectDisposedException(nameof(HostTraceDumpRegistration));
}
