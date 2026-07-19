using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptController : IDisposable
{
    private enum MutationKind { Add, RemoveOwned, RotateOut }

    private sealed class TrainingSession
    {
        public TrainingSession(ConceptProgress target)
        {
            Target = target;
        }

        public ConceptProgress Target { get; }
        public double? StartedAtSeconds { get; set; }
    }

    private readonly struct PendingMutation
    {
        public PendingMutation(
            MutationKind kind,
            string uuid,
            int targetOrDelta,
            string replacementUuid = "",
            string replacementName = "")
        {
            Kind = kind;
            Uuid = uuid;
            TargetOrDelta = targetOrDelta;
            ReplacementUuid = replacementUuid;
            ReplacementName = replacementName;
        }
        public MutationKind Kind { get; }
        public string Uuid { get; }
        public int TargetOrDelta { get; }
        public string ReplacementUuid { get; }
        public string ReplacementName { get; }
    }

    private readonly AutomataConfig _config;
    private readonly ReflectionConceptRuntime _runtime;
    private readonly ManualLogSource _log;
    private readonly SuitePerformanceCoordinator _coordinator;
    private readonly Func<long> _readFrameIdentity;
    private readonly AutomataFeatureStatusReporter? _featureStatus;
    private readonly Func<bool> _ownsActionFamily;
    private readonly SuiteWorkRegistration _readWork;
    private readonly SuiteWorkRegistration _mutationWork;
    private readonly ConceptOwnershipLedger _ownership = new();
    private readonly Dictionary<string, TrainingSession> _trainingSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastTimedAssignment = new(StringComparer.Ordinal);
    private readonly List<string> _completedTrainingSessions = new();
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resourceSafeReplacements = new(StringComparer.Ordinal);
    private readonly DecisionLogGate _failureLogGate = new(TimeSpan.FromSeconds(30));
    private readonly DecisionLogGate _decisionLogGate = new(TimeSpan.FromSeconds(30));
    private PendingMutation? _pending;
    private float _secondsUntilEvaluation;
    private float _secondsUntilWatchdog;
    private double _elapsedSeconds;
    private bool _baselineCaptured;
    private bool _timedSessionsInitialized;
    private AutoConceptSlotManagementMode? _configuredSlotManagementMode;
    private int? _configuredTrainingPeriodSeconds;
    private bool _wasActive;
    private string _configuredAllowed = string.Empty;
    private string _configuredBlocked = string.Empty;
    private IReadOnlyList<NativeConceptCandidate> _cachedCandidates = Array.Empty<NativeConceptCandidate>();
    private string? _loggedBlockedReason;
    private string? _preferredReplacementUuid;
    private double _preferredReplacementExpiresAtSeconds;
    private long _timedAssignmentSequence;
    private bool _postconditionFaulted;
    private NativeMutationCallOutcome _activeMutationOutcome;

    public AutoConceptController(
        AutomataConfig config,
        ReflectionConceptRuntime runtime,
        ManualLogSource log,
        SuitePerformanceCoordinator coordinator,
        Func<long> readFrameIdentity,
        AutomataFeatureStatusReporter? featureStatus = null,
        Func<bool>? ownsActionFamily = null)
    {
        _config = config;
        _runtime = runtime;
        _log = log;
        _coordinator = coordinator;
        _readFrameIdentity = readFrameIdentity;
        _featureStatus = featureStatus;
        _ownsActionFamily = ownsActionFamily ?? (() => true);
        var evaluateIdentity = SuitePerformanceWorkIdentities.AutoConceptEvaluate;
        _readWork = coordinator.Register(
            evaluateIdentity.Subsystem,
            evaluateIdentity.WorkName,
            evaluateIdentity.BudgetClass,
            evaluateIdentity.ExecutionKind);
        var mutationIdentity = SuitePerformanceWorkIdentities.AutoConceptMutation;
        _mutationWork = coordinator.Register(
            mutationIdentity.Subsystem,
            mutationIdentity.WorkName,
            mutationIdentity.BudgetClass,
            mutationIdentity.ExecutionKind);
        _secondsUntilEvaluation = 0.0f;
        _secondsUntilWatchdog = 0.0f;
    }

    public void Tick(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        var active = _config.CanStartAutoConceptActively;
        ObserveConfigurationStatus();
        if (active && !_ownsActionFamily())
        {
            _pending = null;
            SetEnabled(false);
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds the native concept-assignment action family.");
            return;
        }
        SetEnabled(active);
        if (!active)
        {
            if (_wasActive)
            {
                _pending = null;
                _ownership.Clear();
                _trainingSessions.Clear();
                _lastTimedAssignment.Clear();
                _timedAssignmentSequence = 0;
                _baselineCaptured = false;
                _timedSessionsInitialized = false;
                _cachedCandidates = Array.Empty<NativeConceptCandidate>();
                _preferredReplacementUuid = null;
                _preferredReplacementExpiresAtSeconds = 0.0;
            }
            _wasActive = false;
            return;
        }
        _wasActive = true;
        RefreshUuidFilters();
        RefreshSlotManagementMode();
        RefreshTrainingPeriod();
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
                _activeMutationOutcome = default;
                try
                {
                    mutationLease.Complete(ExecuteMutation());
                }
                catch
                {
                    mutationLease.Fail(_activeMutationOutcome.ToWorkCompletion());
                    throw;
                }
            }
        }
        SetPending(false, _pending is not null);
    }

    public void InvalidateLifecycle()
    {
        _runtime.InvalidateLifecycle();
        _pending = null;
        _ownership.Clear();
        _trainingSessions.Clear();
        _lastTimedAssignment.Clear();
        _timedAssignmentSequence = 0;
        _baselineCaptured = false;
        _timedSessionsInitialized = false;
        _cachedCandidates = Array.Empty<NativeConceptCandidate>();
        _preferredReplacementUuid = null;
        _preferredReplacementExpiresAtSeconds = 0.0;
        _loggedBlockedReason = null;
        _postconditionFaulted = false;
        _secondsUntilEvaluation = 0.0f;
        _secondsUntilWatchdog = 0.0f;
    }

    public void NotifyNativeChange() => _secondsUntilEvaluation = 0.0f;

    public bool TryResolveInvalidationEntityId(object nativeRecipe, out string entityId) =>
        _runtime.TryResolveInvalidationEntityId(nativeRecipe, out entityId);

    private void Evaluate()
    {
        _secondsUntilWatchdog = 1.0f;
        var wasReady = _runtime.IsReady;
        var candidates = _runtime.ReadCandidates(_allowed, _blocked, out var reason);
        if (!_runtime.IsReady || !string.IsNullOrWhiteSpace(reason))
        {
            if (_postconditionFaulted)
            {
                ObservePostconditionFault();
            }
            else
            {
                _featureStatus?.Observe(
                    true,
                    _runtime.BlockedReason is null
                        ? FeatureStatusState.NotReady
                        : FeatureStatusState.ContractUnavailable,
                    _runtime.BlockedReason is null
                        ? FeatureStatusReasonCode.RegistryNotReady
                        : FeatureStatusReasonCode.ContractUnavailable,
                    _runtime.BlockedReason is null
                        ? "The native Concept registries are not ready."
                        : "The native Concept contract is unavailable.");
            }
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
        if (candidates.Count == 0)
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.Locked,
                FeatureStatusReasonCode.ProgressionLocked,
                "No compatible discovered Concepts are available.");
        }
        else
        {
            _featureStatus?.ObserveOperational();
        }
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
                if (!candidate.IsSettled) continue;
                var changed = _ownership.RebaselineIfUnexpected(candidate.Uuid, candidate.Quantity);
                if (changed && candidate.Quantity > 0 &&
                    _config.AutoConceptSlotManagement.Value == AutoConceptSlotManagementMode.TimedCycle)
                    BeginTrainingSession(candidate, candidates);
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
        InitializeTimedCycleSessions(candidates);
        UpdateTrainingSessions(byId);

        if (TryPlanPreferredReplacement(byId)) return;

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

        // If a strictly lower-mastery concept is blocked only because all
        // compatible slots are occupied, retire one compatible assignment
        // according to the explicit slot-management policy and then replan.
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

        LogIdleDecision(ranked.Count, candidates);
        _secondsUntilEvaluation = Math.Min(
            SecondsUntilNextTrainingDeadline(),
            Math.Clamp(
                _config.AutoConceptFallbackEvaluationIntervalSeconds.Value,
                10,
                1800));
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
            _pending = new PendingMutation(MutationKind.RemoveOwned, candidate.Uuid, ownership.AutomatedDelta);
            return true;
        }
        return false;
    }

    private bool TryPlanMasteryRebalance(
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, NativeConceptCandidate> byId)
    {
        _resourceSafeReplacements.Clear();
        for (var index = 0; index < ranked.Count; index++)
        {
            var inactive = byId[ranked[index].Uuid];
            if (!inactive.IsSettled || inactive.Quantity != 0 ||
                string.IsNullOrWhiteSpace(inactive.SlotTypeUuid)) continue;
            if (_runtime.TryFindSafeTarget(
                    inactive,
                    1,
                    _config.AutoConceptRateReservePercent.Value,
                    _config.AutoConceptMinimumResourcePercent.Value,
                    out _,
                    out _))
                _resourceSafeReplacements.Add(inactive.Uuid);
        }

        for (var inactiveIndex = 0; inactiveIndex < ranked.Count; inactiveIndex++)
        {
            var desiredProgress = ranked[inactiveIndex];
            var desiredInactive = byId[desiredProgress.Uuid];
            if (!desiredInactive.IsSettled || desiredInactive.Quantity != 0 ||
                string.IsNullOrWhiteSpace(desiredInactive.SlotTypeUuid) ||
                !_resourceSafeReplacements.Contains(desiredInactive.Uuid)) continue;
            if (_config.AutoConceptSlotManagement.Value == AutoConceptSlotManagementMode.TimedCycle &&
                !IsNextTimedReplacement(desiredInactive, ranked, byId, _resourceSafeReplacements)) continue;

            for (var activeIndex = ranked.Count - 1; activeIndex >= 0; activeIndex--)
            {
                var activeProgress = ranked[activeIndex];
                if (AutoConceptBalancer.RequiresLowerMastery(_config.AutoConceptSlotManagement.Value) &&
                    !AutoConceptBalancer.HasStrictlyLowerMastery(desiredProgress, activeProgress)) continue;
                var candidate = byId[activeProgress.Uuid];
                if (!candidate.IsSettled || candidate.Quantity <= 0 ||
                    !string.Equals(candidate.SlotTypeUuid, desiredInactive.SlotTypeUuid, StringComparison.Ordinal)) continue;
                if (_trainingSessions.ContainsKey(candidate.Uuid)) continue;

                if (AutoConceptBalancer.UsesFullRotation(_config.AutoConceptSlotManagement.Value))
                {
                    _pending = new PendingMutation(
                        MutationKind.RotateOut,
                        candidate.Uuid,
                        candidate.Quantity,
                        desiredInactive.Uuid,
                        desiredInactive.DisplayName);
                    return true;
                }

                if (!_ownership.TryGet(candidate.Uuid, out var ownership) ||
                    ownership.ManualBaseline != 0 || ownership.AutomatedDelta <= 0) continue;
                _pending = new PendingMutation(
                    MutationKind.RemoveOwned,
                    candidate.Uuid,
                    ownership.AutomatedDelta,
                    desiredInactive.Uuid,
                    desiredInactive.DisplayName);
                return true;
            }
        }
        return false;
    }

    private SuiteWorkCompletion ExecuteMutation()
    {
        var pending = _pending;
        _pending = null;
        if (pending is null || !_config.CanStartAutoConceptActively || !_ownsActionFamily())
        {
            if (pending is not null && !_ownsActionFamily()) ObserveOwnershipConflict();
            return NoMutationCompletion();
        }
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
            return NoMutationCompletion();
        }

        if (!IsReplacementStillValid(pending.Value, candidate, candidates))
        {
            LogFailure($"Auto Concept rotation target, slot, or resource safety changed before removing {candidate.DisplayName}; replanning.");
            _secondsUntilEvaluation = 0.0f;
            return NoMutationCompletion();
        }

        if (pending.Value.Kind == MutationKind.RotateOut)
        {
            if (!AutoConceptBalancer.UsesFullRotation(_config.AutoConceptSlotManagement.Value) ||
                candidate.Quantity != pending.Value.TargetOrDelta)
            {
                _secondsUntilEvaluation = 0.0f;
                return NoMutationCompletion();
            }
            if (!_ownsActionFamily())
            {
                ObserveOwnershipConflict();
                _secondsUntilEvaluation = 0.0f;
                return NoMutationCompletion();
            }
            bool removedForRotation;
            try
            {
                removedForRotation =
                    _runtime.TryRemoveForRotation(candidate, pending.Value.TargetOrDelta, out reason);
            }
            finally
            {
                _activeMutationOutcome = _runtime.LastNativeMutationOutcome;
            }
            if (!removedForRotation)
            {
                _ownership.RebaselineIfUnexpected(candidate.Uuid, candidate.Quantity);
                LogFailure($"Auto Concept rotation rejected for {candidate.DisplayName}: {reason}");
                ObserveMutationRejection();
                _secondsUntilEvaluation = 0.0f;
                return _activeMutationOutcome.ToWorkCompletion();
            }
            _ownership.ObserveBaseline(candidate.Uuid, 0);
            _preferredReplacementUuid = pending.Value.ReplacementUuid;
            _preferredReplacementExpiresAtSeconds = _elapsedSeconds + 5.0;
            var purpose = _config.AutoConceptSlotManagement.Value == AutoConceptSlotManagementMode.TimedCycle
                ? $"continue the timed cycle with {pending.Value.ReplacementName}"
                : $"train lower-mastery {pending.Value.ReplacementName}";
            LogOperation(
                $"Auto Concept rotated out {candidate.DisplayName} ({pending.Value.TargetOrDelta} instance(s)) to {purpose}.");
            _featureStatus?.ObserveOperational();
            _secondsUntilEvaluation = 0.25f;
            return _activeMutationOutcome.ToWorkCompletion();
        }

        if (pending.Value.Kind == MutationKind.RemoveOwned)
        {
            if (!_ownership.TryGet(candidate.Uuid, out var ownership) ||
                ownership.AutomatedDelta < pending.Value.TargetOrDelta ||
                candidate.Quantity != ownership.ExpectedQuantity)
            {
                _ownership.RebaselineIfUnexpected(candidate.Uuid, candidate.Quantity);
                _secondsUntilEvaluation = 0.0f;
                return NoMutationCompletion();
            }
            if (!_ownsActionFamily())
            {
                ObserveOwnershipConflict();
                _secondsUntilEvaluation = 0.0f;
                return NoMutationCompletion();
            }
            bool removedOwned;
            try
            {
                removedOwned =
                    _runtime.TryRemoveOwned(candidate, pending.Value.TargetOrDelta, out reason);
            }
            finally
            {
                _activeMutationOutcome = _runtime.LastNativeMutationOutcome;
            }
            if (!removedOwned)
            {
                _ownership.RebaselineIfUnexpected(candidate.Uuid, candidate.Quantity);
                LogFailure($"Auto Concept removal rejected for {candidate.DisplayName}: {reason}");
                ObserveMutationRejection();
                _secondsUntilEvaluation = 0.0f;
                return _activeMutationOutcome.ToWorkCompletion();
            }
            _ownership.RecordAutomatedDelta(
                candidate.Uuid,
                candidate.Quantity - pending.Value.TargetOrDelta,
                -pending.Value.TargetOrDelta);
            LogOperation($"Auto Concept removed {pending.Value.TargetOrDelta} owned {candidate.DisplayName} instance(s).");
            _featureStatus?.ObserveOperational();
            _secondsUntilEvaluation = string.IsNullOrWhiteSpace(pending.Value.ReplacementName) ? 0.0f : 0.25f;
            return _activeMutationOutcome.ToWorkCompletion();
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
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.TemporarySafetyBlock,
                "Concept resource safety changed before mutation.");
            _secondsUntilEvaluation = 5.0f;
            return NoMutationCompletion();
        }
        var delta = safeTarget - candidate.Quantity;
        if (delta <= 0)
        {
            _secondsUntilEvaluation = 0.0f;
            return NoMutationCompletion();
        }
        if (!_ownsActionFamily())
        {
            ObserveOwnershipConflict();
            _secondsUntilEvaluation = 0.0f;
            return NoMutationCompletion();
        }
        bool added;
        try
        {
            added = _runtime.TryAdd(candidate, delta, out reason);
        }
        finally
        {
            _activeMutationOutcome = _runtime.LastNativeMutationOutcome;
        }
        if (!added)
        {
            LogFailure($"Auto Concept native mutation rejected {candidate.DisplayName}: {reason}");
            ObserveMutationRejection();
            _secondsUntilEvaluation = 5.0f;
            return _activeMutationOutcome.ToWorkCompletion();
        }
        if (candidate.Quantity == 0) BeginTrainingSession(candidate, candidates);
        _ownership.RecordAutomatedDelta(candidate.Uuid, candidate.Quantity + delta, delta);
        if (string.Equals(_preferredReplacementUuid, candidate.Uuid, StringComparison.Ordinal))
        {
            _preferredReplacementUuid = null;
            _preferredReplacementExpiresAtSeconds = 0.0;
        }
        LogOperation($"Auto Concept added {delta} {candidate.DisplayName} instance(s), target {safeTarget}.");
        _featureStatus?.ObserveOperational();
        _secondsUntilEvaluation = 0.0f;
        return _activeMutationOutcome.ToWorkCompletion();
    }

    private static SuiteWorkCompletion NoMutationCompletion() => new(1);

    private void ObserveOwnershipConflict() =>
        _featureStatus?.Observe(
            true,
            FeatureStatusState.TemporarilyBlocked,
            FeatureStatusReasonCode.ActionFamilyConflict,
            "Concept-assignment ownership changed before mutation.");

    private void BeginTrainingSession(
        NativeConceptCandidate candidate,
        IReadOnlyList<NativeConceptCandidate> candidates)
    {
        var current = ToProgress(candidate);
        var target = current;
        for (var index = 0; index < candidates.Count; index++)
        {
            var progress = ToProgress(candidates[index]);
            if (!progress.Eligible || AutoConceptBalancer.HasStrictlyLowerMastery(progress, target)) continue;
            target = progress;
        }
        var timedCycle = _config.AutoConceptSlotManagement.Value == AutoConceptSlotManagementMode.TimedCycle;
        if (!timedCycle && !AutoConceptBalancer.HasStrictlyLowerMastery(current, target)) return;
        _trainingSessions[candidate.Uuid] = new TrainingSession(target);
        if (timedCycle) _lastTimedAssignment[candidate.Uuid] = ++_timedAssignmentSequence;
        LogOperation(timedCycle
            ? $"Auto Concept reserved {candidate.DisplayName} for {_config.AutoConceptTrainingPeriodSeconds.Value} settled active seconds."
            : $"Auto Concept reserved {candidate.DisplayName} until mastery {FormatMastery(target)} or {_config.AutoConceptTrainingPeriodSeconds.Value} settled active seconds.");
    }

    private void InitializeTimedCycleSessions(IReadOnlyList<NativeConceptCandidate> candidates)
    {
        if (_config.AutoConceptSlotManagement.Value != AutoConceptSlotManagementMode.TimedCycle)
        {
            _timedSessionsInitialized = false;
            return;
        }
        if (_timedSessionsInitialized) return;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.IsSettled && candidate.Quantity > 0 && !_trainingSessions.ContainsKey(candidate.Uuid))
                BeginTrainingSession(candidate, candidates);
        }
        _timedSessionsInitialized = true;
    }

    private void UpdateTrainingSessions(
        IReadOnlyDictionary<string, NativeConceptCandidate> candidates)
    {
        if (_trainingSessions.Count == 0) return;
        _completedTrainingSessions.Clear();
        foreach (var pair in _trainingSessions)
        {
            if (!candidates.TryGetValue(pair.Key, out var candidate))
            {
                _completedTrainingSessions.Add(pair.Key);
                continue;
            }
            if (!candidate.IsSettled) continue;
            if (candidate.Quantity <= 0)
            {
                _completedTrainingSessions.Add(pair.Key);
                continue;
            }

            var session = pair.Value;
            if (session.StartedAtSeconds is null)
            {
                session.StartedAtSeconds = _elapsedSeconds;
                LogOperation(_config.AutoConceptSlotManagement.Value == AutoConceptSlotManagementMode.TimedCycle
                    ? $"Auto Concept timed cycle started for {candidate.DisplayName}."
                    : $"Auto Concept training started for {candidate.DisplayName}; catch-up target {FormatMastery(session.Target)}.");
            }

            var current = ToProgress(candidate);
            var trainingPeriod = Math.Clamp(
                _config.AutoConceptTrainingPeriodSeconds.Value,
                10,
                3600);
            if (AutoConceptBalancer.HasTrainingSessionCompleted(
                    _config.AutoConceptSlotManagement.Value,
                    current,
                    session.Target,
                    session.StartedAtSeconds.Value,
                    _elapsedSeconds,
                    trainingPeriod))
            {
                var elapsed = AutoConceptBalancer.HasTrainingPeriodElapsed(
                    session.StartedAtSeconds.Value,
                    _elapsedSeconds,
                    trainingPeriod);
                LogOperation(elapsed
                    ? $"Auto Concept training completed for {candidate.DisplayName}: {trainingPeriod}-second period elapsed at {FormatMastery(current)}."
                    : $"Auto Concept training completed for {candidate.DisplayName}: reached catch-up target {FormatMastery(session.Target)}.");
                _completedTrainingSessions.Add(pair.Key);
            }
        }
        for (var index = 0; index < _completedTrainingSessions.Count; index++)
            _trainingSessions.Remove(_completedTrainingSessions[index]);
    }

    private static ConceptProgress ToProgress(NativeConceptCandidate candidate) =>
        new(
            candidate.Uuid,
            candidate.MasteryLevel,
            candidate.MasteryProgress,
            candidate.MaximumQuantity > 0);

    private static string FormatMastery(ConceptProgress progress) =>
        $"level {progress.MasteryLevel} ({progress.MasteryProgress:P0})";

    private bool IsReplacementStillValid(
        PendingMutation pending,
        NativeConceptCandidate active,
        IReadOnlyList<NativeConceptCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(pending.ReplacementUuid)) return true;
        for (var index = 0; index < candidates.Count; index++)
        {
            var replacement = candidates[index];
            if (!string.Equals(replacement.Uuid, pending.ReplacementUuid, StringComparison.Ordinal)) continue;
            if (!replacement.IsSettled || replacement.Quantity != 0 ||
                !string.Equals(replacement.SlotTypeUuid, active.SlotTypeUuid, StringComparison.Ordinal)) return false;
            if (!_runtime.TryFindSafeTarget(
                    replacement,
                    1,
                    _config.AutoConceptRateReservePercent.Value,
                    _config.AutoConceptMinimumResourcePercent.Value,
                    out _,
                    out _)) return false;
            return !AutoConceptBalancer.RequiresLowerMastery(_config.AutoConceptSlotManagement.Value) ||
                AutoConceptBalancer.HasStrictlyLowerMastery(
                new ConceptProgress(
                    replacement.Uuid,
                    replacement.MasteryLevel,
                    replacement.MasteryProgress,
                    replacement.MaximumQuantity > 0),
                new ConceptProgress(
                    active.Uuid,
                    active.MasteryLevel,
                    active.MasteryProgress,
                    active.MaximumQuantity > 0));
        }
        return false;
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

    private bool TryPlanPreferredReplacement(
        IReadOnlyDictionary<string, NativeConceptCandidate> byId)
    {
        if (string.IsNullOrWhiteSpace(_preferredReplacementUuid)) return false;
        if (_elapsedSeconds >= _preferredReplacementExpiresAtSeconds)
        {
            LogFailure("Auto Concept rotated replacement did not become valid within five seconds; returning to normal planning.");
            _preferredReplacementUuid = null;
            _preferredReplacementExpiresAtSeconds = 0.0;
            return false;
        }
        if (!byId.TryGetValue(_preferredReplacementUuid, out var candidate))
        {
            _preferredReplacementUuid = null;
            _preferredReplacementExpiresAtSeconds = 0.0;
            return false;
        }
        if (candidate.Quantity > 0)
        {
            _preferredReplacementUuid = null;
            _preferredReplacementExpiresAtSeconds = 0.0;
            return false;
        }
        if (!candidate.IsSettled || !_runtime.CanAdd(candidate))
        {
            _secondsUntilEvaluation = 0.25f;
            return true;
        }
        if (_runtime.TryFindSafeTarget(
                candidate,
                1,
                _config.AutoConceptRateReservePercent.Value,
                _config.AutoConceptMinimumResourcePercent.Value,
                out var safeTarget,
                out var reason))
        {
            _pending = new PendingMutation(MutationKind.Add, candidate.Uuid, safeTarget);
            return true;
        }
        LogFailure($"Auto Concept is waiting to add rotated replacement {candidate.DisplayName}: {reason}");
        _preferredReplacementUuid = null;
        _preferredReplacementExpiresAtSeconds = 0.0;
        return false;
    }

    private void RefreshSlotManagementMode()
    {
        var mode = _config.AutoConceptSlotManagement.Value;
        if (_configuredSlotManagementMode == mode) return;
        _configuredSlotManagementMode = mode;
        _trainingSessions.Clear();
        _lastTimedAssignment.Clear();
        _timedAssignmentSequence = 0;
        _timedSessionsInitialized = false;
        _secondsUntilEvaluation = 0.0f;
    }

    private bool IsNextTimedReplacement(
        NativeConceptCandidate candidate,
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, NativeConceptCandidate> byId,
        ISet<string> resourceSafeReplacements)
    {
        _lastTimedAssignment.TryGetValue(candidate.Uuid, out var candidateSequence);
        long? candidateLast = _lastTimedAssignment.ContainsKey(candidate.Uuid) ? candidateSequence : null;
        for (var index = 0; index < ranked.Count; index++)
        {
            var other = byId[ranked[index].Uuid];
            if (!other.IsSettled || other.Quantity != 0 ||
                !string.Equals(other.SlotTypeUuid, candidate.SlotTypeUuid, StringComparison.Ordinal)) continue;
            _lastTimedAssignment.TryGetValue(other.Uuid, out var otherSequence);
            long? otherLast = _lastTimedAssignment.ContainsKey(other.Uuid) ? otherSequence : null;
            if (AutoConceptBalancer.ResourceSafeTimedCandidatePrecedes(
                    resourceSafeReplacements.Contains(other.Uuid),
                    otherLast,
                    other.Uuid,
                    candidateLast,
                    candidate.Uuid)) return false;
        }
        return true;
    }

    private void RefreshTrainingPeriod()
    {
        var period = _config.AutoConceptTrainingPeriodSeconds.Value;
        if (_configuredTrainingPeriodSeconds == period) return;
        _configuredTrainingPeriodSeconds = period;
        _secondsUntilEvaluation = 0.0f;
    }

    private float SecondsUntilNextTrainingDeadline()
    {
        var remaining = double.PositiveInfinity;
        var period = Math.Clamp(_config.AutoConceptTrainingPeriodSeconds.Value, 10, 3600);
        foreach (var session in _trainingSessions.Values)
        {
            if (session.StartedAtSeconds is null) continue;
            remaining = Math.Min(remaining, Math.Max(0.0, session.StartedAtSeconds.Value + period - _elapsedSeconds));
        }
        return double.IsPositiveInfinity(remaining) ? float.PositiveInfinity : (float)remaining;
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

    private void ObserveConfigurationStatus()
    {
        if (_config.AutoConceptMode.Value != AutoConceptOperationMode.Active)
        {
            _featureStatus?.Observe(
                false,
                FeatureStatusState.ConfigurationDisabled,
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Auto Concept is disabled by configuration.");
            return;
        }
        if (!_config.CanStartAutoConceptActively)
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        }
    }

    private void ObserveMutationRejection()
    {
        if (_runtime.BlockedReason is not null)
        {
            _postconditionFaulted = true;
            ObservePostconditionFault();
            return;
        }
        _featureStatus?.Observe(
            true,
            FeatureStatusState.TemporarilyBlocked,
            FeatureStatusReasonCode.TemporarySafetyBlock,
            "Concept state changed before mutation; Automata will replan.");
    }

    private void ObservePostconditionFault() =>
        _featureStatus?.Observe(
            true,
            FeatureStatusState.Faulted,
            FeatureStatusReasonCode.PostconditionFailed,
            "A native Concept mutation could not be verified and is blocked until lifecycle recovery.");

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

    private void LogIdleDecision(int eligibleCount, IReadOnlyList<NativeConceptCandidate> candidates)
    {
        if (!_config.IsOperationalLoggingEnabled) return;
        var activeCount = 0;
        for (var index = 0; index < candidates.Count; index++)
            if (candidates[index].Quantity > 0) activeCount++;
        var rotation = _config.AutoConceptSlotManagement.Value == AutoConceptSlotManagementMode.TimedCycle
            ? "no compatible timed rotation"
            : "no compatible strictly lower-mastery rotation";
        var message =
            $"Auto Concept made no change: {rotation} or resource-safe quantity increase was available. SlotManagement={_config.AutoConceptSlotManagement.Value}, Training={_trainingSessions.Count}, Eligible={eligibleCount}, Active={activeCount}.";
        if (_decisionLogGate.ShouldLog(message, TimeSpan.FromSeconds(_elapsedSeconds)))
            _log.LogAutomataInfo(message);
    }

    public void Dispose()
    {
        SetEnabled(false);
        _runtime.Dispose();
        _pending = null;
        _ownership.Clear();
        _trainingSessions.Clear();
        _lastTimedAssignment.Clear();
        _timedAssignmentSequence = 0;
        _timedSessionsInitialized = false;
        _configuredSlotManagementMode = null;
        _configuredTrainingPeriodSeconds = null;
        _cachedCandidates = Array.Empty<NativeConceptCandidate>();
        _resourceSafeReplacements.Clear();
        _preferredReplacementUuid = null;
        _preferredReplacementExpiresAtSeconds = 0.0;
    }

    internal void CancelPreparedWork()
    {
        _pending = null;
        _ownership.Clear();
        _trainingSessions.Clear();
        _lastTimedAssignment.Clear();
        _timedAssignmentSequence = 0;
        _baselineCaptured = false;
        _timedSessionsInitialized = false;
        _cachedCandidates = Array.Empty<NativeConceptCandidate>();
        _resourceSafeReplacements.Clear();
        _preferredReplacementUuid = null;
        _preferredReplacementExpiresAtSeconds = 0.0;
        _wasActive = false;
        _secondsUntilEvaluation = 0.0f;
        SetEnabled(false);
    }
}
