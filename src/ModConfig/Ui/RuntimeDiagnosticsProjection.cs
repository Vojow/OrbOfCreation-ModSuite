using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbModConfig;

internal enum RuntimeDiagnosticsSeverity
{
    Healthy = 0,
    Waiting = 1,
    Attention = 2,
    Failure = 3,
}

internal sealed class RuntimeDiagnosticsCard
{
    private readonly List<RuntimeServiceDiagnosticsSnapshot> _runtimeServices;
    private readonly ReadOnlyCollection<RuntimeServiceDiagnosticsSnapshot> _readOnlyRuntimeServices;
    private readonly RuntimeDiagnosticsSeverity _baseSeverity;

    public RuntimeDiagnosticsCard(
        string pluginGuid,
        string displayName,
        string version,
        string schemaText,
        IReadOnlyList<FeatureStatusSnapshot> featureStatuses,
        IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> runtimeServices,
        RuntimeDiagnosticsSeverity baseSeverity)
    {
        PluginGuid = pluginGuid;
        DisplayName = displayName;
        Version = version;
        SchemaText = schemaText;
        FeatureStatuses = featureStatuses;
        _runtimeServices = new List<RuntimeServiceDiagnosticsSnapshot>(runtimeServices.Count);
        for (var index = 0; index < runtimeServices.Count; index++)
            _runtimeServices.Add(runtimeServices[index]);
        _readOnlyRuntimeServices = _runtimeServices.AsReadOnly();
        _baseSeverity = baseSeverity;
        Severity = CalculateSeverity();
        Revision = 1;
    }

    public string PluginGuid { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string SchemaText { get; }
    public IReadOnlyList<FeatureStatusSnapshot> FeatureStatuses { get; }
    public IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> RuntimeServices => _readOnlyRuntimeServices;
    public RuntimeDiagnosticsSeverity Severity { get; private set; }
    public long Revision { get; private set; }

    public bool TryReplaceRuntimeService(RuntimeServiceDiagnosticsSnapshot replacement)
    {
        for (var index = 0; index < _runtimeServices.Count; index++)
        {
            if (!_runtimeServices[index].Key.Equals(replacement.Key)) continue;
            _runtimeServices[index] = replacement;
            Severity = CalculateSeverity();
            Revision = checked(Revision + 1);
            return true;
        }
        return false;
    }

    private RuntimeDiagnosticsSeverity CalculateSeverity()
    {
        var severity = _baseSeverity;
        for (var index = 0; index < _runtimeServices.Count; index++)
        {
            var capabilities = _runtimeServices[index].Capabilities;
            for (var capabilityIndex = 0; capabilityIndex < capabilities.Count; capabilityIndex++)
            {
                severity = RuntimeDiagnosticsProjection.Max(
                    severity,
                    RuntimeDiagnosticsProjection.Severity(capabilities[capabilityIndex].State));
            }
        }
        return severity;
    }
}

internal sealed class RuntimeDiagnosticsDashboard
{
    private readonly List<RuntimeDiagnosticsCard> _cards;
    private readonly ReadOnlyCollection<RuntimeDiagnosticsCard> _readOnlyCards;

    public RuntimeDiagnosticsDashboard(IReadOnlyList<RuntimeDiagnosticsCard> cards)
    {
        _cards = new List<RuntimeDiagnosticsCard>(cards.Count);
        for (var index = 0; index < cards.Count; index++) _cards.Add(cards[index]);
        _readOnlyCards = _cards.AsReadOnly();
        RecountAttention();
    }

    public IReadOnlyList<RuntimeDiagnosticsCard> Cards => _readOnlyCards;
    public int AttentionCount { get; private set; }

    public bool TryApplyChangedRuntime(
        in RuntimeDiagnosticsTransition transition,
        out bool attentionChanged)
    {
        attentionChanged = false;
        if (transition.Kind != RuntimeDiagnosticsTransitionKind.Changed || transition.Current is null)
            return false;
        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            if (!string.Equals(card.PluginGuid, transition.Key.PluginId, StringComparison.Ordinal)) continue;
            var previousAttention = AttentionCount;
            var previousSeverity = card.Severity;
            if (!card.TryReplaceRuntimeService(transition.Current)) return false;
            if (card.Severity != previousSeverity) _cards.Sort(RuntimeDiagnosticsCardComparer.Instance);
            RecountAttention();
            attentionChanged = previousAttention != AttentionCount;
            return true;
        }
        return false;
    }

    private void RecountAttention()
    {
        var count = 0;
        for (var index = 0; index < _cards.Count; index++)
        {
            if (_cards[index].Severity >= RuntimeDiagnosticsSeverity.Attention) count++;
        }
        AttentionCount = count;
    }
}

internal sealed class RuntimeDiagnosticsCardComparer : IComparer<RuntimeDiagnosticsCard>
{
    public static readonly RuntimeDiagnosticsCardComparer Instance = new();

    public int Compare(RuntimeDiagnosticsCard? left, RuntimeDiagnosticsCard? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        var severity = right.Severity.CompareTo(left.Severity);
        if (severity != 0) return severity;
        var name = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        return name != 0
            ? name
            : string.Compare(left.PluginGuid, right.PluginGuid, StringComparison.Ordinal);
    }
}

internal static class RuntimeDiagnosticsProjection
{
    public static RuntimeDiagnosticsDashboard Build(
        ConfigCatalogSnapshot catalog,
        IConfigurationSchemaStatusSource schemaStatuses,
        IFeatureStatusSource featureStatuses,
        IRuntimeDiagnosticsSource runtimeDiagnostics)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (schemaStatuses is null) throw new ArgumentNullException(nameof(schemaStatuses));
        if (featureStatuses is null) throw new ArgumentNullException(nameof(featureStatuses));
        if (runtimeDiagnostics is null) throw new ArgumentNullException(nameof(runtimeDiagnostics));

        var features = featureStatuses.GetSnapshot();
        var services = runtimeDiagnostics.GetSnapshot();
        var plugins = new Dictionary<string, LoadedPluginDescriptor>(StringComparer.Ordinal);
        var featuresByPlugin = new Dictionary<string, List<FeatureStatusSnapshot>>(StringComparer.Ordinal);
        var servicesByPlugin = new Dictionary<string, List<RuntimeServiceDiagnosticsSnapshot>>(StringComparer.Ordinal);
        foreach (var plugin in catalog.LoadedPlugins)
        {
            if (!plugins.ContainsKey(plugin.Guid)) plugins.Add(plugin.Guid, plugin);
        }
        foreach (var feature in features)
        {
            if (!plugins.ContainsKey(feature.Key.PluginId))
                plugins.Add(feature.Key.PluginId, new LoadedPluginDescriptor(
                    feature.Key.PluginId,
                    feature.Key.PluginId,
                    string.Empty));
            if (!featuresByPlugin.TryGetValue(feature.Key.PluginId, out var pluginFeatures))
            {
                pluginFeatures = new List<FeatureStatusSnapshot>();
                featuresByPlugin.Add(feature.Key.PluginId, pluginFeatures);
            }
            pluginFeatures.Add(feature);
        }
        foreach (var service in services)
        {
            if (!plugins.ContainsKey(service.Key.PluginId))
                plugins.Add(service.Key.PluginId, new LoadedPluginDescriptor(
                    service.Key.PluginId,
                    service.Key.PluginId,
                    string.Empty));
            if (!servicesByPlugin.TryGetValue(service.Key.PluginId, out var pluginServices))
            {
                pluginServices = new List<RuntimeServiceDiagnosticsSnapshot>();
                servicesByPlugin.Add(service.Key.PluginId, pluginServices);
            }
            pluginServices.Add(service);
        }

        foreach (var pluginFeatures in featuresByPlugin.Values)
            pluginFeatures.Sort(FeatureComparer.Instance);
        foreach (var pluginServices in servicesByPlugin.Values)
            pluginServices.Sort(ServiceComparer.Instance);

        var cards = new List<RuntimeDiagnosticsCard>(plugins.Count);
        foreach (var plugin in plugins.Values)
        {
            cards.Add(BuildCard(
                plugin,
                schemaStatuses,
                featuresByPlugin.TryGetValue(plugin.Guid, out var pluginFeatures)
                    ? pluginFeatures
                    : Array.Empty<FeatureStatusSnapshot>(),
                servicesByPlugin.TryGetValue(plugin.Guid, out var pluginServices)
                    ? pluginServices
                    : Array.Empty<RuntimeServiceDiagnosticsSnapshot>()));
        }
        cards.Sort(RuntimeDiagnosticsCardComparer.Instance);
        return new RuntimeDiagnosticsDashboard(cards);
    }

    private static RuntimeDiagnosticsCard BuildCard(
        LoadedPluginDescriptor plugin,
        IConfigurationSchemaStatusSource schemaStatuses,
        IReadOnlyList<FeatureStatusSnapshot> features,
        IReadOnlyList<RuntimeServiceDiagnosticsSnapshot> services)
    {
        var severity = SchemaSeverity(plugin.Guid, schemaStatuses);
        foreach (var feature in features) severity = Max(severity, Severity(feature.State));
        return new RuntimeDiagnosticsCard(
            plugin.Guid,
            plugin.Name,
            plugin.Version,
            ConfigurationSchemaStatusProjection.Build(plugin.Guid, schemaStatuses).Text,
            features,
            services,
            severity);
    }

    private sealed class FeatureComparer : IComparer<FeatureStatusSnapshot>
    {
        public static readonly FeatureComparer Instance = new();
        public int Compare(FeatureStatusSnapshot left, FeatureStatusSnapshot right) =>
            string.Compare(left.Key.FeatureId, right.Key.FeatureId, StringComparison.Ordinal);
    }

    private sealed class ServiceComparer : IComparer<RuntimeServiceDiagnosticsSnapshot>
    {
        public static readonly ServiceComparer Instance = new();
        public int Compare(RuntimeServiceDiagnosticsSnapshot? left, RuntimeServiceDiagnosticsSnapshot? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            return string.Compare(left.Key.FeatureId, right.Key.FeatureId, StringComparison.Ordinal);
        }
    }

    private static RuntimeDiagnosticsSeverity SchemaSeverity(
        string pluginGuid,
        IConfigurationSchemaStatusSource source)
    {
        if (!source.TryGet(pluginGuid, out var status)) return RuntimeDiagnosticsSeverity.Healthy;
        return status.State switch
        {
            ConfigurationSchemaState.Failed => RuntimeDiagnosticsSeverity.Failure,
            ConfigurationSchemaState.Future => RuntimeDiagnosticsSeverity.Attention,
            _ => RuntimeDiagnosticsSeverity.Healthy,
        };
    }

    internal static RuntimeDiagnosticsSeverity Severity(FeatureStatusState state) => state switch
    {
        FeatureStatusState.Faulted or FeatureStatusState.ContractUnavailable => RuntimeDiagnosticsSeverity.Failure,
        FeatureStatusState.Degraded => RuntimeDiagnosticsSeverity.Attention,
        FeatureStatusState.Locked or FeatureStatusState.NotReady or FeatureStatusState.TemporarilyBlocked =>
            RuntimeDiagnosticsSeverity.Waiting,
        _ => RuntimeDiagnosticsSeverity.Healthy,
    };

    internal static RuntimeDiagnosticsSeverity Max(
        RuntimeDiagnosticsSeverity left,
        RuntimeDiagnosticsSeverity right) => left >= right ? left : right;
}
