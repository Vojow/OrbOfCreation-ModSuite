using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal sealed class AutoBuyCandidateIndex
{
    private readonly Dictionary<string, Entry> _entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<Entry>> _resourceDependents =
        new Dictionary<string, HashSet<Entry>>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> _activeEntries = new List<Entry>();
    private readonly List<Entry> _lifecycleEntries = new List<Entry>();
    private readonly Queue<Entry> _lifecycleDirty = new Queue<Entry>();
    private readonly List<IAutoBuyCandidate> _active = new List<IAutoBuyCandidate>();
    private readonly List<IAutoBuyCandidate> _dirty = new List<IAutoBuyCandidate>();
    private IEnumerator<KeyValuePair<string, Entry>>? _registryCompletionSweep;
    private ISet<string>? _registrySeen;
    private int _activeRefreshCursor;
    private int _slowRefreshCursor;
    private int _epochValidationPending;
    private long _epoch = 1;

    public long Epoch => _epoch;

    public bool RegistryCompletionPending => _registryCompletionSweep is not null;

    public bool EpochValidationPending => _epochValidationPending > 0;

    public IReadOnlyList<IAutoBuyCandidate> Reconcile(IEnumerable<IAutoBuyCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var epochChanged = false;
        foreach (var candidate in candidates)
        {
            epochChanged |= ObserveCandidate(candidate, seen);
        }

        CompleteRegistryReconciliation(seen);
        if (epochChanged)
        {
            BeginLifecycleEpochCore();
        }

        MarkAll(AutoBuyDirtyReason.LifecycleDirty);
        if (ProcessLifecycleDirty(int.MaxValue))
        {
            BeginLifecycleEpochCore();
            ProcessLifecycleDirty(int.MaxValue);
        }

        RebuildPublicActiveList();
        return _active;
    }

    public bool ObserveCandidate(IAutoBuyCandidate candidate, ISet<string> seen)
    {
        AutoBuyCandidateSnapshot snapshot;
        try
        {
            snapshot = candidate.Snapshot();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.Uuid))
        {
            return false;
        }

        seen.Add(snapshot.Uuid);
        if (!_entries.TryGetValue(snapshot.Uuid, out var entry))
        {
            entry = new Entry(candidate, snapshot, _epoch);
            _entries.Add(snapshot.Uuid, entry);
            _lifecycleEntries.Add(entry);
            MarkDirty(entry, AutoBuyDirtyReason.All);
            return false;
        }

        if (entry.Definition.Kind != snapshot.Kind ||
            !string.Equals(entry.Definition.ReflectedType, snapshot.ReflectedType, StringComparison.Ordinal))
        {
            MarkInvalid(entry, "UUID was registered with a different native type");
            return false;
        }

        if (!ReferenceEquals(GetNativeIdentity(entry.Candidate), GetNativeIdentity(candidate)))
        {
            RemoveDependencies(entry);
            SetHotEligibility(entry, false);
            entry.Replace(candidate, _epoch + 1);
            MarkDirty(entry, AutoBuyDirtyReason.All);
            return true;
        }

        if (entry.State == AutoBuyCandidateLifecycleState.Invalid)
        {
            entry.Restore(_epoch);
            MarkDirty(entry, AutoBuyDirtyReason.All);
        }

        return false;
    }

    public void CompleteRegistryReconciliation(ISet<string> seen)
    {
        BeginRegistryCompletion(seen);
        ProcessRegistryCompletion(int.MaxValue);
    }

    public void BeginRegistryCompletion(ISet<string> seen)
    {
        _registryCompletionSweep?.Dispose();
        _registrySeen = seen;
        _registryCompletionSweep = _entries.GetEnumerator();
    }

    public void CancelRegistryCompletion()
    {
        _registryCompletionSweep?.Dispose();
        _registryCompletionSweep = null;
        _registrySeen = null;
    }

    public void ProcessRegistryCompletion(int workLimit)
    {
        var sweep = _registryCompletionSweep;
        var seen = _registrySeen;
        if (sweep is null || seen is null)
        {
            return;
        }

        var processed = 0;
        while (processed++ < Math.Max(1, workLimit) && sweep.MoveNext())
        {
            var pair = sweep.Current;
            if (!seen.Contains(pair.Key))
            {
                MarkInvalid(pair.Value, "candidate is no longer present in its native registry");
            }
        }

        if (processed <= Math.Max(1, workLimit))
        {
            sweep.Dispose();
            _registryCompletionSweep = null;
            _registrySeen = null;
        }
    }

    public AutoBuyEvaluationBatch PrepareEvaluation(
        AutoBuyEvaluationRequest request,
        int lifecycleWorkLimit,
        int activeRefreshCount,
        int slowRefreshCount)
    {
        ScheduleActiveRefresh(activeRefreshCount);
        ScheduleSlowRefresh(slowRefreshCount);
        if (ProcessLifecycleDirty(Math.Max(1, lifecycleWorkLimit)))
        {
            BeginLifecycleEpochCore();
            ProcessLifecycleDirty(Math.Max(1, lifecycleWorkLimit));
        }

        _active.Clear();
        _dirty.Clear();
        IAutoBuyCandidate? firstExcluded = null;
        var limit = Math.Max(1, request.CandidateLimit);
        for (var i = 0; i < _activeEntries.Count; i++)
        {
            var entry = _activeEntries[i];
            if (!IsIncluded(entry.Definition.Kind, request))
            {
                continue;
            }

            if (_active.Count >= limit)
            {
                firstExcluded ??= entry.Candidate;
                continue;
            }

            _active.Add(entry.Candidate);
            if (entry.DirtyReasons != AutoBuyDirtyReason.None)
            {
                entry.Candidate.AsDirtyCandidate()?.MarkDirty(entry.DirtyReasons);
                _dirty.Add(entry.Candidate);
            }
        }

        return new AutoBuyEvaluationBatch(_active, _dirty, firstExcluded, reconciliationPending: false);
    }

    public void CompleteCandidateEvaluation(IAutoBuyCandidate candidate)
    {
        if (!TryGetEntry(candidate, out var entry))
        {
            return;
        }

        if (candidate is IAutoBuyDirtyCandidate dirtyCandidate)
        {
            ReplaceDependencies(entry, dirtyCandidate.ResourceDependencies);
            entry.DirtyReasons = dirtyCandidate.HasResolvedCosts
                ? AutoBuyDirtyReason.None
                : AutoBuyDirtyReason.CostDirty;
            return;
        }

        entry.DirtyReasons = AutoBuyDirtyReason.None;
    }

    public void InvalidatePolicy()
    {
        MarkAll(AutoBuyDirtyReason.ResourceDirty | AutoBuyDirtyReason.PriorityDirty);
    }

    public void MarkPurchaseAccepted(IAutoBuyCandidate candidate)
    {
        if (!TryGetEntry(candidate, out var entry))
        {
            return;
        }

        MarkDirty(
            entry,
            AutoBuyDirtyReason.AvailabilityDirty |
            AutoBuyDirtyReason.LevelDirty |
            AutoBuyDirtyReason.CostDirty |
            AutoBuyDirtyReason.ResourceDirty |
            AutoBuyDirtyReason.PriorityDirty |
            AutoBuyDirtyReason.CompletionDirty);

        foreach (var resourceId in entry.ResourceDependencies)
        {
            InvalidateResource(resourceId, AutoBuyResourceChange.Quantity);
        }
    }

    public void InvalidateResource(string resourceId, AutoBuyResourceChange change)
    {
        if (!_resourceDependents.TryGetValue(resourceId, out var dependents))
        {
            return;
        }

        foreach (var entry in dependents)
        {
            var reasons = AutoBuyDirtyReason.ResourceDirty | AutoBuyDirtyReason.PriorityDirty;
            if ((change & (AutoBuyResourceChange.Identity |
                           AutoBuyResourceChange.Availability |
                           AutoBuyResourceChange.Unknown)) != 0)
            {
                reasons |= AutoBuyDirtyReason.CostDirty;
            }

            if (entry.Definition.Kind == AutoBuyCandidateKind.Structure &&
                (change & (AutoBuyResourceChange.Quality | AutoBuyResourceChange.AttributeCost)) != 0)
            {
                reasons |= AutoBuyDirtyReason.CostDirty;
            }

            MarkDirty(entry, reasons);
        }
    }

    public void BeginLifecycleEpoch()
    {
        BeginLifecycleEpochCore();
        ProcessLifecycleDirty(int.MaxValue);
        RebuildPublicActiveList();
    }

    public void InvalidateLifecycleIncrementally()
    {
        CancelRegistryCompletion();
        BeginLifecycleEpochCore();
    }

    public bool HasResourceDependents(string resourceId)
    {
        return _resourceDependents.TryGetValue(resourceId, out var dependents) && dependents.Count > 0;
    }

    public bool TryGetState(string uuid, out AutoBuyCandidateLifecycleState state)
    {
        if (_entries.TryGetValue(uuid, out var entry))
        {
            state = entry.State;
            return true;
        }

        state = AutoBuyCandidateLifecycleState.Invalid;
        return false;
    }

    public bool TryGetDirtyReasons(string uuid, out AutoBuyDirtyReason reasons)
    {
        if (_entries.TryGetValue(uuid, out var entry))
        {
            reasons = entry.DirtyReasons;
            return true;
        }

        reasons = AutoBuyDirtyReason.None;
        return false;
    }

    public bool TryGetCandidate(string uuid, out IAutoBuyCandidate candidate)
    {
        if (_entries.TryGetValue(uuid, out var entry))
        {
            candidate = entry.Candidate;
            return true;
        }

        candidate = null!;
        return false;
    }

    public void Clear()
    {
        _entries.Clear();
        _resourceDependents.Clear();
        _activeEntries.Clear();
        _lifecycleEntries.Clear();
        _lifecycleDirty.Clear();
        _registryCompletionSweep?.Dispose();
        _registryCompletionSweep = null;
        _registrySeen = null;
        _active.Clear();
        _dirty.Clear();
        _activeRefreshCursor = 0;
        _slowRefreshCursor = 0;
        _epochValidationPending = 0;
        _epoch++;
    }

    private bool ProcessLifecycleDirty(int limit)
    {
        var rollbackDetected = false;
        var processed = 0;
        while (_lifecycleDirty.Count > 0 && processed++ < limit)
        {
            var entry = _lifecycleDirty.Dequeue();
            entry.LifecycleQueued = false;
            if ((entry.DirtyReasons & AutoBuyDirtyReason.LifecycleDirty) == 0)
            {
                continue;
            }

            var wasEligible = entry.IsEligibleForHotSet;
            var oldState = entry.State;
            rollbackDetected |= RefreshLifecycle(entry);
            CompleteEpochValidation(entry);
            if (entry.State == AutoBuyCandidateLifecycleState.Invalid || !entry.IsEligibleForHotSet)
            {
                entry.DirtyReasons = AutoBuyDirtyReason.None;
                RemoveDependencies(entry);
            }
            else if (!wasEligible || oldState != entry.State)
            {
                MarkDirty(entry, AutoBuyDirtyReason.EvaluationDirty);
            }
        }

        return rollbackDetected;
    }

    private bool RefreshLifecycle(Entry entry)
    {
        if (entry.Candidate is not IAutoBuyLifecycleCandidate lifecycle)
        {
            MarkInvalid(entry, "native lifecycle evidence unavailable");
            return false;
        }

        if (!lifecycle.TryGetLifecycleEvidence(out var evidence, out var reason))
        {
            MarkInvalid(entry, string.IsNullOrWhiteSpace(reason) ? "native lifecycle evidence unavailable" : reason);
            return false;
        }

        if (evidence.CurrentLevel < 0 || evidence.QueuedLevels < 0 ||
            (evidence.IsMaxLevel && !evidence.HasFiniteLevels) ||
            (evidence.IsMaxLevel && evidence.QueuedLevels > 0) ||
            (evidence.IsMaxLevel && !evidence.IsMaxQueuedLevel))
        {
            MarkInvalid(entry, "native lifecycle evidence is contradictory");
            return false;
        }

        var hadLevelEvidence = entry.LastCurrentLevel.HasValue && entry.LastQueuedLevels.HasValue;
        var levelChanged = hadLevelEvidence && evidence.CurrentLevel != entry.LastCurrentLevel!.Value;
        var queueChanged = hadLevelEvidence && evidence.QueuedLevels != entry.LastQueuedLevels!.Value;
        var rollbackDetected = entry.LastCurrentLevel.HasValue &&
                               evidence.CurrentLevel < entry.LastCurrentLevel.Value;
        entry.LastCurrentLevel = evidence.CurrentLevel;
        entry.LastQueuedLevels = evidence.QueuedLevels;
        entry.Epoch = _epoch;
        entry.InvalidReason = string.Empty;
        entry.Candidate.AsDirtyCandidate()?.SetLifecycleEvidence(evidence);

        AutoBuyCandidateLifecycleState newState;
        var eligible = false;
        if (evidence.HasFiniteLevels && evidence.IsMaxLevel && evidence.QueuedLevels == 0)
        {
            newState = AutoBuyCandidateLifecycleState.Completed;
        }
        else if (evidence.HasFiniteLevels && evidence.IsMaxQueuedLevel && evidence.QueuedLevels > 0)
        {
            newState = AutoBuyCandidateLifecycleState.TerminalQueued;
        }
        else if (evidence.QueuedLevels > 0)
        {
            newState = AutoBuyCandidateLifecycleState.Queued;
            eligible = evidence.IsAvailable;
        }
        else
        {
            newState = evidence.IsAvailable
                ? AutoBuyCandidateLifecycleState.Available
                : AutoBuyCandidateLifecycleState.Locked;
            eligible = evidence.IsAvailable;
        }

        entry.State = newState;
        SetHotEligibility(entry, eligible);
        entry.DirtyReasons &= ~AutoBuyDirtyReason.LifecycleDirty;
        if (levelChanged || queueChanged)
        {
            // Manual purchases and native action completion can change the
            // next cost without going through Automata's purchase callback.
            MarkDirty(
                entry,
                AutoBuyDirtyReason.CostDirty |
                AutoBuyDirtyReason.ResourceDirty |
                AutoBuyDirtyReason.PriorityDirty);
        }

        return rollbackDetected;
    }

    private void ScheduleActiveRefresh(int count)
    {
        if (_activeEntries.Count == 0)
        {
            _activeRefreshCursor = 0;
            return;
        }

        for (var i = 0; i < Math.Min(count, _activeEntries.Count); i++)
        {
            if (_activeRefreshCursor >= _activeEntries.Count)
            {
                _activeRefreshCursor = 0;
            }

            MarkDirty(
                _activeEntries[_activeRefreshCursor++],
                AutoBuyDirtyReason.AvailabilityDirty |
                AutoBuyDirtyReason.LevelDirty |
                AutoBuyDirtyReason.CompletionDirty |
                AutoBuyDirtyReason.PriorityDirty);
        }
    }

    private void ScheduleSlowRefresh(int count)
    {
        if (_lifecycleEntries.Count == 0 || count <= 0)
        {
            _slowRefreshCursor = 0;
            return;
        }

        for (var i = 0; i < Math.Min(count, _lifecycleEntries.Count); i++)
        {
            if (_slowRefreshCursor >= _lifecycleEntries.Count)
            {
                _slowRefreshCursor = 0;
            }

            var entry = _lifecycleEntries[_slowRefreshCursor++];
            if (entry.State != AutoBuyCandidateLifecycleState.Invalid)
            {
                MarkDirty(entry, AutoBuyDirtyReason.LifecycleDirty);
            }
        }
    }

    private void BeginLifecycleEpochCore()
    {
        _epoch++;
        _resourceDependents.Clear();
        _activeEntries.Clear();
        _lifecycleDirty.Clear();
        _epochValidationPending = 0;
        foreach (var entry in _lifecycleEntries)
        {
            if (entry.State == AutoBuyCandidateLifecycleState.Invalid)
            {
                continue;
            }

            entry.BeginEpoch(_epoch);
            entry.NeedsEpochValidation = true;
            _epochValidationPending++;
            MarkDirty(entry, AutoBuyDirtyReason.All);
        }
    }

    private void MarkAll(AutoBuyDirtyReason reasons)
    {
        foreach (var entry in _lifecycleEntries)
        {
            if (entry.State != AutoBuyCandidateLifecycleState.Invalid)
            {
                MarkDirty(entry, reasons);
            }
        }
    }

    private void MarkDirty(Entry entry, AutoBuyDirtyReason reasons)
    {
        if (reasons == AutoBuyDirtyReason.None)
        {
            return;
        }

        entry.DirtyReasons |= reasons;
        entry.Candidate.AsDirtyCandidate()?.MarkDirty(reasons);
        if ((entry.DirtyReasons & AutoBuyDirtyReason.LifecycleDirty) != 0 && !entry.LifecycleQueued)
        {
            entry.LifecycleQueued = true;
            _lifecycleDirty.Enqueue(entry);
        }
    }

    private void MarkInvalid(Entry entry, string reason)
    {
        CompleteEpochValidation(entry);
        SetHotEligibility(entry, false);
        entry.State = AutoBuyCandidateLifecycleState.Invalid;
        entry.Epoch = _epoch;
        entry.InvalidReason = reason;
        entry.DirtyReasons = AutoBuyDirtyReason.None;
        entry.LifecycleQueued = false;
        RemoveDependencies(entry);
    }

    private void CompleteEpochValidation(Entry entry)
    {
        if (!entry.NeedsEpochValidation)
        {
            return;
        }

        entry.NeedsEpochValidation = false;
        _epochValidationPending = Math.Max(0, _epochValidationPending - 1);
    }

    private void SetHotEligibility(Entry entry, bool eligible)
    {
        if (entry.IsEligibleForHotSet == eligible)
        {
            return;
        }

        entry.IsEligibleForHotSet = eligible;
        if (!eligible)
        {
            _activeEntries.Remove(entry);
            return;
        }

        var index = _activeEntries.BinarySearch(entry, EntryComparer.Instance);
        _activeEntries.Insert(index < 0 ? ~index : index, entry);
    }

    private void ReplaceDependencies(Entry entry, IReadOnlyList<string> dependencies)
    {
        RemoveDependencies(entry);
        for (var i = 0; i < dependencies.Count; i++)
        {
            var resourceId = dependencies[i];
            if (string.IsNullOrWhiteSpace(resourceId) || !entry.ResourceDependencies.Add(resourceId))
            {
                continue;
            }

            if (!_resourceDependents.TryGetValue(resourceId, out var dependents))
            {
                dependents = new HashSet<Entry>();
                _resourceDependents.Add(resourceId, dependents);
            }

            dependents.Add(entry);
        }
    }

    private void RemoveDependencies(Entry entry)
    {
        foreach (var resourceId in entry.ResourceDependencies)
        {
            if (!_resourceDependents.TryGetValue(resourceId, out var dependents))
            {
                continue;
            }

            dependents.Remove(entry);
            if (dependents.Count == 0)
            {
                _resourceDependents.Remove(resourceId);
            }
        }

        entry.ResourceDependencies.Clear();
    }

    private bool TryGetEntry(IAutoBuyCandidate candidate, out Entry entry)
    {
        entry = null!;
        AutoBuyCandidateSnapshot snapshot;
        try
        {
            snapshot = candidate.Snapshot();
        }
        catch
        {
            return false;
        }

        return _entries.TryGetValue(snapshot.Uuid, out entry!) && ReferenceEquals(entry.Candidate, candidate);
    }

    private void RebuildPublicActiveList()
    {
        _active.Clear();
        foreach (var entry in _activeEntries)
        {
            _active.Add(entry.Candidate);
        }
    }

    private static bool IsIncluded(AutoBuyCandidateKind kind, AutoBuyEvaluationRequest request)
    {
        return kind == AutoBuyCandidateKind.Structure
            ? request.IncludeStructures
            : request.IncludeUpgrades;
    }

    private static object GetNativeIdentity(IAutoBuyCandidate candidate)
    {
        return candidate is IAutoBuyNativeIdentity identity ? identity.NativeIdentity : candidate;
    }

    private sealed class Entry
    {
        public Entry(IAutoBuyCandidate candidate, AutoBuyCandidateSnapshot snapshot, long epoch)
        {
            Candidate = candidate;
            Definition = new Definition(snapshot.Uuid, snapshot.Kind, snapshot.ReflectedType);
            State = AutoBuyCandidateLifecycleState.Registered;
            Epoch = epoch;
        }

        public IAutoBuyCandidate Candidate { get; private set; }

        public Definition Definition { get; }

        public AutoBuyCandidateLifecycleState State { get; set; }

        public long Epoch { get; set; }

        public string InvalidReason { get; set; } = string.Empty;

        public bool IsEligibleForHotSet { get; set; }

        public bool LifecycleQueued { get; set; }

        public int? LastCurrentLevel { get; set; }

        public int? LastQueuedLevels { get; set; }

        public AutoBuyDirtyReason DirtyReasons { get; set; }

        public bool NeedsEpochValidation { get; set; }

        public HashSet<string> ResourceDependencies { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Replace(IAutoBuyCandidate candidate, long epoch)
        {
            Candidate = candidate;
            BeginEpoch(epoch);
        }

        public void BeginEpoch(long epoch)
        {
            State = AutoBuyCandidateLifecycleState.Registered;
            Epoch = epoch;
            InvalidReason = string.Empty;
            IsEligibleForHotSet = false;
            LastCurrentLevel = null;
            LastQueuedLevels = null;
            DirtyReasons = AutoBuyDirtyReason.None;
            LifecycleQueued = false;
            NeedsEpochValidation = false;
            ResourceDependencies.Clear();
        }

        public void Restore(long epoch)
        {
            State = AutoBuyCandidateLifecycleState.Registered;
            Epoch = epoch;
            InvalidReason = string.Empty;
            IsEligibleForHotSet = false;
        }
    }

    private sealed class Definition
    {
        public Definition(string uuid, AutoBuyCandidateKind kind, string reflectedType)
        {
            Uuid = uuid;
            Kind = kind;
            ReflectedType = reflectedType;
        }

        public string Uuid { get; }

        public AutoBuyCandidateKind Kind { get; }

        public string ReflectedType { get; }
    }

    private sealed class EntryComparer : IComparer<Entry>
    {
        public static readonly EntryComparer Instance = new EntryComparer();

        public int Compare(Entry? left, Entry? right)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(left?.Definition.Uuid, right?.Definition.Uuid);
        }
    }
}

internal static class AutoBuyCandidateExtensions
{
    public static IAutoBuyDirtyCandidate? AsDirtyCandidate(this IAutoBuyCandidate candidate)
    {
        return candidate as IAutoBuyDirtyCandidate;
    }
}
