using System;

namespace OrbModding.Common.Runtime;

public sealed class RuntimeDiagnosticsRegistration : IDisposable
{
    private RuntimeDiagnosticsRegistry? _registry;
    private readonly FeatureStatusKey _key;

    internal RuntimeDiagnosticsRegistration(RuntimeDiagnosticsRegistry registry, FeatureStatusKey key)
    {
        _registry = registry;
        _key = key;
    }

    public bool Update(RuntimeServiceDiagnosticsSnapshot snapshot)
    {
        var registry = _registry ?? throw new ObjectDisposedException(nameof(RuntimeDiagnosticsRegistration));
        return registry.Update(_key, snapshot);
    }

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        _registry = null;
        registry.Remove(_key);
    }
}
