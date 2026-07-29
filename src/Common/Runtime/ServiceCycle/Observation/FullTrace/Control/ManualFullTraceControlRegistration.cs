using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;

public sealed class ManualFullTraceControlRegistration : IDisposable
{
    private ManualFullTraceControlRegistry? _registry;

    internal ManualFullTraceControlRegistration(ManualFullTraceControlRegistry registry) =>
        _registry = registry;

    public bool Publish(ManualFullTraceStatus status) => Registry().Publish(this, status);

    public bool TryTakeCommand(out ManualFullTraceCommand command) =>
        Registry().TryTakeCommand(this, out command);

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        registry.Remove(this);
        _registry = null;
    }

    private ManualFullTraceControlRegistry Registry() =>
        _registry ?? throw new ObjectDisposedException(nameof(ManualFullTraceControlRegistration));
}
