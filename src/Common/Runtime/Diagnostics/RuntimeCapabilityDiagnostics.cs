using System;

namespace OrbModding.Common.Runtime;

public readonly struct RuntimeCapabilityDiagnostics : IEquatable<RuntimeCapabilityDiagnostics>
{
    public RuntimeCapabilityDiagnostics(
        string capabilityId,
        string displayName,
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReason reason = default)
    {
        CapabilityId = RequireText(capabilityId, nameof(capabilityId));
        DisplayName = RequireText(displayName, nameof(displayName));
        if (!Enum.IsDefined(typeof(FeatureStatusState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (state == FeatureStatusState.ConfigurationDisabled && configuredEnabled)
            throw new ArgumentException(
                "A configuration-disabled capability cannot be configured enabled.",
                nameof(configuredEnabled));
        if (state != FeatureStatusState.ConfigurationDisabled && !configuredEnabled)
            throw new ArgumentException(
                "Only a configuration-disabled capability can be configured disabled.",
                nameof(configuredEnabled));
        if (state == FeatureStatusState.Operational && !reason.IsEmpty)
            throw new ArgumentException("An operational capability cannot carry a blocking reason.", nameof(reason));
        if (state != FeatureStatusState.Operational && reason.IsEmpty)
            throw new ArgumentException("A non-operational capability requires a structured reason.", nameof(reason));

        ConfiguredEnabled = configuredEnabled;
        State = state;
        Reason = reason;
    }

    public string CapabilityId { get; }
    public string DisplayName { get; }
    public bool ConfiguredEnabled { get; }
    public FeatureStatusState State { get; }
    public FeatureStatusReason Reason { get; }

    public bool Equals(RuntimeCapabilityDiagnostics other) =>
        string.Equals(CapabilityId, other.CapabilityId, StringComparison.Ordinal) &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
        ConfiguredEnabled == other.ConfiguredEnabled &&
        State == other.State &&
        Reason.Equals(other.Reason);

    public override bool Equals(object? obj) => obj is RuntimeCapabilityDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(CapabilityId ?? string.Empty);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(DisplayName ?? string.Empty);
            hash = (hash * 397) ^ ConfiguredEnabled.GetHashCode();
            hash = (hash * 397) ^ (int)State;
            return (hash * 397) ^ Reason.GetHashCode();
        }
    }

    internal static string RequireText(string value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("A stable non-empty value is required.", parameterName);
        return normalized;
    }
}
