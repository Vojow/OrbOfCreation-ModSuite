using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptController : IDisposable
{
    private enum MutationKind { Add, Remove }

    private readonly struct PendingMutation
    {
        public PendingMutation(MutationKind kind, string uuid, int targetOrDelta)
        {
            Kind = kind;
            Uuid = uuid;
            TargetOrDelta = targetOrDelta;
        }
        public MutationKind Kind { get; }
        public string Uuid { get; }
        public int TargetOrDelta { get; }
    }

    private readonly AutomataConfig _config;
    private readonly ReflectionConceptRuntime _runtime;
    private readonly ManualLogSource _log;
    private readonly SuitePerformanceCoordinator _coordinator;
    private readonly Func<long> _readFrameIdentity;
    private readonly SuiteWorkRegistration _readWork;
    private readonly SuiteWorkRegistration _mutationWork;
    private readonly ConceptOwnershipLedger _ownership = new();
    private readonly Dictionary<string, double> _lastAutomatedChange = new(StringComparer.Ordinal);
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly DecisionLogGate _failureLogGate = new(TimeSpan.FromSeconds(30));
    private PendingMutation? _pending;
    private float _secondsUntilEvaluation;
    private float _secondsUntilWatchdog;
    private double _elapsedSeconds;
    private bool _baselineCaptured;
    private bool _wasActive;
    private string _configuredAllowed = string.Empty;
    private string _configuredBlocked = string.Empty;
    private IReadOnlyList<NativeConceptCandidate> _cachedCandidates = Array.Empty<NativeConceptCandidate>();
    private string? _loggedBlockedReason;

    public AutoConceptController(
        AutomataConfig config,
        ReflectionConceptRuntime runtime,
        ManualLogSource log,
        SuitePerformanceCoordinator coordinator,
        Func<long> readFrameIdentity)
    {
        _config = config;
        _runtime = runtime;
        _log = log;
        _coordinator = coordinator;
        _readFrameIdentity = readFrameIdentity;
        _readWork = coordinator.Register(
            "OrbAutomata.AutoConcept",
            "Reconcile and plan concept mastery",
            SuiteBudgetClass.SoftLimited,
            SuiteWorkExecutionKind.Cooperative);
        _mutationWork = coordinator.Register(
            "OrbAutomata.AutoConcept",
            "Change Active Concept quantity",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        _secondsUntilEvaluation = 0.0f;
        _secondsUntilWatchdog = 0.0f;
    }

    public void Tick(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        var active = _config.CanStartAutoConceptActively;
        SetEnabled(active);
        if (!active)
        {
            if (_wasActive)
            {
                _pending = null;
                _ownership.Clear();
                _lastAutomatedChange.Clear();
                _baselineCaptured = false;
                _cachedCandidates = Array.Empty<NativeConceptCandidate>();
            }
            _wasActive = false;
            return;
        }
        _wasActive = true;
        RefreshUuidFilters();
        _secondsUntilEvaluation -= elapsed;
        _secondsUntilWatchdog -= elapsed;
        var evaluationDue = _secondsUntilEvaluation <= 0.0f;
        var watchdogDue = _secondsUntilWatchdog <= 0.0f;
        var readDue = _pending is null && (evaluationDue || watchdogDue);
        SetPending(readDue, _pending is not null);
        if (readDue && TryAcquire(_readWork, out var readLease))
        {
            using (readLease)
            {
                if (evaluationDue) Evaluate();
                else RunWatchdog();
                readLease.Complete();
            }
        }
        SetPending(false, _pending is not null);
        if (_pending is not null && TryAcquire(_mutationWork, out var mutationLease))
        {
            using (mutationLease)
            {
                ExecuteMutation();
                mutationLease.Complete();
            }
        }
        SetPending(false, _pending is not null);
    }

    public void InvalidateLifecycle()
    {
        _runtime.InvalidateLifecycle();
        _pending = null;
        _ownership.Clear();
        _lastAutomatedChange.Clear();
        _baselineCaptured = false;
        _cachedCandidates = Array.Empty<NativeConceptCandidate>();
        _loggedBlockedReason = null;
        _secondsUntilEvaluation = 0.0f;
        _secondsUntilWatchdog = 0.0f;
    }

    public void NotifyNativeChange() => _secondsUntilEvaluation = 0.0f;

    private void Evaluate()
    {
        _secondsUntilWatchdog = 1.0f;
        var wasReady = _runtime.IsReady;
        var candidates = _runtime.ReadCandidates(_allowed, _blocked, out var reason);
        if (!_runtime.IsReady || !string.IsNullOrWhiteSpace(reason))
        {
            LogFailure(reason);
            _secondsUntilEvaluation = 10.0f;
            return;
        }
        if (!wasReady)
        {
            _log.LogAutomataInfo(
                $"Auto Concept catalog initialized. ScopedRecipes={_runtime.ScopedRecipeCount}, ActiveConcepts={_runtime.ActiveConceptCount}, EligibleCandidates={candidates.Count}.");
        }
        _cachedCandidates = candidates;
        if (!_baselineCaptured)
        {
            for (var index = 0; index < candidates.Count; index++)
                _ownership.ObserveBaseline(candidates[index].Uuid, candidates[index].Quantity);
            _baselineCaptured = true;
        }
        else
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (candidate.IsSettled) _ownership.RebaselineIfUnexpected(candidate.Uuid, candidate.Quantity);
            }
        }

        if (TryPlanUnsafeRollback(candidates)) return;

        var progress = new List<ConceptProgress>(candidates.Count);
        var byId = new Dictionary<string, NativeConceptCandidate>(candidates.Count, StringComparer.Ordinal);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            byId[candidate.Uuid] = candidate;
            progress.Add(new ConceptProgress(
                candidate.Uuid,
                candidate.MasteryLevel,
                candidate.MasteryProgress,
                candidate.MaximumQuantity > 0));
        }
        var ranked = AutoConceptBalancer.Rank(progress);

        // Breadth first: claim each currently compatible acquired slot with
        // one lowest-progress concept before deepening any assignment.
        for (var index = 0; index < ranked.Count; index++)
        {
            var candidate = byId[ranked[index].Uuid];
            if (!candidate.IsSettled || candidate.Quantity != 0 || !_runtime.CanAdd(candidate)) continue;
            if (_runtime.TryFindSafeTarget(
                    candidate,
                    1,
                    _config.AutoConceptRateReservePercent.Value,
                    _config.AutoConceptMinimumResourcePercent.Value,
                    out var safeTarget,
                    out reason))
            {
                _pending = new PendingMutation(MutationKind.Add, candidate.Uuid, safeTarget);
                return;
            }
        }

        // If a newly discovered lower-progress concept is blocked only after
        // all slots are occupied, retire the worst proven automated slot and
        // replan. Manual baselines are never candidates for removal.
        if (TryPlanMasteryRebalance(ranked, byId)) return;

        // Depth second: submit one native batched quantity change for the
        // lowest-progress active concept that has verified resource headroom.
        for (var index = 0; index < ranked.Count; index++)
        {
            var candidate = byId[ranked[index].Uuid];
            if (!candidate.IsSettled || candidate.Quantity <= 0 || candidate.Quantity >= candidate.MaximumQuantity) continue;
            var configuredCap = _config.AutoConceptQuantityCap.Value;
            var desired = configuredCap > 0
                ? Math.Min(candidate.MaximumQuantity, configuredCap)
                : candidate.MaximumQuantity;
            if (_runtime.TryFindSafeTarget(
                    candidate,
                    desired,
                    _config.AutoConceptRateReservePercent.Value,
                    _config.AutoConceptMinimumResourcePercent.Value,
                    out var safeTarget,
                    out reason) && safeTarget > candidate.Quantity)
            {
                _pending = new PendingMutation(MutationKind.Add, candidate.Uuid, safeTarget);
                return;
            }
        }

        _secondsUntilEvaluation = Math.Clamp(
            _config.AutoConceptRebalanceIntervalSeconds.Value,
            10,
            1800);
    }

    private void RunWatchdog()
    {
        _secondsUntilWatchdog = 1.0f;
        if (!_baselineCaptured || _cachedCandidates.Count == 0) return;
        TryPlanUnsafeRollback(_cachedCandidates);
    }

    private bool TryPlanUnsafeRollback(IReadOnlyList<NativeConceptCandidate> candidates)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (!_ownership.TryGet(candidate.Uuid, out var ownership) || ownership.AutomatedDelta <= 0) continue;
            if (_runtime.IsDrainSafe(candidate, _config.AutoConceptMinimumDrainRatio.Value)) continue;
            _pending = new PendingMutation(MutationKind.Remove, candidate.Uuid, ownership.AutomatedDelta);
            return true;
        }
        return false;
    }

    private bool TryPlanMasteryRebalance(
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, NativeConceptCandidate> byId)
    {
        var firstInactiveRank = -1;
        for (var index = 0; index < ranked.Count; index++)
        {
            if (byId[ranked[index].Uuid].Quantity == 0) { firstInactiveRank = index; break; }
        }
        if (firstInactiveRank < 0) return false;
        var desiredInactive = byId[ranked[firstInactiveRank].Uuid];

        for (var index = ranked.Count - 1; index > firstInactiveRank; index--)
        {
            var candidate = byId[ranked[index].Uuid];
            if (string.IsNullOrWhiteSpace(desiredInactive.SlotTypeUuid) ||
                !string.Equals(candidate.SlotTypeUuid, desiredInactive.SlotTypeUuid, StringComparison.Ordinal)) continue;
            if (!candidate.IsSettled || !_ownership.TryGet(candidate.Uuid, out var ownership) ||
                ownership.ManualBaseline != 0 || ownership.AutomatedDelta <= 0) continue;
            if (_lastAutomatedChange.TryGetValue(candidate.Uuid, out var changedAt) &&
                _elapsedSeconds - changedAt < 180.0) continue;
            _pending = new PendingMutation(MutationKind.Remove, candidate.Uuid, ownership.AutomatedDelta);
            return true;
        }
        return false;
    }

    private void ExecuteMutation()
    {
        var pending = _pending;
        _pending = null;
        if (pending is null || !_config.CanStartAutoConceptActively) return;
        var candidates = _runtime.ReadCandidates(_allowed, _blocked, out var reason);
        _cachedCandidates = candidates;
        NativeConceptCandidate? candidate = null;
        for (var index = 0; index < candidates.Count; index++)
            if (string.Equals(candidates[index].Uuid, pending.Value.Uuid, StringComparison.Ordinal))
            {
                candidate = candidates[index];
                break;
            }
        if (candidate is null || !candidate.IsSettled)
        {
            _secondsUntilEvaluation = 0.0f;
            return;
        }

        if (pending.Value.Kind == MutationKind.Remove)
        {
            if (!_ownership.TryGet(candidate.Uuid, out var ownership) ||
                ownership.AutomatedDelta < pending.Value.TargetOrDelta ||
                candidate.Quantity != ownership.ExpectedQuantity ||
                !_runtime.TryRemoveOwned(candidate, pending.Value.TargetOrDelta, out reason))
            {
                _ownership.RebaselineIfUnexpected(candidate.Uuid, candidate.Quantity);
                LogFailure($"Auto Concept removal rejected for {candidate.DisplayName}: {reason}");
                _secondsUntilEvaluation = 0.0f;
                return;
            }
            _ownership.RecordAutomatedDelta(
                candidate.Uuid,
                candidate.Quantity - pending.Value.TargetOrDelta,
                -pending.Value.TargetOrDelta);
            _lastAutomatedChange[candidate.Uuid] = _elapsedSeconds;
            LogOperation($"Auto Concept removed {pending.Value.TargetOrDelta} owned {candidate.DisplayName} instance(s).");
            _secondsUntilEvaluation = 0.0f;
            return;
        }

        if (!_runtime.TryFindSafeTarget(
                candidate,
                pending.Value.TargetOrDelta,
                _config.AutoConceptRateReservePercent.Value,
                _config.AutoConceptMinimumResourcePercent.Value,
                out var safeTarget,
                out reason))
        {
            LogFailure($"Auto Concept resource revalidation rejected {candidate.DisplayName}: {reason}");
            _secondsUntilEvaluation = 5.0f;
            return;
        }
        var delta = safeTarget - candidate.Quantity;
        if (delta <= 0 || !_runtime.TryAdd(candidate, delta, out reason))
        {
            LogFailure($"Auto Concept native mutation rejected {candidate.DisplayName}: {reason}");
            _secondsUntilEvaluation = 5.0f;
            return;
        }
        _ownership.RecordAutomatedDelta(candidate.Uuid, candidate.Quantity + delta, delta);
        _lastAutomatedChange[candidate.Uuid] = _elapsedSeconds;
        LogOperation($"Auto Concept added {delta} {candidate.DisplayName} instance(s), target {safeTarget}.");
        _secondsUntilEvaluation = 0.0f;
    }

    private void RefreshUuidFilters()
    {
        if (!string.Equals(_configuredAllowed, _config.AllowedAutoConceptUuids.Value, StringComparison.Ordinal))
        {
            _configuredAllowed = _config.AllowedAutoConceptUuids.Value;
            ParseUuids(_configuredAllowed, _allowed);
            _secondsUntilEvaluation = 0.0f;
        }
        if (!string.Equals(_configuredBlocked, _config.BlockedAutoConceptUuids.Value, StringComparison.Ordinal))
        {
            _configuredBlocked = _config.BlockedAutoConceptUuids.Value;
            ParseUuids(_configuredBlocked, _blocked);
            _secondsUntilEvaluation = 0.0f;
        }
    }

    private static void ParseUuids(string value, ISet<string> destination)
    {
        destination.Clear();
        foreach (var token in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var uuid = token.Trim();
            if (Guid.TryParse(uuid, out var parsed)) destination.Add(parsed.ToString());
        }
    }

    private bool TryAcquire(SuiteWorkRegistration registration, out SuiteWorkLease lease) =>
        _coordinator.RequestWork(registration, _readFrameIdentity(), out lease) == SuiteWorkAdmission.Granted;

    private void SetEnabled(bool enabled)
    {
        if (_readWork.IsEnabled != enabled) _readWork.SetEnabled(enabled);
        if (_mutationWork.IsEnabled != enabled) _mutationWork.SetEnabled(enabled);
        if (!enabled) SetPending(false, false);
    }

    private void SetPending(bool read, bool mutation)
    {
        _readWork.SetPending(read);
        _mutationWork.SetPending(mutation);
    }

    private void LogFailure(string reason)
    {
        if (_runtime.BlockedReason is not null)
        {
            if (string.Equals(_loggedBlockedReason, reason, StringComparison.Ordinal)) return;
            _loggedBlockedReason = reason;
            _log.LogAutomataWarning(reason);
            return;
        }
        if (_failureLogGate.ShouldLog(reason, TimeSpan.FromSeconds(_elapsedSeconds)))
            _log.LogAutomataWarning(reason);
    }

    private void LogOperation(string message)
    {
        if (_config.IsOperationalLoggingEnabled) _log.LogAutomataInfo(message);
    }

    public void Dispose()
    {
        SetEnabled(false);
        _runtime.Dispose();
        _pending = null;
        _ownership.Clear();
        _cachedCandidates = Array.Empty<NativeConceptCandidate>();
    }
}
