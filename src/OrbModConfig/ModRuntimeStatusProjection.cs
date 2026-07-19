using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbModConfig;

internal sealed class ModRuntimeStatusProjection
{
    private ModRuntimeStatusProjection(string pluginId, IReadOnlyList<FeatureStatusSnapshot> features)
    {
        PluginId = pluginId;
        Features = features;
    }

    public string PluginId { get; }
    public IReadOnlyList<FeatureStatusSnapshot> Features { get; }

    public static ModRuntimeStatusProjection Build(
        string pluginId,
        IReadOnlyList<FeatureStatusSnapshot> statuses)
    {
        var normalizedPluginId = (pluginId ?? string.Empty).Trim();
        if (normalizedPluginId.Length == 0)
            throw new ArgumentException("A plugin GUID is required.", nameof(pluginId));

        var features = new List<FeatureStatusSnapshot>();
        for (var index = 0; index < statuses.Count; index++)
        {
            var status = statuses[index];
            if (string.Equals(status.Key.PluginId, normalizedPluginId, StringComparison.Ordinal))
                features.Add(status);
        }
        features.Sort(FeatureComparer.Instance);
        return new ModRuntimeStatusProjection(normalizedPluginId, features);
    }

    public string FormatCompact()
    {
        if (Features.Count == 0) return "Runtime status: Not reported by this plugin.";

        var parts = new string[Features.Count];
        for (var index = 0; index < Features.Count; index++)
        {
            var status = Features[index];
            parts[index] = status.DisplayName + ": " + FeatureStatusPresenter.Label(status.State) +
                (status.Reason.IsEmpty ? string.Empty : " - " + status.Reason.Summary);
        }
        return "Runtime status: " + string.Join(" | ", parts);
    }

    private sealed class FeatureComparer : IComparer<FeatureStatusSnapshot>
    {
        public static readonly FeatureComparer Instance = new();

        public int Compare(FeatureStatusSnapshot left, FeatureStatusSnapshot right) =>
            string.Compare(left.Key.FeatureId, right.Key.FeatureId, StringComparison.Ordinal);
    }
}
