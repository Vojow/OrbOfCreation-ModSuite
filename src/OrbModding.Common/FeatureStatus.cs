using System;
using System.Collections.Generic;
using System.Threading;

namespace OrbModding.Common;

public enum FeatureStatusState
{
    ConfigurationDisabled = 0,
    Locked = 1,
    NotReady = 2,
    Operational = 3,
    TemporarilyBlocked = 4,
    ContractUnavailable = 5,
    Degraded = 6,
    Faulted = 7,
}

public enum FeatureStatusReasonCode
{
    None = 0,
    ConfigurationDisabled = 100,
    ParentFeatureDisabled = 101,
    EmergencyDisabled = 102,
    ProgressionLocked = 200,
    GameplayNotReady = 300,
    RegistryNotReady = 301,
    LifecycleTransition = 302,
    QueueNotReady = 303,
    Initializing = 304,
    TemporarySafetyBlock = 400,
    QueueFull = 401,
    NativeBusy = 402,
    ManualPause = 403,
    TargetingInProgress = 404,
    CapacityExceeded = 405,
    MutationQuarantined = 406,
    ActionFamilyConflict = 407,
    ContractUnavailable = 500,
    ContractMismatch = 501,
    IdentityMismatch = 502,
    EvidenceUnavailable = 503,
    PartialCapabilityUnavailable = 600,
    NativeMutationFailed = 700,
    PostconditionFailed = 701,
    RuntimeFailure = 702,
    InvariantViolation = 703,
}

public enum FeatureStatusTransitionKind
{
    Added = 0,
    Changed = 1,
    Removed = 2,
}

public readonly struct FeatureStatusKey : IEquatable<FeatureStatusKey>
{
    public FeatureStatusKey(string pluginId, string featureId)
    {
        PluginId = Normalize(pluginId, nameof(pluginId));
        FeatureId = Normalize(featureId, nameof(featureId));
    }

    public string PluginId { get; }
    public string FeatureId { get; }

    public bool Equals(FeatureStatusKey other) =>
        string.Equals(PluginId, other.PluginId, StringComparison.Ordinal) &&
        string.Equals(FeatureId, other.FeatureId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is FeatureStatusKey other && Equals(other);
    public override int GetHashCode() => unchecked(
        (StringComparer.Ordinal.GetHashCode(PluginId ?? string.Empty) * 397) ^
        StringComparer.Ordinal.GetHashCode(FeatureId ?? string.Empty));
    public override string ToString() => (PluginId ?? string.Empty) + "/" + (FeatureId ?? string.Empty);

    private static string Normalize(string value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("A stable status key is required.", parameterName);
        return normalized;
    }
}

public readonly struct FeatureStatusReason : IEquatable<FeatureStatusReason>
{
    public FeatureStatusReason(
        FeatureStatusReasonCode code,
        string summary,
        AutomationEntityIdentity relatedEntity = default)
    {
        if (!Enum.IsDefined(typeof(FeatureStatusReasonCode), code))
            throw new ArgumentOutOfRangeException(nameof(code));
        var normalizedSummary = (summary ?? string.Empty).Trim();
        if (code == FeatureStatusReasonCode.None && normalizedSummary.Length > 0)
            throw new ArgumentException("An operational status without a reason cannot carry a reason summary.", nameof(summary));
        if (code != FeatureStatusReasonCode.None && normalizedSummary.Length == 0)
            throw new ArgumentException("A non-empty reason summary is required.", nameof(summary));
        Code = code;
        Summary = normalizedSummary;
        RelatedEntity = relatedEntity;
    }

    public FeatureStatusReasonCode Code { get; }
    public string Summary { get; }
    public AutomationEntityIdentity RelatedEntity { get; }
    public bool IsEmpty => Code == FeatureStatusReasonCode.None;

    public bool Equals(FeatureStatusReason other) =>
        Code == other.Code &&
        string.Equals(Summary, other.Summary, StringComparison.Ordinal) &&
        RelatedEntity.Equals(other.RelatedEntity);
    public override bool Equals(object? obj) => obj is FeatureStatusReason other && Equals(other);
    public override int GetHashCode() => unchecked(
        (((int)Code * 397) ^ StringComparer.Ordinal.GetHashCode(Summary ?? string.Empty)) * 397 ^
        RelatedEntity.GetHashCode());
}

public readonly struct FeatureStatusSnapshot : IEquatable<FeatureStatusSnapshot>
{
    public FeatureStatusSnapshot(
        FeatureStatusKey key,
        string displayName,
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReason reason = default,
        long lifecycleGeneration = 0)
    {
        if (string.IsNullOrWhiteSpace(key.PluginId) || string.IsNullOrWhiteSpace(key.FeatureId))
            throw new ArgumentException("An initialized feature key is required.", nameof(key));
        var normalizedDisplayName = (displayName ?? string.Empty).Trim();
        if (normalizedDisplayName.Length == 0) throw new ArgumentException("A feature display name is required.", nameof(displayName));
        if (!Enum.IsDefined(typeof(FeatureStatusState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (lifecycleGeneration < 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (state == FeatureStatusState.ConfigurationDisabled && configuredEnabled)
            throw new ArgumentException("A configuration-disabled status cannot be configured enabled.", nameof(configuredEnabled));
        if (state != FeatureStatusState.ConfigurationDisabled && !configuredEnabled)
            throw new ArgumentException("Only configuration-disabled status can be configured disabled.", nameof(configuredEnabled));
        if (state == FeatureStatusState.Operational && !reason.IsEmpty)
            throw new ArgumentException("Operational status cannot carry a blocking reason.", nameof(reason));
        if (state != FeatureStatusState.Operational && reason.IsEmpty)
            throw new ArgumentException("A non-operational status requires a structured reason.", nameof(reason));

        Key = key;
        DisplayName = normalizedDisplayName;
        ConfiguredEnabled = configuredEnabled;
        State = state;
        Reason = reason;
        LifecycleGeneration = lifecycleGeneration;
    }

    public FeatureStatusKey Key { get; }
    public string DisplayName { get; }
    public bool ConfiguredEnabled { get; }
    public FeatureStatusState State { get; }
    public FeatureStatusReason Reason { get; }
    public long LifecycleGeneration { get; }
    public FeatureStatusConditionKey ConditionKey => new(this);

    public bool Equals(FeatureStatusSnapshot other) =>
        Key.Equals(other.Key) &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
        ConfiguredEnabled == other.ConfiguredEnabled && State == other.State &&
        Reason.Equals(other.Reason) && LifecycleGeneration == other.LifecycleGeneration;
    public override bool Equals(object? obj) => obj is FeatureStatusSnapshot other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Key.GetHashCode();
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(DisplayName ?? string.Empty);
            hash = (hash * 397) ^ ConfiguredEnabled.GetHashCode();
            hash = (hash * 397) ^ (int)State;
            hash = (hash * 397) ^ Reason.GetHashCode();
            hash = (hash * 397) ^ LifecycleGeneration.GetHashCode();
            return hash;
        }
    }
}

public readonly struct FeatureStatusConditionKey : IEquatable<FeatureStatusConditionKey>
{
    private readonly FeatureStatusSnapshot _status;
    internal FeatureStatusConditionKey(FeatureStatusSnapshot status) => _status = status;
    public bool Equals(FeatureStatusConditionKey other) =>
        _status.Key.Equals(other._status.Key) &&
        _status.ConfiguredEnabled == other._status.ConfiguredEnabled &&
        _status.State == other._status.State &&
        _status.Reason.Code == other._status.Reason.Code &&
        _status.Reason.RelatedEntity.Equals(other._status.Reason.RelatedEntity) &&
        _status.LifecycleGeneration == other._status.LifecycleGeneration;
    public override bool Equals(object? obj) => obj is FeatureStatusConditionKey other && Equals(other);
    public override int GetHashCode() => unchecked(
        (((((_status.Key.GetHashCode() * 397) ^ _status.ConfiguredEnabled.GetHashCode()) * 397 ^
            (int)_status.State) * 397 ^ (int)_status.Reason.Code) * 397 ^
          _status.Reason.RelatedEntity.GetHashCode()) * 397 ^ _status.LifecycleGeneration.GetHashCode());
}

public readonly struct FeatureStatusTransition
{
    internal FeatureStatusTransition(
        FeatureStatusTransitionKind kind,
        FeatureStatusSnapshot? previous,
        FeatureStatusSnapshot? current,
        long sequence)
    {
        Kind = kind;
        Previous = previous;
        Current = current;
        Sequence = sequence;
    }

    public FeatureStatusTransitionKind Kind { get; }
    public FeatureStatusSnapshot? Previous { get; }
    public FeatureStatusSnapshot? Current { get; }
    public long Sequence { get; }
}

public interface IFeatureStatusSource
{
    event Action<FeatureStatusTransition>? Transitioned;
    IReadOnlyList<FeatureStatusSnapshot> GetSnapshot();
}

public sealed class FeatureStatusRegistry : IFeatureStatusSource
{
    private readonly int _ownerThreadId;
    private readonly Dictionary<FeatureStatusKey, FeatureStatusSnapshot> _statuses = new();
    private long _sequence;

    public FeatureStatusRegistry() => _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    public static FeatureStatusRegistry Shared { get; } = new();

    public event Action<FeatureStatusTransition>? Transitioned;

    public FeatureStatusRegistration Register(FeatureStatusSnapshot initialStatus)
    {
        AssertOwnerThread();
        if (_statuses.ContainsKey(initialStatus.Key))
            throw new InvalidOperationException("A feature status publisher already owns " + initialStatus.Key + ".");
        _statuses.Add(initialStatus.Key, initialStatus);
        Publish(new FeatureStatusTransition(
            FeatureStatusTransitionKind.Added,
            null,
            initialStatus,
            checked(++_sequence)));
        return new FeatureStatusRegistration(this, initialStatus.Key);
    }

    public IReadOnlyList<FeatureStatusSnapshot> GetSnapshot()
    {
        AssertOwnerThread();
        var result = new List<FeatureStatusSnapshot>(_statuses.Values);
        result.Sort(StatusComparer.Instance);
        return result;
    }

    public bool TryGet(FeatureStatusKey key, out FeatureStatusSnapshot status)
    {
        AssertOwnerThread();
        return _statuses.TryGetValue(key, out status);
    }

    internal bool Update(FeatureStatusKey key, FeatureStatusSnapshot status)
    {
        AssertOwnerThread();
        if (!key.Equals(status.Key)) throw new ArgumentException("A registration cannot change its feature key.", nameof(status));
        if (!_statuses.TryGetValue(key, out var previous))
            throw new ObjectDisposedException(nameof(FeatureStatusRegistration));
        if (previous.ConditionKey.Equals(status.ConditionKey)) return false;
        _statuses[key] = status;
        Publish(new FeatureStatusTransition(
            FeatureStatusTransitionKind.Changed,
            previous,
            status,
            checked(++_sequence)));
        return true;
    }

    internal void Remove(FeatureStatusKey key)
    {
        AssertOwnerThread();
        if (!_statuses.Remove(key, out var previous)) return;
        Publish(new FeatureStatusTransition(
            FeatureStatusTransitionKind.Removed,
            previous,
            null,
            checked(++_sequence)));
    }

    private void Publish(FeatureStatusTransition transition)
    {
        var handlers = Transitioned;
        if (handlers is null) return;
        foreach (Action<FeatureStatusTransition> handler in handlers.GetInvocationList())
        {
            try { handler(transition); }
            catch { }
        }
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Feature status registry access must remain on its owning main thread.");
    }

    private sealed class StatusComparer : IComparer<FeatureStatusSnapshot>
    {
        public static readonly StatusComparer Instance = new();
        public int Compare(FeatureStatusSnapshot left, FeatureStatusSnapshot right)
        {
            var plugin = string.Compare(left.Key.PluginId, right.Key.PluginId, StringComparison.Ordinal);
            return plugin != 0
                ? plugin
                : string.Compare(left.Key.FeatureId, right.Key.FeatureId, StringComparison.Ordinal);
        }
    }
}

public sealed class FeatureStatusRegistration : IDisposable
{
    private FeatureStatusRegistry? _registry;
    private readonly FeatureStatusKey _key;

    internal FeatureStatusRegistration(FeatureStatusRegistry registry, FeatureStatusKey key)
    {
        _registry = registry;
        _key = key;
    }

    public bool Update(FeatureStatusSnapshot status)
    {
        var registry = _registry ?? throw new ObjectDisposedException(nameof(FeatureStatusRegistration));
        return registry.Update(_key, status);
    }

    public void Dispose()
    {
        var registry = _registry;
        if (registry is null) return;
        _registry = null;
        registry.Remove(_key);
    }
}

public static class FeatureStatusPresenter
{
    public static string Format(in FeatureStatusSnapshot status)
    {
        var configured = status.ConfiguredEnabled ? "Enabled" : "Disabled";
        return status.Reason.IsEmpty
            ? $"Configured: {configured}; Runtime: {Label(status.State)}"
            : $"Configured: {configured}; Runtime: {Label(status.State)} - {status.Reason.Summary}";
    }

    public static string Label(FeatureStatusState state) => state switch
    {
        FeatureStatusState.ConfigurationDisabled => "Off",
        FeatureStatusState.Locked => "Locked",
        FeatureStatusState.NotReady => "Not ready",
        FeatureStatusState.Operational => "Operational",
        FeatureStatusState.TemporarilyBlocked => "Temporarily blocked",
        FeatureStatusState.ContractUnavailable => "Contract unavailable",
        FeatureStatusState.Degraded => "Degraded",
        FeatureStatusState.Faulted => "Faulted",
        _ => "Unknown",
    };
}
