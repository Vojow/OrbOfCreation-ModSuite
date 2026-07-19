using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OrbModding.Common;

public enum AutomationDecisionDisposition
{
    Accepted = 0,
    Rejected = 1,
    Deferred = 2,
    Skipped = 3,
    Dropped = 4,
    Failed = 5,
}

/// <summary>
/// Stable append-only automation decision codes. Numeric values are part of the
/// public diagnostics contract consumed by tests, UI, logs, and future Insights.
/// </summary>
public enum AutomationDecisionCode
{
    None = 0,

    Eligible = 100,
    ConfigurationDisabled = 101,
    PolicyExcluded = 102,
    Locked = 103,
    Unavailable = 104,
    AlreadyActive = 105,
    DuplicateScheduled = 106,

    ContractUnresolved = 200,
    RegistryNotReady = 201,
    IdentityUnavailable = 202,
    IdentityChanged = 203,
    WrongNativeType = 204,
    NativeStateUnavailable = 205,
    NativeAdmissionRejected = 206,
    MutationQuarantined = 207,
    NativeMutationFailed = 208,
    PostconditionFailed = 209,

    CostUnavailable = 300,
    InvalidConfiguration = 301,
    InvalidResourceState = 302,
    InsufficientResource = 303,
    ReserveFloor = 304,
    AffordabilityThreshold = 305,
    DrainUnsafe = 306,
    ResourceStartThreshold = 307,

    QueueUnavailable = 400,
    QueueFull = 401,
    QueuePolicyLimit = 402,
    QueueBatchLimit = 403,
    TargetUnavailable = 410,
    TargetInvalid = 411,
    TargetingInProgress = 412,

    BudgetDeferred = 500,
    WaitingForTurn = 501,
    ScanLimitDeferred = 502,
    LifecycleChanged = 503,
    ManualPause = 504,
    NativeBusy = 505,

    SourceIneligible = 600,
    NoEligibleTargets = 601,
    ZeroEffect = 602,
    CapacityOverflow = 603,
}

[Flags]
public enum AutomationRetryTrigger
{
    None = 0,
    Configuration = 1 << 0,
    Lifecycle = 1 << 1,
    Registry = 1 << 2,
    Progression = 1 << 3,
    ResourceQuantity = 1 << 4,
    ResourceRate = 1 << 5,
    Queue = 1 << 6,
    Inventory = 1 << 7,
    Targeting = 1 << 8,
    SchedulerTurn = 1 << 9,
    ManualInput = 1 << 10,
}

public enum AutomationResourceConstraintKind
{
    Cost = 0,
    InsufficientQuantity = 1,
    ReserveFloor = 2,
    AffordabilityThreshold = 3,
    Drain = 4,
    StartFullness = 5,
}

public enum AutomationNativeStateCode
{
    None = 0,
    Unknown = 1,
    NotReady = 2,
    Unavailable = 3,
    Locked = 4,
    Busy = 5,
    AlreadyQueued = 6,
    Completed = 7,
    IdentityMismatch = 8,
    ContractMismatch = 9,
}

public readonly struct AutomationScientificValue : IEquatable<AutomationScientificValue>, IComparable<AutomationScientificValue>
{
    public AutomationScientificValue(double mantissa, long exponent)
    {
        if (double.IsNaN(mantissa) || double.IsInfinity(mantissa))
            throw new ArgumentOutOfRangeException(nameof(mantissa));
        if (mantissa == 0.0)
        {
            Mantissa = 0.0;
            Exponent = 0;
            return;
        }

        var sign = Math.Sign(mantissa);
        mantissa = Math.Abs(mantissa);
        while (mantissa >= 10.0)
        {
            mantissa /= 10.0;
            exponent = checked(exponent + 1);
        }
        while (mantissa < 1.0)
        {
            mantissa *= 10.0;
            exponent = checked(exponent - 1);
        }
        Mantissa = mantissa * sign;
        Exponent = exponent;
    }

    public double Mantissa { get; }
    public long Exponent { get; }
    public bool IsZero => Mantissa == 0.0;

    public int CompareTo(AutomationScientificValue other)
    {
        if (IsZero && other.IsZero) return 0;
        if (IsZero) return other.Mantissa > 0.0 ? -1 : 1;
        if (other.IsZero) return Mantissa > 0.0 ? 1 : -1;
        if (Math.Sign(Mantissa) != Math.Sign(other.Mantissa)) return Mantissa.CompareTo(other.Mantissa);
        var exponent = Exponent.CompareTo(other.Exponent);
        if (Mantissa < 0.0) exponent = -exponent;
        return exponent != 0 ? exponent : Mantissa.CompareTo(other.Mantissa);
    }

    public bool Equals(AutomationScientificValue other) =>
        Mantissa.Equals(other.Mantissa) && Exponent == other.Exponent;

    public override bool Equals(object? obj) => obj is AutomationScientificValue other && Equals(other);
    public override int GetHashCode() => unchecked((Mantissa.GetHashCode() * 397) ^ Exponent.GetHashCode());
    public override string ToString() => IsZero
        ? "0"
        : Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" + Exponent.ToString(CultureInfo.InvariantCulture);
}

public readonly struct AutomationEntityIdentity : IEquatable<AutomationEntityIdentity>
{
    public AutomationEntityIdentity(
        string domain,
        string stableId,
        string expectedNativeType = "",
        string displayName = "")
    {
        var normalizedDomain = (domain ?? string.Empty).Trim();
        var normalizedStableId = (stableId ?? string.Empty).Trim();
        var normalizedExpectedType = (expectedNativeType ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(domain) && normalizedDomain.Length == 0)
            throw new ArgumentException("An entity domain cannot contain only whitespace.", nameof(domain));
        if (!string.IsNullOrEmpty(stableId) && normalizedStableId.Length == 0)
            throw new ArgumentException("A stable entity ID cannot contain only whitespace.", nameof(stableId));
        if (!string.IsNullOrEmpty(expectedNativeType) && normalizedExpectedType.Length == 0)
            throw new ArgumentException("An expected native type cannot contain only whitespace.", nameof(expectedNativeType));
        if (Guid.TryParseExact(normalizedStableId, "D", out var nativeUuid))
            normalizedStableId = nativeUuid.ToString("D");

        Domain = normalizedDomain;
        StableId = normalizedStableId;
        ExpectedNativeType = normalizedExpectedType;
        DisplayName = displayName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Domain) && !string.IsNullOrEmpty(StableId))
            throw new ArgumentException("A stable entity ID requires a domain.", nameof(domain));
        if (string.IsNullOrWhiteSpace(StableId) && !string.IsNullOrEmpty(ExpectedNativeType))
            throw new ArgumentException("An expected native type requires a stable entity ID.", nameof(expectedNativeType));
        if (Guid.TryParse(StableId, out _))
        {
            if (!Guid.TryParseExact(StableId, "D", out _))
                throw new ArgumentException("A native UUID must use canonical D format.", nameof(stableId));
            if (string.IsNullOrWhiteSpace(ExpectedNativeType))
                throw new ArgumentException("A native UUID requires its expected native type.", nameof(expectedNativeType));
        }
    }

    public string Domain { get; }
    public string StableId { get; }
    public string ExpectedNativeType { get; }
    public string DisplayName { get; }
    public bool IsEmpty => string.IsNullOrEmpty(Domain) && string.IsNullOrEmpty(StableId);

    public bool Equals(AutomationEntityIdentity other) =>
        string.Equals(Domain, other.Domain, StringComparison.Ordinal) &&
        string.Equals(StableId, other.StableId, StringComparison.Ordinal) &&
        string.Equals(ExpectedNativeType, other.ExpectedNativeType, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AutomationEntityIdentity other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(Domain ?? string.Empty);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(StableId ?? string.Empty);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ExpectedNativeType ?? string.Empty);
            return hash;
        }
    }
}

public readonly struct AutomationResourceConstraint : IEquatable<AutomationResourceConstraint>
{
    public AutomationResourceConstraint(
        AutomationResourceConstraintKind kind,
        AutomationEntityIdentity resource,
        AutomationScientificValue cost,
        AutomationScientificValue observed,
        AutomationScientificValue required,
        bool isBandwidth = false)
    {
        if (kind < AutomationResourceConstraintKind.Cost ||
            kind > AutomationResourceConstraintKind.StartFullness)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (resource.IsEmpty) throw new ArgumentException("A resource identity is required.", nameof(resource));
        Kind = kind;
        Resource = resource;
        Cost = cost;
        Observed = observed;
        Required = required;
        IsBandwidth = isBandwidth;
    }

    public AutomationResourceConstraintKind Kind { get; }
    public AutomationEntityIdentity Resource { get; }
    public AutomationScientificValue Cost { get; }
    public AutomationScientificValue Observed { get; }
    public AutomationScientificValue Required { get; }
    public bool IsBandwidth { get; }

    public bool Equals(AutomationResourceConstraint other) =>
        Kind == other.Kind && Resource.Equals(other.Resource) && Cost.Equals(other.Cost) &&
        Observed.Equals(other.Observed) && Required.Equals(other.Required) && IsBandwidth == other.IsBandwidth;

    public override bool Equals(object? obj) => obj is AutomationResourceConstraint other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Kind;
            hash = (hash * 397) ^ Resource.GetHashCode();
            hash = (hash * 397) ^ Cost.GetHashCode();
            hash = (hash * 397) ^ Observed.GetHashCode();
            hash = (hash * 397) ^ Required.GetHashCode();
            hash = (hash * 397) ^ IsBandwidth.GetHashCode();
            return hash;
        }
    }
}

public readonly struct AutomationQueueDetail : IEquatable<AutomationQueueDetail>
{
    private AutomationQueueDetail(
        int nativeCapacity,
        int liveOccupancy,
        int nativeRemainingRoom,
        int automationLimit,
        int manualReservation,
        int usableRoom,
        int requestedCount,
        QueueCapacityInvalidReason invalidReason = QueueCapacityInvalidReason.None)
    {
        NativeCapacity = nativeCapacity;
        LiveOccupancy = liveOccupancy;
        NativeRemainingRoom = nativeRemainingRoom;
        AutomationLimit = automationLimit;
        ManualReservation = manualReservation;
        UsableRoom = usableRoom;
        RequestedCount = requestedCount;
        InvalidReason = invalidReason;
    }

    public static AutomationQueueDetail FromSnapshot(QueueCapacitySnapshot snapshot, int requestedCount)
    {
        if (requestedCount < 0) throw new ArgumentOutOfRangeException(nameof(requestedCount));
        return new AutomationQueueDetail(
            snapshot.NativeCapacity,
            snapshot.LiveOccupancy,
            snapshot.NativeRemainingRoom,
            snapshot.AutomationUsageLimit,
            snapshot.ManualReservation,
            snapshot.UsableAutomationRoom,
            requestedCount,
            QueueCapacityInvalidReason.None);
    }

    public static AutomationQueueDetail Invalid(QueueCapacityInvalidReason invalidReason, int requestedCount = 0)
    {
        if (invalidReason == QueueCapacityInvalidReason.None ||
            !Enum.IsDefined(typeof(QueueCapacityInvalidReason), invalidReason))
            throw new ArgumentOutOfRangeException(nameof(invalidReason));
        if (requestedCount < 0) throw new ArgumentOutOfRangeException(nameof(requestedCount));
        return new AutomationQueueDetail(0, 0, 0, 0, 0, 0, requestedCount, invalidReason);
    }

    public int NativeCapacity { get; }
    public int LiveOccupancy { get; }
    public int NativeRemainingRoom { get; }
    public int AutomationLimit { get; }
    public int ManualReservation { get; }
    public int UsableRoom { get; }
    public int RequestedCount { get; }
    public QueueCapacityInvalidReason InvalidReason { get; }

    public bool Equals(AutomationQueueDetail other) =>
        NativeCapacity == other.NativeCapacity && LiveOccupancy == other.LiveOccupancy &&
        NativeRemainingRoom == other.NativeRemainingRoom && AutomationLimit == other.AutomationLimit &&
        ManualReservation == other.ManualReservation && UsableRoom == other.UsableRoom &&
        RequestedCount == other.RequestedCount && InvalidReason == other.InvalidReason;

    public override bool Equals(object? obj) => obj is AutomationQueueDetail other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = NativeCapacity;
            hash = (hash * 397) ^ LiveOccupancy;
            hash = (hash * 397) ^ NativeRemainingRoom;
            hash = (hash * 397) ^ AutomationLimit;
            hash = (hash * 397) ^ ManualReservation;
            hash = (hash * 397) ^ UsableRoom;
            hash = (hash * 397) ^ RequestedCount;
            hash = (hash * 397) ^ (int)InvalidReason;
            return hash;
        }
    }
}

public readonly struct AutomationNativeDetail : IEquatable<AutomationNativeDetail>
{
    public AutomationNativeDetail(
        string contractId = "",
        AutomationNativeStateCode stateCode = AutomationNativeStateCode.None,
        TypedRegistryResolutionStatus? registryStatus = null,
        NativeMutationOutcome? mutationOutcome = null)
    {
        if (stateCode < AutomationNativeStateCode.None ||
            stateCode > AutomationNativeStateCode.ContractMismatch)
            throw new ArgumentOutOfRangeException(nameof(stateCode));
        if (registryStatus.HasValue &&
            (registryStatus.Value < TypedRegistryResolutionStatus.Resolved ||
             registryStatus.Value > TypedRegistryResolutionStatus.StaleGeneration))
            throw new ArgumentOutOfRangeException(nameof(registryStatus));
        if (mutationOutcome.HasValue &&
            (mutationOutcome.Value < NativeMutationOutcome.Verified ||
             mutationOutcome.Value > NativeMutationOutcome.PostconditionFailed))
            throw new ArgumentOutOfRangeException(nameof(mutationOutcome));
        var normalizedContractId = (contractId ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(contractId) && normalizedContractId.Length == 0)
            throw new ArgumentException("A native contract ID cannot contain only whitespace.", nameof(contractId));
        ContractId = normalizedContractId;
        StateCode = stateCode;
        RegistryStatus = registryStatus;
        MutationOutcome = mutationOutcome;
    }

    public string ContractId { get; }
    public AutomationNativeStateCode StateCode { get; }
    public TypedRegistryResolutionStatus? RegistryStatus { get; }
    public NativeMutationOutcome? MutationOutcome { get; }
    public bool IsEmpty => string.IsNullOrEmpty(ContractId) && StateCode == AutomationNativeStateCode.None && !RegistryStatus.HasValue && !MutationOutcome.HasValue;

    public bool Equals(AutomationNativeDetail other) =>
        string.Equals(ContractId, other.ContractId, StringComparison.Ordinal) &&
        StateCode == other.StateCode &&
        RegistryStatus == other.RegistryStatus && MutationOutcome == other.MutationOutcome;

    public override bool Equals(object? obj) => obj is AutomationNativeDetail other && Equals(other);
    public override int GetHashCode() => unchecked((((StringComparer.Ordinal.GetHashCode(ContractId ?? string.Empty) * 397) ^ (int)StateCode) * 397 ^ RegistryStatus.GetHashCode()) * 397 ^ MutationOutcome.GetHashCode());
}

public readonly struct AutomationResourceConstraintCollection : IReadOnlyList<AutomationResourceConstraint>
{
    private readonly AutomationResourceConstraint[]? _items;

    internal AutomationResourceConstraintCollection(AutomationResourceConstraint[]? items) => _items = items;

    public int Count => _items?.Length ?? 0;

    public AutomationResourceConstraint this[int index] => (_items ?? Array.Empty<AutomationResourceConstraint>())[index];

    public IEnumerator<AutomationResourceConstraint> GetEnumerator() =>
        ((IEnumerable<AutomationResourceConstraint>)(_items ?? Array.Empty<AutomationResourceConstraint>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public readonly struct AutomationDecision
{
    public const int CurrentSchemaVersion = 1;
    private readonly AutomationResourceConstraint[]? _resourceConstraints;

    public AutomationDecision(
        string featureId,
        string operationId,
        AutomationDecisionDisposition disposition,
        AutomationDecisionCode code,
        AutomationEntityIdentity subject = default,
        AutomationEntityIdentity related = default,
        long lifecycleGeneration = 0,
        AutomationRetryTrigger retryTriggers = AutomationRetryTrigger.None,
        IReadOnlyList<AutomationResourceConstraint>? resourceConstraints = null,
        AutomationQueueDetail? queue = null,
        AutomationNativeDetail native = default,
        int affectedCount = 1,
        string technicalDetail = "")
        : this(
            featureId,
            operationId,
            disposition,
            code,
            subject,
            related,
            lifecycleGeneration,
            retryTriggers,
            CopyAndSort(resourceConstraints),
            queue,
            native,
            affectedCount,
            technicalDetail,
            default)
    {
    }

    private AutomationDecision(
        string featureId,
        string operationId,
        AutomationDecisionDisposition disposition,
        AutomationDecisionCode code,
        AutomationEntityIdentity subject,
        AutomationEntityIdentity related,
        long lifecycleGeneration,
        AutomationRetryTrigger retryTriggers,
        AutomationResourceConstraint[]? preparedResourceConstraints,
        AutomationQueueDetail? queue,
        AutomationNativeDetail native,
        int affectedCount,
        string technicalDetail,
        OwnedConstraintArray _)
    {
        var normalizedFeatureId = (featureId ?? string.Empty).Trim();
        var normalizedOperationId = (operationId ?? string.Empty).Trim();
        if (normalizedFeatureId.Length == 0) throw new ArgumentException("A stable feature ID is required.", nameof(featureId));
        if (normalizedOperationId.Length == 0) throw new ArgumentException("A stable operation ID is required.", nameof(operationId));
        if (disposition < AutomationDecisionDisposition.Accepted || disposition > AutomationDecisionDisposition.Failed)
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (!IsDefinedCode(code)) throw new ArgumentOutOfRangeException(nameof(code));
        const AutomationRetryTrigger allRetryTriggers =
            AutomationRetryTrigger.Configuration |
            AutomationRetryTrigger.Lifecycle |
            AutomationRetryTrigger.Registry |
            AutomationRetryTrigger.Progression |
            AutomationRetryTrigger.ResourceQuantity |
            AutomationRetryTrigger.ResourceRate |
            AutomationRetryTrigger.Queue |
            AutomationRetryTrigger.Inventory |
            AutomationRetryTrigger.Targeting |
            AutomationRetryTrigger.SchedulerTurn |
            AutomationRetryTrigger.ManualInput;
        if ((retryTriggers & ~allRetryTriggers) != 0)
            throw new ArgumentOutOfRangeException(nameof(retryTriggers));
        if (code == AutomationDecisionCode.Eligible && disposition != AutomationDecisionDisposition.Accepted)
            throw new ArgumentException("Eligible decisions must be accepted.", nameof(disposition));
        if (code != AutomationDecisionCode.Eligible && disposition == AutomationDecisionDisposition.Accepted)
            throw new ArgumentException("Only Eligible decisions may be accepted.", nameof(disposition));
        if (lifecycleGeneration < 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (affectedCount < 0) throw new ArgumentOutOfRangeException(nameof(affectedCount));

        SchemaVersion = CurrentSchemaVersion;
        FeatureId = normalizedFeatureId;
        OperationId = normalizedOperationId;
        Disposition = disposition;
        Code = code;
        Subject = subject;
        Related = related;
        LifecycleGeneration = lifecycleGeneration;
        RetryTriggers = retryTriggers;
        _resourceConstraints = preparedResourceConstraints;
        Queue = queue;
        Native = native;
        AffectedCount = affectedCount;
        TechnicalDetail = technicalDetail ?? string.Empty;
    }

    internal static AutomationDecision CreateWithOwnedResourceConstraints(
        string featureId,
        string operationId,
        AutomationDecisionDisposition disposition,
        AutomationDecisionCode code,
        AutomationEntityIdentity subject,
        AutomationEntityIdentity related,
        long lifecycleGeneration,
        AutomationRetryTrigger retryTriggers,
        AutomationResourceConstraint[]? resourceConstraints,
        AutomationQueueDetail? queue,
        AutomationNativeDetail native,
        int affectedCount,
        string technicalDetail)
    {
        return new AutomationDecision(
            featureId,
            operationId,
            disposition,
            code,
            subject,
            related,
            lifecycleGeneration,
            retryTriggers,
            ValidateAndSortOwned(resourceConstraints),
            queue,
            native,
            affectedCount,
            technicalDetail,
            default);
    }

    public int SchemaVersion { get; }
    public string FeatureId { get; }
    public string OperationId { get; }
    public AutomationDecisionDisposition Disposition { get; }
    public AutomationDecisionCode Code { get; }
    public AutomationEntityIdentity Subject { get; }
    public AutomationEntityIdentity Related { get; }
    public long LifecycleGeneration { get; }
    public AutomationRetryTrigger RetryTriggers { get; }
    public AutomationResourceConstraintCollection ResourceConstraints => new(_resourceConstraints);
    public AutomationQueueDetail? Queue { get; }
    public AutomationNativeDetail Native { get; }
    public int AffectedCount { get; }
    public string TechnicalDetail { get; }
    public AutomationDecisionConditionKey ConditionKey => new(this);
    public AutomationDecisionInstanceKey InstanceKey => new(ConditionKey, LifecycleGeneration);

    public AutomationDecision WithLifecycleGeneration(long lifecycleGeneration)
    {
        if (lifecycleGeneration < 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (lifecycleGeneration == LifecycleGeneration) return this;
        return new AutomationDecision(
            FeatureId,
            OperationId,
            Disposition,
            Code,
            Subject,
            Related,
            lifecycleGeneration,
            RetryTriggers,
            _resourceConstraints,
            Queue,
            Native,
            AffectedCount,
            TechnicalDetail,
            default);
    }

    private static AutomationResourceConstraint[]? CopyAndSort(IReadOnlyList<AutomationResourceConstraint>? constraints)
    {
        if (constraints is null || constraints.Count == 0) return null;
        var copy = new AutomationResourceConstraint[constraints.Count];
        for (var index = 0; index < copy.Length; index++)
        {
            var constraint = constraints[index];
            if (constraint.Resource.IsEmpty ||
                constraint.Kind < AutomationResourceConstraintKind.Cost ||
                constraint.Kind > AutomationResourceConstraintKind.StartFullness)
                throw new ArgumentException("Every resource constraint must be initialized.", nameof(constraints));
            copy[index] = constraint;
        }
        if (copy.Length > 1) Array.Sort(copy, ConstraintComparer.Instance);
        return copy;
    }

    private static AutomationResourceConstraint[]? ValidateAndSortOwned(AutomationResourceConstraint[]? constraints)
    {
        if (constraints is null || constraints.Length == 0) return null;
        for (var index = 0; index < constraints.Length; index++)
        {
            var constraint = constraints[index];
            if (constraint.Resource.IsEmpty ||
                constraint.Kind < AutomationResourceConstraintKind.Cost ||
                constraint.Kind > AutomationResourceConstraintKind.StartFullness)
                throw new ArgumentException("Every resource constraint must be initialized.", nameof(constraints));
        }
        if (constraints.Length > 1) Array.Sort(constraints, ConstraintComparer.Instance);
        return constraints;
    }

    private static bool IsDefinedCode(AutomationDecisionCode code) => code switch
    {
        AutomationDecisionCode.Eligible or
        AutomationDecisionCode.ConfigurationDisabled or
        AutomationDecisionCode.PolicyExcluded or
        AutomationDecisionCode.Locked or
        AutomationDecisionCode.Unavailable or
        AutomationDecisionCode.AlreadyActive or
        AutomationDecisionCode.DuplicateScheduled or
        AutomationDecisionCode.ContractUnresolved or
        AutomationDecisionCode.RegistryNotReady or
        AutomationDecisionCode.IdentityUnavailable or
        AutomationDecisionCode.IdentityChanged or
        AutomationDecisionCode.WrongNativeType or
        AutomationDecisionCode.NativeStateUnavailable or
        AutomationDecisionCode.NativeAdmissionRejected or
        AutomationDecisionCode.MutationQuarantined or
        AutomationDecisionCode.NativeMutationFailed or
        AutomationDecisionCode.PostconditionFailed or
        AutomationDecisionCode.CostUnavailable or
        AutomationDecisionCode.InvalidConfiguration or
        AutomationDecisionCode.InvalidResourceState or
        AutomationDecisionCode.InsufficientResource or
        AutomationDecisionCode.ReserveFloor or
        AutomationDecisionCode.AffordabilityThreshold or
        AutomationDecisionCode.DrainUnsafe or
        AutomationDecisionCode.ResourceStartThreshold or
        AutomationDecisionCode.QueueUnavailable or
        AutomationDecisionCode.QueueFull or
        AutomationDecisionCode.QueuePolicyLimit or
        AutomationDecisionCode.QueueBatchLimit or
        AutomationDecisionCode.TargetUnavailable or
        AutomationDecisionCode.TargetInvalid or
        AutomationDecisionCode.TargetingInProgress or
        AutomationDecisionCode.BudgetDeferred or
        AutomationDecisionCode.WaitingForTurn or
        AutomationDecisionCode.ScanLimitDeferred or
        AutomationDecisionCode.LifecycleChanged or
        AutomationDecisionCode.ManualPause or
        AutomationDecisionCode.NativeBusy or
        AutomationDecisionCode.SourceIneligible or
        AutomationDecisionCode.NoEligibleTargets or
        AutomationDecisionCode.ZeroEffect or
        AutomationDecisionCode.CapacityOverflow => true,
        _ => false,
    };

    private sealed class ConstraintComparer : IComparer<AutomationResourceConstraint>
    {
        public static readonly ConstraintComparer Instance = new();
        public int Compare(AutomationResourceConstraint left, AutomationResourceConstraint right)
        {
            var result = string.Compare(left.Resource.Domain, right.Resource.Domain, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.Resource.StableId, right.Resource.StableId, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.Resource.ExpectedNativeType, right.Resource.ExpectedNativeType, StringComparison.Ordinal);
            if (result != 0) return result;
            result = left.Kind.CompareTo(right.Kind);
            if (result != 0) return result;
            result = left.Required.CompareTo(right.Required);
            if (result != 0) return result;
            result = left.Cost.CompareTo(right.Cost);
            return result != 0 ? result : left.IsBandwidth.CompareTo(right.IsBandwidth);
        }
    }

    private readonly struct OwnedConstraintArray
    {
    }
}

public readonly struct AutomationDecisionConditionKey : IEquatable<AutomationDecisionConditionKey>
{
    private readonly AutomationDecision _decision;
    internal AutomationDecisionConditionKey(AutomationDecision decision) => _decision = decision;

    public bool Equals(AutomationDecisionConditionKey other)
    {
        var left = _decision;
        var right = other._decision;
        if (left.SchemaVersion != right.SchemaVersion || left.Disposition != right.Disposition || left.Code != right.Code ||
            !string.Equals(left.FeatureId, right.FeatureId, StringComparison.Ordinal) ||
            !string.Equals(left.OperationId, right.OperationId, StringComparison.Ordinal) ||
            !left.Subject.Equals(right.Subject) || !left.Related.Equals(right.Related) ||
            left.RetryTriggers != right.RetryTriggers || !QueueConditionEquals(left.Queue, right.Queue) ||
            !left.Native.Equals(right.Native) ||
            left.ResourceConstraints.Count != right.ResourceConstraints.Count) return false;
        for (var index = 0; index < left.ResourceConstraints.Count; index++)
        {
            var a = left.ResourceConstraints[index];
            var b = right.ResourceConstraints[index];
            if (a.Kind != b.Kind || !a.Resource.Equals(b.Resource) || !a.Cost.Equals(b.Cost) ||
                !a.Required.Equals(b.Required) || a.IsBandwidth != b.IsBandwidth) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is AutomationDecisionConditionKey other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = _decision.SchemaVersion;
            hash = (hash * 397) ^ (int)_decision.Disposition;
            hash = (hash * 397) ^ (int)_decision.Code;
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_decision.FeatureId ?? string.Empty);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_decision.OperationId ?? string.Empty);
            hash = (hash * 397) ^ _decision.Subject.GetHashCode();
            hash = (hash * 397) ^ _decision.Related.GetHashCode();
            hash = (hash * 397) ^ (int)_decision.RetryTriggers;
            if (_decision.Queue is { } queue)
            {
                hash = (hash * 397) ^ queue.NativeCapacity;
                hash = (hash * 397) ^ queue.AutomationLimit;
                hash = (hash * 397) ^ queue.ManualReservation;
                hash = (hash * 397) ^ queue.RequestedCount;
                hash = (hash * 397) ^ (int)queue.InvalidReason;
            }
            hash = (hash * 397) ^ _decision.Native.GetHashCode();
            for (var index = 0; index < _decision.ResourceConstraints.Count; index++)
            {
                var constraint = _decision.ResourceConstraints[index];
                hash = (hash * 397) ^ (int)constraint.Kind;
                hash = (hash * 397) ^ constraint.Resource.GetHashCode();
                hash = (hash * 397) ^ constraint.Cost.GetHashCode();
                hash = (hash * 397) ^ constraint.Required.GetHashCode();
                hash = (hash * 397) ^ constraint.IsBandwidth.GetHashCode();
            }
            return hash;
        }
    }

    private static bool QueueConditionEquals(AutomationQueueDetail? left, AutomationQueueDetail? right)
    {
        if (!left.HasValue || !right.HasValue) return left.HasValue == right.HasValue;
        var a = left.Value;
        var b = right.Value;
        return a.NativeCapacity == b.NativeCapacity &&
               a.AutomationLimit == b.AutomationLimit &&
               a.ManualReservation == b.ManualReservation &&
               a.RequestedCount == b.RequestedCount &&
               a.InvalidReason == b.InvalidReason;
    }
}

public readonly struct AutomationDecisionInstanceKey : IEquatable<AutomationDecisionInstanceKey>
{
    public AutomationDecisionInstanceKey(AutomationDecisionConditionKey condition, long lifecycleGeneration)
    {
        Condition = condition;
        LifecycleGeneration = lifecycleGeneration;
    }

    public AutomationDecisionConditionKey Condition { get; }
    public long LifecycleGeneration { get; }
    public bool Equals(AutomationDecisionInstanceKey other) =>
        LifecycleGeneration == other.LifecycleGeneration && Condition.Equals(other.Condition);
    public override bool Equals(object? obj) => obj is AutomationDecisionInstanceKey other && Equals(other);
    public override int GetHashCode() => unchecked((Condition.GetHashCode() * 397) ^ LifecycleGeneration.GetHashCode());
}

public interface IAutomationDecisionSink
{
    void Observe(in AutomationDecision decision);
}

/// <summary>
/// Process-wide, allocation-free-on-publish decision channel for diagnostics
/// consumers such as Orb Insights. Subscriber failures are isolated from game
/// automation and cannot prevent later subscribers from observing a decision.
/// </summary>
public static class AutomationDecisionPublisher
{
    private static readonly object Sync = new();
    private static volatile IAutomationDecisionSink[] _sinks = Array.Empty<IAutomationDecisionSink>();

    public static IDisposable Subscribe(IAutomationDecisionSink sink)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));
        lock (Sync)
        {
            var updated = new IAutomationDecisionSink[_sinks.Length + 1];
            Array.Copy(_sinks, updated, _sinks.Length);
            updated[updated.Length - 1] = sink;
            _sinks = updated;
        }
        return new Subscription(sink);
    }

    public static void Publish(in AutomationDecision decision)
    {
        if (decision.SchemaVersion != AutomationDecision.CurrentSchemaVersion)
            throw new ArgumentException("Only initialized decisions can be published.", nameof(decision));
        var sinks = _sinks;
        for (var index = 0; index < sinks.Length; index++)
        {
            try
            {
                sinks[index].Observe(in decision);
            }
            catch
            {
                // Diagnostics consumers must never alter automation behavior.
            }
        }
    }

    private static void Unsubscribe(IAutomationDecisionSink sink)
    {
        lock (Sync)
        {
            var match = -1;
            for (var index = 0; index < _sinks.Length; index++)
            {
                if (!ReferenceEquals(_sinks[index], sink)) continue;
                match = index;
                break;
            }
            if (match < 0) return;

            var updated = new IAutomationDecisionSink[_sinks.Length - 1];
            if (match > 0) Array.Copy(_sinks, 0, updated, 0, match);
            if (match < _sinks.Length - 1)
                Array.Copy(_sinks, match + 1, updated, match, _sinks.Length - match - 1);
            _sinks = updated;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private IAutomationDecisionSink? _sink;
        public Subscription(IAutomationDecisionSink sink) => _sink = sink;
        public void Dispose()
        {
            var sink = _sink;
            if (sink is null) return;
            _sink = null;
            Unsubscribe(sink);
        }
    }
}

public static class AutomationDecisionPresenter
{
    public static string Format(in AutomationDecision decision)
    {
        var builder = new StringBuilder(Label(decision.Code));
        if (!decision.Subject.IsEmpty)
        {
            builder.Append(" [");
            builder.Append(decision.Subject.Domain);
            if (!string.IsNullOrEmpty(decision.Subject.DisplayName))
            {
                builder.Append('/');
                builder.Append(decision.Subject.DisplayName);
            }
            if (!string.IsNullOrEmpty(decision.Subject.StableId))
            {
                builder.Append(" (");
                builder.Append(decision.Subject.StableId);
                builder.Append(')');
            }
            builder.Append(']');
        }
        if (decision.ResourceConstraints.Count > 0)
        {
            builder.Append("; resources=");
            for (var index = 0; index < decision.ResourceConstraints.Count; index++)
            {
                if (index > 0) builder.Append(", ");
                var constraint = decision.ResourceConstraints[index];
                builder.Append(!string.IsNullOrEmpty(constraint.Resource.DisplayName)
                    ? constraint.Resource.DisplayName
                    : constraint.Resource.StableId);
                builder.Append(" observed=");
                builder.Append(constraint.Observed);
                builder.Append(" required=");
                builder.Append(constraint.Required);
            }
        }
        if (!string.IsNullOrWhiteSpace(decision.TechnicalDetail))
        {
            builder.Append("; ");
            builder.Append(decision.TechnicalDetail);
        }
        return builder.ToString();
    }

    public static string Label(AutomationDecisionCode code) => code switch
    {
        AutomationDecisionCode.Eligible => "Eligible",
        AutomationDecisionCode.ConfigurationDisabled => "Disabled by configuration",
        AutomationDecisionCode.PolicyExcluded => "Excluded by policy",
        AutomationDecisionCode.Locked => "Locked",
        AutomationDecisionCode.Unavailable => "Unavailable",
        AutomationDecisionCode.AlreadyActive => "Already active",
        AutomationDecisionCode.DuplicateScheduled => "Already scheduled",
        AutomationDecisionCode.ContractUnresolved => "Contract unresolved",
        AutomationDecisionCode.RegistryNotReady => "Registry not ready",
        AutomationDecisionCode.IdentityUnavailable => "Identity unavailable",
        AutomationDecisionCode.IdentityChanged => "Identity changed",
        AutomationDecisionCode.WrongNativeType => "Wrong native type",
        AutomationDecisionCode.NativeStateUnavailable => "Native state unavailable",
        AutomationDecisionCode.NativeAdmissionRejected => "Native admission rejected",
        AutomationDecisionCode.MutationQuarantined => "Mutation quarantined",
        AutomationDecisionCode.NativeMutationFailed => "Native mutation failed",
        AutomationDecisionCode.PostconditionFailed => "Mutation postcondition failed",
        AutomationDecisionCode.CostUnavailable => "Cost unavailable",
        AutomationDecisionCode.InvalidConfiguration => "Invalid configuration",
        AutomationDecisionCode.InvalidResourceState => "Invalid resource state",
        AutomationDecisionCode.InsufficientResource => "Insufficient resource",
        AutomationDecisionCode.ReserveFloor => "Reserve floor blocked",
        AutomationDecisionCode.AffordabilityThreshold => "Affordability threshold blocked",
        AutomationDecisionCode.DrainUnsafe => "Resource drain unsafe",
        AutomationDecisionCode.ResourceStartThreshold => "Resource start threshold blocked",
        AutomationDecisionCode.QueueUnavailable => "Queue unavailable",
        AutomationDecisionCode.QueueFull => "Queue full",
        AutomationDecisionCode.QueuePolicyLimit => "Queue policy limit",
        AutomationDecisionCode.QueueBatchLimit => "Queue batch limit",
        AutomationDecisionCode.TargetUnavailable => "Target unavailable",
        AutomationDecisionCode.TargetInvalid => "Target invalid",
        AutomationDecisionCode.TargetingInProgress => "Targeting in progress",
        AutomationDecisionCode.BudgetDeferred => "Budget deferred",
        AutomationDecisionCode.WaitingForTurn => "Waiting for scheduler turn",
        AutomationDecisionCode.ScanLimitDeferred => "Scan limit deferred",
        AutomationDecisionCode.LifecycleChanged => "Lifecycle changed",
        AutomationDecisionCode.ManualPause => "Paused after manual input",
        AutomationDecisionCode.NativeBusy => "Native system busy",
        AutomationDecisionCode.SourceIneligible => "Source ineligible",
        AutomationDecisionCode.NoEligibleTargets => "No eligible targets",
        AutomationDecisionCode.ZeroEffect => "No effect",
        AutomationDecisionCode.CapacityOverflow => "Bounded capacity exceeded",
        _ => "Unknown automation decision",
    };
}
