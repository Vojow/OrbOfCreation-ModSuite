using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace OrbAutomata;

internal sealed class ReflectionAutoBuyCatalog : IAutoBuyCatalog, IAutoBuyIncrementalCatalog
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
    private RegistryReconciliation? _registryReconciliation;
    private TimeSpan _nextRegistryReconciliation;
    private bool _completionEffectsDirty;

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
            _index.InvalidateResource);
    }

    public IEnumerable<IAutoBuyCandidate> Discover()
    {
        return BeginEvaluation(new AutoBuyEvaluationRequest(int.MaxValue, true, true)).ActiveCandidates;
    }

    public AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request)
    {
        if (_completionEffectsDirty)
        {
            _completionEffectsDirty = false;
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

    public void CompleteCandidateEvaluation(IAutoBuyCandidate candidate, bool suppressResourceTracking)
    {
        _index.CompleteCandidateEvaluation(candidate, suppressResourceTracking);
    }

    public void InvalidatePolicy()
    {
        _index.InvalidatePolicy();
    }

    public void BeginMutationEvaluation()
    {
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void NotifyPurchaseAttempted(IAutoBuyCandidate candidate)
    {
        _index.MarkPurchaseAttempted(candidate);
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void NotifyStructureQueueChanged(object nativeIdentity)
    {
        _index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Structure);
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void NotifyUpgradeQueueChanged(object nativeIdentity)
    {
        _index.InvalidateQueue(nativeIdentity, AutoBuyCandidateKind.Upgrade);
        _resourceSnapshots.BeginLazyEpoch();
    }

    public void NotifyNativeCompletion()
    {
        _completionEffectsDirty = true;
    }

    public void InvalidateLifecycle()
    {
        _resourceSnapshots.Clear();
        _registryReconciliation = null;
        _completionEffectsDirty = false;
        _nextRegistryReconciliation = TimeSpan.Zero;
        _maintenanceCadence.Reset(Elapsed);
        _index.InvalidateLifecycleIncrementally();
    }

    public bool TryGetRemainingQueueRoom(out int remainingRoom)
    {
        remainingRoom = 0;
        var type = ReflectionUtil.FindLoadedType("ActionManager");
        var method = type?.GetMethod("GetRemainingRoom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        try
        {
            var value = method?.Invoke(null, Array.Empty<object>());
            if (value is int room)
            {
                remainingRoom = room;
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
        }

        return false;
    }

    public bool TryGetBulkDevelopment(out int levels)
    {
        levels = 1;
        var player = ReflectionUtil.FindLoadedType("Player");
        var method = player?.GetMethod("GetBulkDevelopment", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        try
        {
            var variable = method?.Invoke(null, Array.Empty<object>());
            if (variable is not null && ReflectionUtil.TryReadNumeric(variable, out var value, "AsInt"))
            {
                levels = Math.Max(1, (int)value);
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
        var globals = ReflectionUtil.FindLoadedType("GlobalVariables");
        var method = globals?.GetMethod("GetMultiBuy", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        try
        {
            var variable = method?.Invoke(null, Array.Empty<object>());
            if (variable is not null && ReflectionUtil.TryReadNumeric(variable, out var value, "AsInt"))
            {
                multiplier = Math.Max(1, (int)value);
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
        }

        return false;
    }

    public void Dispose()
    {
        _registryReconciliation = null;
        _resourceSnapshots.Clear();
        _index.Clear();
    }

    private TimeSpan Elapsed => _readElapsed?.Invoke() ?? _lifetime.Elapsed;

    private void StartRegistryReconciliationIfDue()
    {
        if (_registryReconciliation is not null || Elapsed < _nextRegistryReconciliation)
        {
            return;
        }

        _registryReconciliation = new RegistryReconciliation(
            ReadStaticList("StructureSO", "All"),
            ReadStaticList("UpgradeSO", "All"));
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
            _index.BeginRegistryCompletion(reconciliation.Seen);
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
        _nextRegistryReconciliation = Elapsed + RegistryReconciliationInterval;
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

        public RegistryReconciliation(IList structures, IList upgrades)
        {
            _structures = structures;
            _upgrades = upgrades;
        }

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
    IAutoBuyPriorityCandidate
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
                resource.Capacity));
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
        reason = string.Empty;
        ReflectionUtil.TryReadNumeric(_source, out var before, levelMethod);
        try
        {
            method.Invoke(_source, arguments);
        }
        catch (TargetInvocationException ex)
        {
            reason = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }

        if (!ReflectionUtil.TryReadNumeric(_source, out var after, levelMethod) || after <= before)
        {
            reason = $"native purchase did not increase {levelMethod}";
            return false;
        }

        return true;
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

internal sealed class NativeMultiBuyScope : IDisposable
{
    private static readonly DecisionLogGate FailureLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private static readonly System.Diagnostics.Stopwatch Lifetime = System.Diagnostics.Stopwatch.StartNew();
    private readonly object _variable;
    private readonly MethodInfo _setValue;
    private readonly int _originalValue;
    private static bool _mutationQuarantined;
    private static string _quarantineReason = string.Empty;
    private bool _disposed;

    private NativeMultiBuyScope(object variable, MethodInfo setValue, int originalValue)
    {
        _variable = variable;
        _setValue = setValue;
        _originalValue = originalValue;
    }

    public static bool TryEnterOne(out NativeMultiBuyScope scope, out string reason)
    {
        scope = null!;
        reason = string.Empty;
        if (_mutationQuarantined)
        {
            reason = $"global multi-buy mutation is quarantined: {_quarantineReason}";
            return false;
        }

        var globals = ReflectionUtil.FindLoadedType("GlobalVariables");
        var getMultiBuy = globals?.GetMethod("GetMultiBuy", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        object? variable;
        try
        {
            variable = getMultiBuy?.Invoke(null, Array.Empty<object>());
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }

        if (variable is null || !ReflectionUtil.TryReadNumeric(variable, out var original, "AsInt"))
        {
            reason = "global multi-buy value unavailable";
            return false;
        }

        var setValue = variable.GetType().GetMethod("SetValue", ReflectionUtil.InstanceFlags, null, new[] { typeof(int) }, null);
        if (setValue is null)
        {
            reason = "global multi-buy SetValue(int) unavailable";
            return false;
        }

        var originalValue = (int)original;
        try
        {
            setValue.Invoke(variable, new object[] { 1 });
        }
        catch (Exception ex)
        {
            var originalFailure = DescribeException(ex);
            var restored = TryRestore(variable, setValue, originalValue, out var restorationDetail);
            reason = $"global multi-buy SetValue(1) failed: {originalFailure}; {restorationDetail}";
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

        if (!ReflectionUtil.TryReadNumeric(variable, out var enteredValue, "AsInt") || enteredValue != 1)
        {
            var restored = TryRestore(variable, setValue, originalValue, out var restorationDetail);
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

        scope = new NativeMultiBuyScope(variable, setValue, originalValue);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!TryRestore(_variable, _setValue, _originalValue, out var restorationDetail))
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
    }

    private static bool TryRestore(
        object variable,
        MethodInfo setValue,
        int originalValue,
        out string detail)
    {
        string? setterFailure = null;
        try
        {
            setValue.Invoke(variable, new object[] { originalValue });
        }
        catch (Exception ex)
        {
            setterFailure = DescribeException(ex);
        }

        if (ReflectionUtil.TryReadNumeric(variable, out var restoredValue, "AsInt") && restoredValue == originalValue)
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

    private static string DescribeException(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null } target
            ? target.InnerException.Message
            : exception.Message;
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
