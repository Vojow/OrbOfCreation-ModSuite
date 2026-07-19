using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class ReflectionAutoBuyCatalog :
    IAutoBuyCatalog,
    IAutoBuyIncrementalCatalog,
    IAutoBuyProgressionCatalog,
    IAutoBuyInvalidationIdentityCatalog,
    IAutoBuyCompletionRevalidationCatalog
{
    private static readonly TimeSpan RegistryReconciliationInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LifecycleMaintenanceInterval = TimeSpan.FromMilliseconds(250);
    private const int RegistryItemsPerEvaluation = 32;
    private const int LifecycleItemsPerEvaluation = 32;
    private const int ActiveLifecycleRefreshPerMaintenance = 8;
    private const int SlowLifecycleRefreshPerMaintenance = 16;
    private readonly AutoBuyCandidateIndex _index = new AutoBuyCandidateIndex();
    private readonly Stopwatch _lifetime = Stopwatch.StartNew();
    private readonly Func<TimeSpan>? _readElapsed;
    private readonly AutoBuyMaintenanceCadence _maintenanceCadence;
    private readonly AutoBuyResourceSnapshotCache _resourceSnapshots;
    private readonly HashSet<string> _deferredPurchaseResourceInvalidations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly AutoBuyCompletionSettlementGate _completionSettlement =
        new AutoBuyCompletionSettlementGate();
    private RegistryReconciliation? _registryReconciliation;
    private TimeSpan _nextRegistryReconciliation;
    private AutoBuyCandidateKinds _pendingRegistryRefresh;
    private bool _mutationGroupActive;
    private MethodInfo? _getRemainingQueueRoom;
    private FieldInfo? _actionManagerInstance;
    private FieldInfo? _actionableItems;
    private FieldInfo? _maxQueuedItems;
    private MethodInfo? _readMaxQueuedItems;
    private MethodInfo? _getBulkDevelopment;

    public ReflectionAutoBuyCatalog()
        : this(null)
    {
    }

    internal ReflectionAutoBuyCatalog(Func<TimeSpan>? readElapsed)
    {
        _readElapsed = readElapsed;
        _maintenanceCadence = new AutoBuyMaintenanceCadence(
            LifecycleMaintenanceInterval,
            ActiveLifecycleRefreshPerMaintenance,
            SlowLifecycleRefreshPerMaintenance);
        _resourceSnapshots = new AutoBuyResourceSnapshotCache(
            new ReflectionAutoBuyResourceSnapshotReader(),
            OnResourceSnapshotChanged);
    }

    internal AutoBuyCandidateKinds PendingRegistryRefresh => _pendingRegistryRefresh;

    public IEnumerable<IAutoBuyCandidate> Discover()
    {
        return BeginEvaluation(new AutoBuyEvaluationRequest(int.MaxValue, true, true)).ActiveCandidates;
    }

    public AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request)
    {
        CompleteMutationGroup();
        FlushDeferredPurchaseInvalidations();
        if (_completionSettlement.TryBegin(_index.SettlementValidationPending))
        {
            _index.InvalidateCompletionEffects();
        }

        StartRegistryReconciliationIfDue();
        ProcessRegistryReconciliationSlice();
        _resourceSnapshots.BeginEvaluationEpoch(_index.HasResourceDependents);

        _maintenanceCadence.TryTake(
            Elapsed,
            out var activeRefreshCount,
            out var slowRefreshCount);
        var batch = _index.PrepareEvaluation(
            request,
            LifecycleItemsPerEvaluation,
            activeRefreshCount,
            slowRefreshCount);
        return new AutoBuyEvaluationBatch(
            batch.ActiveCandidates,
            batch.DirtyCandidates,
            batch.FirstExcludedCandidate,
            _registryReconciliation is not null ||
            _index.EpochValidationPending ||
            _index.SettlementValidationPending);
    }

    public void CompleteCandidateEvaluation(
        IAutoBuyCandidate candidate,
        bool suppressResourceTracking,
        bool policyExcluded,
        AutoBuyDecision? decision = null)
    {
        _index.CompleteCandidateEvaluation(candidate, suppressResourceTracking, policyExcluded, decision);
    }

    public void InvalidatePolicy()
    {
        _deferredPurchaseResourceInvalidations.Clear();
        _index.InvalidatePolicy();
    }

    public void BeginMutationEvaluation()
    {
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void NotifyPurchaseAttempted(IAutoBuyCandidate candidate)
    {
        _mutationGroupActive = true;
        if (candidate is IAutoBuyDirtyCandidate dirtyCandidate)
        {
            for (var i = 0; i < dirtyCandidate.ResourceDependencies.Count; i++)
            {
                _deferredPurchaseResourceInvalidations.Add(dirtyCandidate.ResourceDependencies[i]);
            }

            // The selected candidate must refresh its own live cost and level
            // before the next repeated level, but shared dependents can settle
            // once when the owned group ends.
            _index.MarkPurchaseAttempted(candidate, invalidateResourceDependents: false);
        }
        else
        {
            _index.MarkPurchaseAttempted(candidate);
        }
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void CompleteMutationGroup()
    {
        _mutationGroupActive = false;
    }

    private void FlushDeferredPurchaseInvalidations()
    {
        if (_deferredPurchaseResourceInvalidations.Count == 0)
        {
            return;
        }

        foreach (var resourceId in _deferredPurchaseResourceInvalidations)
        {
            _index.InvalidateResource(resourceId, AutoBuyResourceChange.Quantity);
        }

        _deferredPurchaseResourceInvalidations.Clear();
    }

    private void OnResourceSnapshotChanged(
        string resourceId,
        AutoBuyResourceChange change,
        BigAmount? previousQuantity,
        BigAmount? currentQuantity)
    {
        if (ShouldDeferResourceInvalidation(_mutationGroupActive, change))
        {
            _deferredPurchaseResourceInvalidations.Add(resourceId);
            return;
        }

        _index.InvalidateResource(resourceId, change, previousQuantity, currentQuantity);
    }

    internal static bool ShouldDeferResourceInvalidation(
        bool mutationGroupActive,
        AutoBuyResourceChange change)
    {
        return mutationGroupActive && change == AutoBuyResourceChange.Quantity;
    }

    public void NotifyStructureQueueChanged(object nativeIdentity)
    {
        CompleteMutationGroup();
        _index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Structure);
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void NotifyUpgradeQueueChanged(object nativeIdentity)
    {
        CompleteMutationGroup();
        _index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Upgrade);
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void NotifyNativeCompletion()
    {
        _pendingRegistryRefresh |= AutoBuyCandidateKinds.All;
        _completionSettlement.Notify();
    }

    public void NotifyNativeCompletion(object nativeIdentity, AutoBuyCandidateKind completedKind)
    {
        _index.InvalidateQueue(nativeIdentity, completedKind);
        _pendingRegistryRefresh |= completedKind == AutoBuyCandidateKind.Structure
            ? AutoBuyCandidateKinds.Upgrades
            : AutoBuyCandidateKinds.Structures;
        _completionSettlement.Notify();
    }

    public bool TryResolveInvalidationTarget(
        object nativeIdentity,
        AutoBuyCandidateKind expectedKind,
        out string entityId,
        out string expectedTypeName)
    {
        return _index.TryResolveInvalidationTarget(
            nativeIdentity,
            expectedKind,
            out entityId,
            out expectedTypeName);
    }

    public bool TryRefreshCandidateAfterCompletion(
        IAutoBuyCandidate candidate,
        long completionGeneration,
        out string reason)
    {
        if (candidate is ReflectionAutoBuyCandidate reflectionCandidate)
        {
            return reflectionCandidate.TryRefreshAfterCompletion(completionGeneration, out reason);
        }

        reason = "candidate is not backed by the audited reflection adapter";
        return false;
    }

    public void InvalidateLifecycle()
    {
        _mutationGroupActive = false;
        _deferredPurchaseResourceInvalidations.Clear();
        _resourceSnapshots.Clear();
        _registryReconciliation = null;
        _pendingRegistryRefresh = AutoBuyCandidateKinds.None;
        _completionSettlement.Clear();
        _nextRegistryReconciliation = TimeSpan.Zero;
        _maintenanceCadence.Reset(Elapsed);
        _index.InvalidateLifecycleIncrementally();
    }

    public bool TryCaptureQueueCapacity(
        int automationUsageLimit,
        int manualReservation,
        out QueueCapacitySnapshot snapshot)
    {
        snapshot = default;
        var method = ResolveStaticNoArgMethod(
            ref _getRemainingQueueRoom,
            "ActionManager",
            "GetRemainingRoom",
            typeof(int));
        if (method is null || !TryResolveQueueCapacityContract())
        {
            return false;
        }

        try
        {
            var manager = _actionManagerInstance!.GetValue(null);
            var actionableItems = manager is null ? null : _actionableItems!.GetValue(manager);
            var maxQueuedItems = actionableItems is null ? null : _maxQueuedItems!.GetValue(actionableItems);
            if (maxQueuedItems is null ||
                _readMaxQueuedItems!.Invoke(maxQueuedItems, Array.Empty<object>()) is not int nativeCapacity ||
                method.Invoke(null, Array.Empty<object>()) is not int nativeRemainingRoom)
            {
                return false;
            }

            return QueueCapacitySnapshot.TryCreate(
                nativeCapacity,
                nativeRemainingRoom,
                automationUsageLimit,
                manualReservation,
                out snapshot,
                out _);
        }
        catch (Exception ex) when (
            ex is TargetInvocationException ||
            ex is ArgumentException ||
            ex is InvalidOperationException ||
            ex is TargetException)
        {
        }

        return false;
    }

    private bool TryResolveQueueCapacityContract()
    {
        if (_actionManagerInstance is not null &&
            _actionableItems is not null &&
            _maxQueuedItems is not null &&
            _readMaxQueuedItems is not null)
        {
            return true;
        }

        var actionManagerType = ReflectionUtil.FindLoadedType("ActionManager");
        var instance = actionManagerType?.GetField("instance", BindingFlags.Static | BindingFlags.Public);
        var actionableItems = actionManagerType?.GetField("actionableItems", BindingFlags.Instance | BindingFlags.Public);
        if (actionManagerType is null ||
            instance is null ||
            instance.FieldType != actionManagerType ||
            actionableItems is null ||
            !string.Equals(actionableItems.FieldType.Name, "ActionableListVariable", StringComparison.Ordinal))
        {
            return false;
        }

        var maxQueuedItems = actionableItems.FieldType.GetField(
            "maxQueuedItems",
            BindingFlags.Instance | BindingFlags.Public);
        if (maxQueuedItems is null ||
            !string.Equals(maxQueuedItems.FieldType.Name, "IntVariable", StringComparison.Ordinal))
        {
            return false;
        }

        var readMaxQueuedItems = maxQueuedItems.FieldType.GetMethod(
            "AsInt",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        if (readMaxQueuedItems?.ReturnType != typeof(int))
        {
            return false;
        }

        _actionManagerInstance = instance;
        _actionableItems = actionableItems;
        _maxQueuedItems = maxQueuedItems;
        _readMaxQueuedItems = readMaxQueuedItems;
        return true;
    }

    public bool TryGetBulkDevelopment(out int levels)
    {
        levels = 1;
        var method = ResolveStaticNoArgMethod(
            ref _getBulkDevelopment,
            "Player",
            "GetBulkDevelopment",
            expectedReturnType: null);
        try
        {
            var variable = method?.Invoke(null, Array.Empty<object>());
            if (variable is not null &&
                NativeIntVariableContract.TryResolve(variable, out var contract, out _) &&
                contract.TryRead(variable, out var value, out _))
            {
                levels = Math.Max(1, value);
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
        }

        return false;
    }

    public bool TryGetActionMultiplier(out int multiplier)
    {
        multiplier = 1;
        if (NativeGlobalVariableAccess.TryGetMultiBuy(
                out var variable,
                out var contract,
                out _) &&
            contract.TryRead(variable, out var value, out _))
        {
            multiplier = Math.Max(1, value);
            return true;
        }

        return false;
    }

    private static MethodInfo? ResolveStaticNoArgMethod(
        ref MethodInfo? cached,
        string typeName,
        string methodName,
        Type? expectedReturnType)
    {
        if (cached is not null)
        {
            return cached;
        }

        var type = ReflectionUtil.FindLoadedType(typeName);
        var method = type?.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        if (method is null ||
            (expectedReturnType is not null && method.ReturnType != expectedReturnType))
        {
            return null;
        }

        cached = method;
        return method;
    }

    public void Dispose()
    {
        _mutationGroupActive = false;
        _deferredPurchaseResourceInvalidations.Clear();
        _registryReconciliation = null;
        _pendingRegistryRefresh = AutoBuyCandidateKinds.None;
        _completionSettlement.Clear();
        _resourceSnapshots.Clear();
        _index.Clear();
    }

    private TimeSpan Elapsed => _readElapsed?.Invoke() ?? _lifetime.Elapsed;

    private void StartRegistryReconciliationIfDue()
    {
        if (_registryReconciliation is not null)
        {
            return;
        }

        if (_pendingRegistryRefresh != AutoBuyCandidateKinds.None)
        {
            var requested = _pendingRegistryRefresh;
            _pendingRegistryRefresh = AutoBuyCandidateKinds.None;
            _registryReconciliation = CreateRegistryReconciliation(requested);
            return;
        }

        if (Elapsed < _nextRegistryReconciliation)
        {
            return;
        }

        _registryReconciliation = CreateRegistryReconciliation(AutoBuyCandidateKinds.All);
    }

    private static RegistryReconciliation CreateRegistryReconciliation(AutoBuyCandidateKinds kinds)
    {
        var structures = (kinds & AutoBuyCandidateKinds.Structures) != 0
            ? ReadStaticList("StructureSO", "All")
            : Array.Empty<object>();
        var upgrades = (kinds & AutoBuyCandidateKinds.Upgrades) != 0
            ? ReadStaticList("UpgradeSO", "All")
            : Array.Empty<object>();
        return new RegistryReconciliation(structures, upgrades, kinds);
    }

    private void ProcessRegistryReconciliationSlice()
    {
        var reconciliation = _registryReconciliation;
        if (reconciliation is null)
        {
            return;
        }

        var remaining = RegistryItemsPerEvaluation;
        while (remaining > 0 && reconciliation.TryTakeNext(out var source, out var kind))
        {
            remaining--;
            if (source is null)
            {
                continue;
            }

            var uuid = ReflectionUtil.ReadStableId(source) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(uuid) &&
                _index.TryReuseObservedCandidate(
                    uuid,
                    source,
                    kind,
                    reconciliation.Seen,
                    out var reusedEpochChanged))
            {
                reconciliation.ReplacementDetected |= reusedEpochChanged;
                continue;
            }

            var candidate = new ReflectionAutoBuyCandidate(source, kind, _resourceSnapshots);
            if (_index.ObserveCandidate(candidate, reconciliation.Seen))
            {
                reconciliation.ReplacementDetected = true;
            }
        }

        if (!reconciliation.IsComplete)
        {
            return;
        }

        if (!reconciliation.CompletionStarted)
        {
            reconciliation.CompletionStarted = true;
            _index.BeginRegistryCompletion(reconciliation.Seen, reconciliation.CompletionKind);
        }

        if (remaining <= 0)
        {
            return;
        }

        _index.ProcessRegistryCompletion(remaining);
        if (_index.RegistryCompletionPending)
        {
            return;
        }

        if (reconciliation.ReplacementDetected)
        {
            // Coalesce any number of recreated native objects into one epoch.
            _index.InvalidateLifecycleIncrementally();
        }

        _registryReconciliation = null;
        if (reconciliation.IsFull)
        {
            _nextRegistryReconciliation = Elapsed + RegistryReconciliationInterval;
        }
    }

    private static IList ReadStaticList(string typeName, string memberName)
    {
        var type = ReflectionUtil.FindLoadedType(typeName);
        object? value = type?.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ??
                        type?.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
        return value as IList ?? Array.Empty<object>();
    }

    private sealed class RegistryReconciliation
    {
        private readonly IList _structures;
        private readonly IList _upgrades;
        private int _structureIndex;
        private int _upgradeIndex;

        public RegistryReconciliation(
            IList structures,
            IList upgrades,
            AutoBuyCandidateKinds kinds)
        {
            _structures = structures;
            _upgrades = upgrades;
            Kinds = kinds;
        }

        public AutoBuyCandidateKinds Kinds { get; }

        public bool IsFull => Kinds == AutoBuyCandidateKinds.All;

        public AutoBuyCandidateKind? CompletionKind => Kinds switch
        {
            AutoBuyCandidateKinds.Structures => AutoBuyCandidateKind.Structure,
            AutoBuyCandidateKinds.Upgrades => AutoBuyCandidateKind.Upgrade,
            _ => null,
        };

        public HashSet<string> Seen { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsComplete => _structureIndex >= _structures.Count && _upgradeIndex >= _upgrades.Count;

        public bool CompletionStarted { get; set; }

        public bool ReplacementDetected { get; set; }

        public bool TryTakeNext(out object source, out AutoBuyCandidateKind kind)
        {
            if (_structureIndex < _structures.Count)
            {
                var candidate = _structures[_structureIndex++];
                source = candidate!;
                kind = AutoBuyCandidateKind.Structure;
                return true;
            }

            if (_upgradeIndex < _upgrades.Count)
            {
                var candidate = _upgrades[_upgradeIndex++];
                source = candidate!;
                kind = AutoBuyCandidateKind.Upgrade;
                return true;
            }

            source = null!;
            kind = default;
            return false;
        }
    }
}

internal sealed class ReflectionAutoBuyCandidate :
    IAutoBuyCandidate,
    IAutoBuyLifecycleCandidate,
    IAutoBuyNativeIdentity,
    IAutoBuyDirtyCandidate,
    IAutoBuyPriorityCandidate,
    IAutoBuyMutationCandidate
{
    private readonly object _source;
    private readonly AutoBuyCandidateKind _kind;
    private readonly Type _sourceType;
    private readonly MethodInfo? _isAvailable;
    private readonly MethodInfo? _canPurchase;
    private readonly MethodInfo? _getPurchaseCost;
    private readonly MethodInfo? _purchase;
    private readonly MethodInfo? _getPurchaseLevel;
    private readonly MethodInfo? _getQueuedState;
    private readonly MethodInfo? _hasFiniteLevels;
    private readonly MethodInfo? _isMaxLevel;
    private readonly MethodInfo? _isMaxQueuedLevel;
    private readonly bool _expectedNativeType;
    private readonly AutoBuyResourceSnapshotCache _resourceSnapshots;
    private AutoBuyEconomicPriority _economicPriority;
    private bool _economicPriorityClassified;
    private readonly List<AutoBuyResourceDefinition> _costDefinitions = new List<AutoBuyResourceDefinition>();
    private readonly List<DecodedResourceCost> _decodedCosts = new List<DecodedResourceCost>();
    private readonly List<DecodedResourceCost> _combinedCosts = new List<DecodedResourceCost>();
    private readonly List<ResourceAdmissionCost> _admissionCosts = new List<ResourceAdmissionCost>();
    private readonly List<string> _resourceDependencies = new List<string>();
    private AutoBuyCandidateSnapshot? _snapshot;
    private bool _costDirty = true;
    private bool _hasResolvedCosts;
    private int _adapterFailureCount;
    private long _nextAdapterRetryEpoch;
    private long _lastAdapterWarningEpoch = long.MinValue;
    private bool _hasCachedAvailability;
    private bool _cachedAvailability;
    private long _completionRefreshGeneration = -1;
    private string? _mutationBlockedReason;
    private NativeMutationEvidence<int>? _lastMutationEvidence;

    public ReflectionAutoBuyCandidate(
        object source,
        AutoBuyCandidateKind kind,
        AutoBuyResourceSnapshotCache resourceSnapshots)
    {
        _source = source;
        _kind = kind;
        _resourceSnapshots = resourceSnapshots;
        _sourceType = source.GetType();
        _expectedNativeType = HasExpectedNativeType(_sourceType, kind);
        _isAvailable = FindNoArgMethod("IsAvailable", typeof(bool));
        _canPurchase = FindNoArgMethod("CanPurchase", typeof(bool));
        _getPurchaseCost = FindNoArgMethod("GetPurchaseCost", null);
        _purchase = kind == AutoBuyCandidateKind.Structure
            ? _sourceType.GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, new[] { typeof(bool) }, null)
            : _sourceType.GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
        _getPurchaseLevel = FindNoArgMethod("GetPurchaseLevel", typeof(int));
        _getQueuedState = FindNoArgMethod(
            kind == AutoBuyCandidateKind.Structure ? "GetQueuedQuantity" : "GetQueuedPurchaseLevel",
            typeof(int));
        if (kind == AutoBuyCandidateKind.Upgrade)
        {
            _hasFiniteLevels = FindNoArgMethod("HasFiniteLevels", typeof(bool));
            _isMaxLevel = FindNoArgMethod("IsMaxLevel", typeof(bool));
            _isMaxQueuedLevel = FindNoArgMethod("IsMaxQueuedLevel", typeof(bool));
        }
    }

    public object NativeIdentity => _source;

    public AutoBuyEconomicPriority EconomicPriority
    {
        get
        {
            if (!_economicPriorityClassified)
            {
                _economicPriority = _kind == AutoBuyCandidateKind.Structure && _expectedNativeType
                    ? NativeStructurePriorityClassifier.Classify(_source)
                    : AutoBuyEconomicPriority.None;
                _economicPriorityClassified = true;
            }

            return _economicPriority;
        }
    }

    public IReadOnlyList<string> ResourceDependencies => _resourceDependencies;

    public bool HasResolvedCosts => !_costDirty && _hasResolvedCosts;

    public AutoBuyCandidateSnapshot Snapshot()
    {
        return _snapshot ??= new AutoBuyCandidateSnapshot(
            this,
            ReflectionUtil.ReadStableId(_source) ?? string.Empty,
            ReflectionUtil.ReadDisplayName(_source) ?? _sourceType.Name,
            _kind,
            _sourceType.FullName ?? _sourceType.Name);
    }

    public bool IsAvailable()
    {
        return _hasCachedAvailability
            ? _cachedAvailability
            : TryInvoke(_isAvailable, out bool available) && available;
    }

    public bool CanPurchase(out string reason)
    {
        if (_mutationBlockedReason is not null)
        {
            reason = _mutationBlockedReason;
            return false;
        }

        if (!TryInvoke(_canPurchase, out bool canPurchase))
        {
            reason = "CanPurchase unavailable";
            return false;
        }

        reason = canPurchase ? string.Empty : "native CanPurchase returned false";
        return canPurchase;
    }

    public IReadOnlyList<ResourceAdmissionCost> GetCosts()
    {
        if (_costDirty)
        {
            _hasResolvedCosts = false;
            if (_resourceSnapshots.Epoch < _nextAdapterRetryEpoch)
            {
                _admissionCosts.Clear();
                return _admissionCosts;
            }

            var costContainer = Invoke(_getPurchaseCost);
            if (costContainer is null)
            {
                RecordAdapterFailure("native GetPurchaseCost result is unavailable");
                _admissionCosts.Clear();
                return _admissionCosts;
            }

            if (!NativeResourceCostAdapter.TryRead(
                    costContainer,
                    _decodedCosts,
                    out _,
                    out var adapterReason) ||
                !ApplyDecodedCosts())
            {
                RecordAdapterFailure(
                    string.IsNullOrWhiteSpace(adapterReason)
                        ? "native ResourceCostList contained contradictory duplicate resources"
                        : adapterReason);
                _admissionCosts.Clear();
                return _admissionCosts;
            }

            _costDirty = false;
            _hasResolvedCosts = true;
            _adapterFailureCount = 0;
            _nextAdapterRetryEpoch = 0;
        }

        _admissionCosts.Clear();
        for (var i = 0; i < _costDefinitions.Count; i++)
        {
            var definition = _costDefinitions[i];
            if (!_resourceSnapshots.TryResolve(definition, out var resource))
            {
                _hasResolvedCosts = false;
                _admissionCosts.Clear();
                return _admissionCosts;
            }

            _admissionCosts.Add(new ResourceAdmissionCost(
                definition.ResourceId,
                definition.ResourceName,
                definition.NominalCost,
                resource.TrueQuantity,
                resource.Capacity,
                resource.IsBandwidth));
        }

        return _admissionCosts;
    }

    private bool ApplyDecodedCosts()
    {
        _combinedCosts.Clear();
        for (var i = 0; i < _decodedCosts.Count; i++)
        {
            var decoded = _decodedCosts[i];
            var combined = false;
            for (var j = 0; j < _combinedCosts.Count; j++)
            {
                var existing = _combinedCosts[j];
                if (!string.Equals(existing.ResourceId, decoded.ResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ReferenceEquals(existing.NativeResource, decoded.NativeResource))
                {
                    return false;
                }

                _combinedCosts[j] = new DecodedResourceCost(
                    existing.ResourceId,
                    existing.NativeResource,
                    existing.Amount.Add(decoded.Amount));
                combined = true;
                break;
            }

            if (!combined)
            {
                _combinedCosts.Add(decoded);
            }
        }

        for (var i = 0; i < _combinedCosts.Count; i++)
        {
            var decoded = _combinedCosts[i];
            if (i < _costDefinitions.Count &&
                string.Equals(_costDefinitions[i].ResourceId, decoded.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                ReferenceEquals(_costDefinitions[i].NativeResource, decoded.NativeResource))
            {
                _costDefinitions[i].NominalCost = decoded.Amount;
                continue;
            }

            var definition = new AutoBuyResourceDefinition(
                decoded.ResourceId,
                NativeResourceCostAdapter.ReadResourceName(decoded.NativeResource) ?? decoded.NativeResource.GetType().Name,
                decoded.NativeResource,
                decoded.Amount);
            if (i < _costDefinitions.Count)
            {
                _costDefinitions[i] = definition;
            }
            else
            {
                _costDefinitions.Add(definition);
            }
        }

        if (_costDefinitions.Count > _combinedCosts.Count)
        {
            _costDefinitions.RemoveRange(_combinedCosts.Count, _costDefinitions.Count - _combinedCosts.Count);
        }

        _resourceDependencies.Clear();
        for (var i = 0; i < _costDefinitions.Count; i++)
        {
            _resourceDependencies.Add(_costDefinitions[i].ResourceId);
        }

        return true;
    }

    private void RecordAdapterFailure(string reason)
    {
        _adapterFailureCount = Math.Min(16, _adapterFailureCount + 1);
        var backoff = 1L << Math.Min(6, _adapterFailureCount - 1);
        _nextAdapterRetryEpoch = _resourceSnapshots.Epoch + backoff;
        if (_adapterFailureCount == 1 ||
            _resourceSnapshots.Epoch - _lastAdapterWarningEpoch >= 64)
        {
            _lastAdapterWarningEpoch = _resourceSnapshots.Epoch;
            Plugin.Log?.LogAutomataWarning(
                $"Auto Buy quarantined cost evaluation for {Snapshot().Uuid}; " +
                $"retryEpoch={_nextAdapterRetryEpoch}; reason={reason}");
        }
    }

    public void MarkDirty(AutoBuyDirtyReason reasons)
    {
        if ((reasons & AutoBuyDirtyReason.CostDirty) != 0)
        {
            _costDirty = true;
            _hasResolvedCosts = false;
        }
    }

    public void SetLifecycleEvidence(AutoBuyLifecycleEvidence evidence)
    {
        _cachedAvailability = evidence.IsAvailable;
        _hasCachedAvailability = true;
    }

    public bool TryRefreshAfterCompletion(long completionGeneration, out string reason)
    {
        if (_completionRefreshGeneration == completionGeneration)
        {
            reason = string.Empty;
            return true;
        }

        _hasCachedAvailability = false;
        MarkDirty(AutoBuyDirtyReason.CostDirty);
        if (!TryGetLifecycleEvidence(out var evidence, out reason))
        {
            return false;
        }

        SetLifecycleEvidence(evidence);
        _completionRefreshGeneration = completionGeneration;
        return true;
    }

    public bool TryGetLifecycleEvidence(out AutoBuyLifecycleEvidence evidence, out string reason)
    {
        evidence = default;
        if (!_expectedNativeType)
        {
            reason = $"native object is not an audited {_kind} type";
            return false;
        }

        if (_source is UnityEngine.Object unityObject && unityObject == null)
        {
            reason = "native Unity object was destroyed";
            return false;
        }

        if (!TryInvoke(_isAvailable, out bool available) ||
            !TryInvoke(_getPurchaseLevel, out int currentLevel) ||
            !TryInvoke(_getQueuedState, out int queuedValue))
        {
            reason = "required native lifecycle method was unavailable";
            return false;
        }

        _cachedAvailability = available;
        _hasCachedAvailability = true;

        if (_kind == AutoBuyCandidateKind.Structure)
        {
            evidence = new AutoBuyLifecycleEvidence(
                available,
                currentLevel,
                queuedValue,
                hasFiniteLevels: false,
                isMaxLevel: false,
                isMaxQueuedLevel: false);
            reason = string.Empty;
            return true;
        }

        if (!TryInvoke(_hasFiniteLevels, out bool finite) ||
            !TryInvoke(_isMaxLevel, out bool maxLevel) ||
            !TryInvoke(_isMaxQueuedLevel, out bool maxQueued))
        {
            reason = "required finite Upgrade lifecycle method was unavailable";
            return false;
        }

        var queuedLevels = queuedValue - currentLevel;
        evidence = new AutoBuyLifecycleEvidence(
            available,
            currentLevel,
            queuedLevels,
            finite,
            maxLevel,
            maxQueued);
        reason = string.Empty;
        return true;
    }

    public bool TryPurchaseOne(out string reason)
    {
        reason = string.Empty;
        if (_mutationBlockedReason is not null)
        {
            reason = _mutationBlockedReason;
            return false;
        }

        if (!CanPurchase(out reason))
        {
            return false;
        }

        return _kind == AutoBuyCandidateKind.Structure
            ? TryPurchaseStructure(out reason)
            : TryPurchaseUpgrade(out reason);
    }

    private bool TryPurchaseStructure(out string reason)
    {
        reason = string.Empty;
        var method = _purchase;
        if (method is null)
        {
            reason = "Purchase(bool forceOne) unavailable";
            return false;
        }

        return InvokeAndVerify(method, new object[] { true }, "GetQueuedQuantity", out reason);
    }

    private bool TryPurchaseUpgrade(out string reason)
    {
        reason = string.Empty;
        var method = _purchase;
        if (method is null)
        {
            reason = "Purchase() unavailable";
            return false;
        }

        if (!NativeMultiBuyScope.TryEnterOne(out var scope, out reason))
        {
            return false;
        }

        using (scope)
        {
            return InvokeAndVerify(method, Array.Empty<object>(), "GetQueuedPurchaseLevel", out reason);
        }
    }

    private bool InvokeAndVerify(MethodInfo method, object[] arguments, string levelMethod, out string reason)
    {
        var evidence = NativeMutationVerifier.Execute(
            $"Auto Buy {_kind}",
            Snapshot().Uuid,
            $"{levelMethod} exact delta +1",
            CaptureQueuedState,
            () => method.Invoke(_source, arguments),
            (before, after) => after == before + 1);
        _lastMutationEvidence = evidence;
        if (evidence.IsVerified)
        {
            reason = string.Empty;
            return true;
        }

        reason = evidence.Format();
        if (evidence.MutationWasAttempted)
        {
            _mutationBlockedReason = $"native mutation blocked until the next lifecycle: {reason}";
            Plugin.Log?.LogAutomataWarning(_mutationBlockedReason);
        }

        return false;
    }

    public void RecoverMutationBlock()
    {
        _mutationBlockedReason = null;
        _lastMutationEvidence = null;
    }

    private int CaptureQueuedState()
    {
        if (!TryInvoke(_getQueuedState, out int queued))
        {
            throw new InvalidOperationException("native queued state is unavailable");
        }

        return queued;
    }

    private MethodInfo? FindNoArgMethod(string name, Type? returnType)
    {
        var method = _sourceType.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
        return method is not null && (returnType is null || method.ReturnType == returnType) ? method : null;
    }

    private object? Invoke(MethodInfo? method)
    {
        try
        {
            return method?.Invoke(_source, Array.Empty<object>());
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private bool TryInvoke<T>(MethodInfo? method, out T value)
    {
        var result = Invoke(method);
        if (result is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    private static bool HasExpectedNativeType(Type type, AutoBuyCandidateKind kind)
    {
        var expected = kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO";
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.Name, expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class NativeIntVariableContract
{
    private static readonly object CacheGate = new object();
    private static readonly Dictionary<Type, NativeIntVariableContract> Cache =
        new Dictionary<Type, NativeIntVariableContract>();
    private readonly Type _variableType;
    private readonly MethodInfo _asInt;
    private readonly MethodInfo _setValue;

    private NativeIntVariableContract(Type variableType, MethodInfo asInt, MethodInfo setValue)
    {
        _variableType = variableType;
        _asInt = asInt;
        _setValue = setValue;
    }

    internal static int ResolutionCount { get; private set; }

    public static bool TryResolve(
        object variable,
        out NativeIntVariableContract contract,
        out string reason)
    {
        var variableType = variable.GetType();
        lock (CacheGate)
        {
            if (Cache.TryGetValue(variableType, out contract!))
            {
                reason = string.Empty;
                return true;
            }
        }

        if (!string.Equals(variableType.Name, "IntVariable", StringComparison.Ordinal))
        {
            contract = null!;
            reason = $"unexpected global multi-buy variable type {variableType.FullName}";
            return false;
        }

        var asInt = variableType.GetMethod(
            "AsInt",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        var setValue = variableType.GetMethod(
            "SetValue",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(int) },
            null);
        if (asInt?.ReturnType != typeof(int) || setValue?.ReturnType != typeof(void))
        {
            contract = null!;
            reason = "global multi-buy requires exact IntVariable.AsInt() and SetValue(int) contracts";
            return false;
        }

        contract = new NativeIntVariableContract(variableType, asInt, setValue);
        lock (CacheGate)
        {
            Cache[variableType] = contract;
            ResolutionCount++;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryRead(object variable, out int value, out string reason)
    {
        if (variable.GetType() != _variableType)
        {
            value = 0;
            reason = "global multi-buy variable runtime type changed";
            return false;
        }

        try
        {
            if (_asInt.Invoke(variable, Array.Empty<object>()) is int current)
            {
                value = current;
                reason = string.Empty;
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            value = 0;
            reason = DescribeException(ex);
            return false;
        }

        value = 0;
        reason = "global multi-buy AsInt() did not return Int32";
        return false;
    }

    public bool TrySet(object variable, int value, out string reason)
    {
        if (variable.GetType() != _variableType)
        {
            reason = "global multi-buy variable runtime type changed";
            return false;
        }

        try
        {
            _setValue.Invoke(variable, new object[] { value });
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = DescribeException(ex);
            return false;
        }
    }

    internal static void ResetForTests()
    {
        lock (CacheGate)
        {
            Cache.Clear();
            ResolutionCount = 0;
        }
    }

    private static string DescribeException(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null } target
            ? target.InnerException.Message
            : exception.Message;
    }
}

internal static class NativeGlobalVariableAccess
{
    private static readonly object ContractGate = new object();
    private static MethodInfo? _getMultiBuy;

    internal static int ResolutionCount { get; private set; }

    public static bool TryGetMultiBuy(
        out object variable,
        out NativeIntVariableContract contract,
        out string reason)
    {
        variable = null!;
        contract = null!;
        MethodInfo? getMultiBuy;
        lock (ContractGate)
        {
            getMultiBuy = _getMultiBuy;
        }

        if (getMultiBuy is null)
        {
            var globals = ReflectionUtil.FindLoadedType("GlobalVariables");
            var resolved = globals?.GetMethod(
                "GetMultiBuy",
                BindingFlags.Static | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (resolved is null ||
                !string.Equals(resolved.ReturnType.Name, "IntVariable", StringComparison.Ordinal))
            {
                reason = "GlobalVariables.GetMultiBuy() -> IntVariable contract unavailable";
                return false;
            }

            lock (ContractGate)
            {
                _getMultiBuy ??= resolved;
                getMultiBuy = _getMultiBuy;
                ResolutionCount++;
            }
        }

        try
        {
            variable = getMultiBuy.Invoke(null, Array.Empty<object>())!;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex is TargetInvocationException { InnerException: not null } target
                ? target.InnerException.Message
                : ex.Message;
            return false;
        }

        if (variable is null)
        {
            reason = "global multi-buy value unavailable";
            return false;
        }

        if (variable.GetType() != getMultiBuy.ReturnType)
        {
            reason = $"global multi-buy runtime type {variable.GetType().FullName} did not match " +
                     $"{getMultiBuy.ReturnType.FullName}";
            return false;
        }

        return NativeIntVariableContract.TryResolve(variable, out contract, out reason);
    }

    internal static void ResetForTests()
    {
        lock (ContractGate)
        {
            _getMultiBuy = null;
            ResolutionCount = 0;
        }
    }
}

internal sealed class NativeMultiBuyScope : IDisposable
{
    private static readonly DecisionLogGate FailureLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private static readonly System.Diagnostics.Stopwatch Lifetime = System.Diagnostics.Stopwatch.StartNew();
    private readonly object _variable;
    private readonly NativeIntVariableContract _contract;
    private readonly int _originalValue;
    private static bool _mutationQuarantined;
    private static string _quarantineReason = string.Empty;
    private bool _disposed;

    private NativeMultiBuyScope(
        object variable,
        NativeIntVariableContract contract,
        int originalValue)
    {
        _variable = variable;
        _contract = contract;
        _originalValue = originalValue;
    }

    internal static int GlobalContractResolutionCount => NativeGlobalVariableAccess.ResolutionCount;

    internal static int VariableContractResolutionCount => NativeIntVariableContract.ResolutionCount;

    public static bool TryEnterOne(out NativeMultiBuyScope scope, out string reason)
    {
        scope = null!;
        reason = string.Empty;
        if (_mutationQuarantined)
        {
            reason = $"global multi-buy mutation is quarantined: {_quarantineReason}";
            return false;
        }

        if (!NativeGlobalVariableAccess.TryGetMultiBuy(out var variable, out var contract, out reason) ||
            !contract.TryRead(variable, out var originalValue, out reason))
        {
            return false;
        }

        if (!contract.TrySet(variable, 1, out var setterFailure))
        {
            var restored = TryRestore(variable, contract, originalValue, out var restorationDetail);
            reason = $"global multi-buy SetValue(1) failed: {setterFailure}; {restorationDetail}";
            if (!restored)
            {
                Quarantine(reason);
            }
            else
            {
                LogFailure(reason);
            }

            return false;
        }

        if (!contract.TryRead(variable, out var enteredValue, out _) || enteredValue != 1)
        {
            var restored = TryRestore(variable, contract, originalValue, out var restorationDetail);
            reason = $"global multi-buy SetValue(1) could not be verified; {restorationDetail}";
            if (!restored)
            {
                Quarantine(reason);
            }
            else
            {
                LogFailure(reason);
            }

            return false;
        }

        scope = new NativeMultiBuyScope(variable, contract, originalValue);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!TryRestore(_variable, _contract, _originalValue, out var restorationDetail))
        {
            Quarantine($"global multi-buy cleanup failed: {restorationDetail}");
        }
        else if (restorationDetail.Contains("threw", StringComparison.OrdinalIgnoreCase))
        {
            LogFailure($"global multi-buy cleanup recovered after an exception: {restorationDetail}");
        }
    }

    internal static bool IsMutationQuarantined => _mutationQuarantined;

    internal static bool TryGetMutationQuarantine(out string reason)
    {
        reason = _quarantineReason;
        return _mutationQuarantined;
    }

    internal static void ResetQuarantineForTests()
    {
        _mutationQuarantined = false;
        _quarantineReason = string.Empty;
        NativeGlobalVariableAccess.ResetForTests();
        NativeIntVariableContract.ResetForTests();
    }

    private static bool TryRestore(
        object variable,
        NativeIntVariableContract contract,
        int originalValue,
        out string detail)
    {
        string? setterFailure = null;
        if (!contract.TrySet(variable, originalValue, out var failure))
        {
            setterFailure = failure;
        }

        if (contract.TryRead(variable, out var restoredValue, out _) && restoredValue == originalValue)
        {
            detail = setterFailure is null
                ? $"restoration to {originalValue} verified"
                : $"restoration setter threw ({setterFailure}) but value {originalValue} was verified";
            return true;
        }

        detail = setterFailure is null
            ? $"restoration to {originalValue} could not be verified"
            : $"restoration setter threw ({setterFailure}) and value {originalValue} could not be verified";
        return false;
    }

    private static void Quarantine(string reason)
    {
        _mutationQuarantined = true;
        _quarantineReason = reason;
    }

    private static void LogFailure(string message)
    {
        if (FailureLogGate.ShouldLog("native-multi-buy-failure", Lifetime.Elapsed))
        {
            Plugin.Log?.LogAutomataError(message);
        }
    }
}
