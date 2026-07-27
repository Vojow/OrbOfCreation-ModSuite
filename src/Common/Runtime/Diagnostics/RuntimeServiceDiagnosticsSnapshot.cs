using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OrbModding.Common.Runtime;

public sealed class RuntimeServiceDiagnosticsSnapshot : IEquatable<RuntimeServiceDiagnosticsSnapshot>
{
    private readonly ReadOnlyCollection<RuntimeCapabilityDiagnostics> _capabilities;

    public RuntimeServiceDiagnosticsSnapshot(
        FeatureStatusKey key,
        string displayName,
        string implementation,
        long lifecycleGeneration,
        IReadOnlyList<RuntimeCapabilityDiagnostics> capabilities)
    {
        if (string.IsNullOrWhiteSpace(key.PluginId) || string.IsNullOrWhiteSpace(key.FeatureId))
            throw new ArgumentException("An initialized feature key is required.", nameof(key));
        DisplayName = RuntimeCapabilityDiagnostics.RequireText(displayName, nameof(displayName));
        Implementation = RuntimeCapabilityDiagnostics.RequireText(implementation, nameof(implementation));
        if (lifecycleGeneration < 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));

        var copy = new RuntimeCapabilityDiagnostics[capabilities.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < capabilities.Count; index++)
        {
            var capability = capabilities[index];
            if (!ids.Add(capability.CapabilityId))
                throw new ArgumentException("Capability identifiers must be unique within a service snapshot.", nameof(capabilities));
            copy[index] = capability;
        }

        Key = key;
        LifecycleGeneration = lifecycleGeneration;
        _capabilities = Array.AsReadOnly(copy);
    }

    public FeatureStatusKey Key { get; }
    public string DisplayName { get; }
    public string Implementation { get; }
    public long LifecycleGeneration { get; }
    public IReadOnlyList<RuntimeCapabilityDiagnostics> Capabilities => _capabilities;

    public bool Equals(RuntimeServiceDiagnosticsSnapshot? other)
    {
        if (other is null || !Key.Equals(other.Key) ||
            !string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) ||
            !string.Equals(Implementation, other.Implementation, StringComparison.Ordinal) ||
            LifecycleGeneration != other.LifecycleGeneration ||
            _capabilities.Count != other._capabilities.Count)
            return false;
        for (var index = 0; index < _capabilities.Count; index++)
        {
            if (!_capabilities[index].Equals(other._capabilities[index])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as RuntimeServiceDiagnosticsSnapshot);
    public override int GetHashCode() => HashCode.Combine(Key, DisplayName, Implementation, LifecycleGeneration);
}
