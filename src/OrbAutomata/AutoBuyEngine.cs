using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoBuyEngine : IDisposable
{
    private const double MaximumCpuBudgetMilliseconds = 1.0;
    private const float QueuePollIntervalSeconds = 0.1f;
    private readonly AutomataConfig _config;
    private readonly IAutoBuyCatalog _catalog;
    private readonly IAutoBuyIncrementalCatalog? _incrementalCatalog;
    private readonly ReservePolicy _reservePolicy;
    private readonly AutoBuyRejectionTelemetry _rejectionTelemetry = new AutoBuyRejectionTelemetry();
    private readonly ManualLogSource _log;
    private readonly Func<Stopwatch, double> _readElapsedMilliseconds;
    private readonly Func<Stopwatch, double> _readPurchaseElapsedMilliseconds;
    private readonly SuitePerformanceCoordinator? _coordinator;
    private readonly Func<long>? _readFrameIdentity;
    private readonly SuiteWorkRegistration? _readWork;
    private readonly SuiteWorkRegistration? _mutationWork;
    private readonly DecisionLogGate _decisionLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly DecisionLogGate _scanProgressLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly DecisionLogGate _upgradeQuarantineLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly DecisionLogGate _batchSummaryLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly DecisionLogGate _queueWaitLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly Dictionary<string, TimeSpan> _nativeFailureLogTimes =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _lifetime = Stopwatch.StartNew();
    private IReadOnlyList<IAutoBuyCandidate>? _pendingCandidates;
    private IReadOnlyList<IAutoBuyCandidate>? _pendingActiveCandidates;
    private readonly List<AutoBuyDecision> _pendingDecisions = new List<AutoBuyDecision>();
    private readonly Dictionary<string, AutoBuyDecision> _cachedDecisions =
        new Dictionary<string, AutoBuyDecision>(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<AutoBuyDecision> _rankedRecommendations =
        new SortedSet<AutoBuyDecision>(AutoBuyDecisionComparer.Instance);
    private readonly HashSet<string> _activeCandidateUuids =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _inactiveCandidateUuids = new List<string>();
    private readonly List<string> _quarantinedUpgradeUuids = new List<string>();
    private readonly List<AutoBuyDecision> _activeDecisionBuffer = new List<AutoBuyDecision>();
    private readonly List<AutoBuyDecision> _recommendationBuffer = new List<AutoBuyDecision>();
    private readonly HashSet<string> _allowedUuids =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedUuids =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private int _pendingIndex;
    private IReadOnlyList<AutoBuyDecision>? _pendingPurchaseRecommendations;
    private int _pendingPurchaseIndex;
    private int _pendingCandidateRepeats;
    private int _pendingCandidateRepeatLimit;
    private int _pendingBatchPurchased;
    private int _pendingBatchAttempted;
    private double _pendingBatchElapsedMilliseconds;
    private bool _pendingBatchCpuSliced;
    private bool _pendingBatchQueueWaitLogged;
    private bool _pendingWaitingForQueue;
    private float _secondsUntilEvaluation;
    private float _secondsUntilQueuePoll;
    private bool _queueWaitingLogged;
    private int _successfulPurchasesThisSession;
    private long _nativePurchaseAttempts;
    private long _nativePurchaseFailures;
    // Process-monotonic so a lifecycle rebuild cannot alias a completion
    // generation cached by a reused native candidate wrapper.
    private long _nativeCompletionGeneration;
    private bool _registryReconciliationPending;
    private AutoBuyPolicyFingerprint? _lastPolicy;
    private bool _nativeStateSignalPending;
    private bool _upgradeQuarantineObserved;
    private int _diagnosticBatchCount;
    private int _diagnosticBatchPurchased;
    private int _diagnosticBatchAttempted;
    private int _diagnosticCpuSlicedBatchCount;
    private int _diagnosticQueueWaitedBatchCount;
    private double _diagnosticBatchElapsedMilliseconds;

    internal int UpgradeQuarantinePurgePasses { get; private set; }

    internal int UpgradeQuarantineCacheEntriesInspected { get; private set; }

    internal int UpgradeQuarantineDecisionsRemoved { get; private set; }

    public AutoBuyEngine(
        AutomataConfig config,
        IAutoBuyCatalog catalog,
        ReservePolicy reservePolicy,
        ManualLogSource log,
        Func<Stopwatch, double>? readElapsedMilliseconds = null,
        Func<Stopwatch, double>? readPurchaseElapsedMilliseconds = null,
        SuitePerformanceCoordinator? coordinator = null,
        Func<long>? readFrameIdentity = null)
    {
        _config = config;
        _catalog = catalog;
        _incrementalCatalog = catalog as IAutoBuyIncrementalCatalog;
        _reservePolicy = reservePolicy;
        _log = log;
        _readElapsedMilliseconds = readElapsedMilliseconds ?? (stopwatch => stopwatch.Elapsed.TotalMilliseconds);
        _readPurchaseElapsedMilliseconds = readPurchaseElapsedMilliseconds ?? (stopwatch => stopwatch.Elapsed.TotalMilliseconds);
        _coordinator = coordinator;
        _readFrameIdentity = readFrameIdentity;
        if (coordinator is not null)
        {
            _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
            _readWork = coordinator.RegisterWeighted(
                "OrbAutomata.AutoBuy",
                "Evaluate candidates",
                SuiteBudgetClass.SoftLimited,
                SuiteWorkExecutionKind.Cooperative,
                schedulingWeight: 3);
            _mutationWork = coordinator.RegisterWeighted(
                "OrbAutomata.AutoBuy",
                "Submit one purchase",
                SuiteBudgetClass.HardLimited,
                SuiteWorkExecutionKind.NonPreemptibleNativeMutation,
                schedulingWeight: 3);
        }
        _secondsUntilEvaluation = ClampInterval(config.AutoBuyIntervalSeconds.Value);
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (_coordinator is null)
        {
            TickLegacy(unscaledDeltaTime);
            return;
        }

        TickCoordinated(unscaledDeltaTime);
    }

    private void TickLegacy(float unscaledDeltaTime)
    {
        if (_nativeStateSignalPending)
        {
            _nativeStateSignalPending = false;
            ResetAllPendingWork();
            _secondsUntilEvaluation = 0.0f;
        }

        var mode = _config.AutoBuyMode.Value;
        if (mode == AutoBuyOperationMode.Disabled)
        {
            ResetAllPendingWork();
            return;
        }

        if (!_config.CanStartAutoBuyActively)
        {
            ResetAllPendingWork();
            return;
        }

        if (_incrementalCatalog is not null)
        {
            var policy = AutoBuyPolicyFingerprint.Capture(_config);
            if (!_lastPolicy.HasValue || !_lastPolicy.Value.Equals(policy))
            {
                _lastPolicy = policy;
                _cachedDecisions.Clear();
                _rankedRecommendations.Clear();
                _rejectionTelemetry.ClearCurrentStates();
                PopulateUuidSet(_allowedUuids, _config.AllowedAutoBuyUuids.Value);
                PopulateUuidSet(_blockedUuids, _config.BlockedAutoBuyUuids.Value);
                _incrementalCatalog.InvalidatePolicy();
                ResetAllPendingWork();
                _secondsUntilEvaluation = 0.0f;
            }
        }

        SynchronizeUpgradeQuarantine(cancelPendingBatch: true);

        if (_pendingPurchaseRecommendations is not null)
        {
            if (mode != AutoBuyOperationMode.Active)
            {
                ResetPendingPurchaseBatch();
                return;
            }

            if (!_pendingWaitingForQueue)
            {
                ContinueRankedBatch();
            }
            else
            {
                _secondsUntilQueuePoll -= Math.Max(0.0f, unscaledDeltaTime);
                if (_secondsUntilQueuePoll <= 0.0f)
                {
                    _secondsUntilQueuePoll = QueuePollIntervalSeconds;
                    ContinueRankedBatch();
                }
            }
            return;
        }

        if (_pendingCandidates is not null)
        {
            // Once a scan starts, continue its CPU-budgeted slices on every
            // Unity frame. The idle interval applies only between scans.
            EvaluateBatch();
            return;
        }

        _secondsUntilEvaluation -= Math.Max(0.0f, unscaledDeltaTime);
        if (_secondsUntilEvaluation > 0.0f)
        {
            return;
        }

        _secondsUntilEvaluation = ClampInterval(_config.AutoBuyIntervalSeconds.Value);
        EvaluateBatch();
    }

    private void TickCoordinated(float unscaledDeltaTime)
    {
        if (_nativeStateSignalPending)
        {
            _nativeStateSignalPending = false;
            ResetAllPendingWork();
            _secondsUntilEvaluation = 0.0f;
        }

        var mode = _config.AutoBuyMode.Value;
        if (mode == AutoBuyOperationMode.Disabled || !_config.CanStartAutoBuyActively)
        {
            ResetAllPendingWork();
            SetCoordinatorEnabled(false);
            return;
        }

        SetCoordinatorEnabled(true);
        RefreshPolicyIfNeeded();
        SynchronizeUpgradeQuarantine(cancelPendingBatch: true);

        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        var readDue = false;
        var mutationDue = false;
        var queuePollDue = false;
        if (_pendingPurchaseRecommendations is not null)
        {
            if (mode != AutoBuyOperationMode.Active)
            {
                ResetPendingPurchaseBatch();
            }
            else if (_pendingWaitingForQueue)
            {
                _secondsUntilQueuePoll -= elapsed;
                queuePollDue = _secondsUntilQueuePoll <= 0.0f;
                readDue = queuePollDue;
            }
            else
            {
                mutationDue = true;
            }
        }
        else if (_pendingCandidates is not null)
        {
            readDue = true;
        }
        else
        {
            _secondsUntilEvaluation -= elapsed;
            readDue = _secondsUntilEvaluation <= 0.0f;
        }

        SetCoordinatorPending(readDue, mutationDue);
        var readCompleted = false;
        if (readDue && TryAcquire(_readWork, out var readLease))
        {
            using (readLease)
            {
                if (queuePollDue)
                {
                    PollPendingQueueRoom();
                }
                else
                {
                    if (_pendingCandidates is null)
                    {
                        _secondsUntilEvaluation = ClampInterval(_config.AutoBuyIntervalSeconds.Value);
                    }

                    EvaluateBatch();
                }
                readLease.Complete();
                readCompleted = true;
            }
        }

        // A read step may have completed ranking and created a purchase batch.
        mutationDue = _pendingPurchaseRecommendations is not null &&
                      !_pendingWaitingForQueue &&
                      mode == AutoBuyOperationMode.Active;
        SetCoordinatorPending(
            (!readCompleted && readDue) || _pendingCandidates is not null,
            mutationDue);
        var mutationCompleted = false;
        if (mutationDue && TryAcquire(_mutationWork, out var mutationLease))
        {
            using (mutationLease)
            {
                if (_pendingWaitingForQueue)
                {
                    _secondsUntilQueuePoll = QueuePollIntervalSeconds;
                }

                ContinueRankedBatch(singleStep: true);
                mutationLease.Complete();
                mutationCompleted = true;
            }
        }

        SetCoordinatorPending(
            (!readCompleted && readDue) || _pendingCandidates is not null,
            (!mutationCompleted && mutationDue) ||
            (_pendingPurchaseRecommendations is not null && !_pendingWaitingForQueue));
    }

    private void PollPendingQueueRoom()
    {
        _secondsUntilQueuePoll = QueuePollIntervalSeconds;
        if (TryCaptureQueueCapacity(out var queueCapacity) &&
            queueCapacity.UsableAutomationRoom > 0)
        {
            _pendingWaitingForQueue = false;
        }
    }

    private void RefreshPolicyIfNeeded()
    {
        if (_incrementalCatalog is null)
        {
            return;
        }

        var policy = AutoBuyPolicyFingerprint.Capture(_config);
        if (_lastPolicy.HasValue && _lastPolicy.Value.Equals(policy))
        {
            return;
        }

        _lastPolicy = policy;
        _cachedDecisions.Clear();
        _rankedRecommendations.Clear();
        _rejectionTelemetry.ClearCurrentStates();
        PopulateUuidSet(_allowedUuids, _config.AllowedAutoBuyUuids.Value);
        PopulateUuidSet(_blockedUuids, _config.BlockedAutoBuyUuids.Value);
        _incrementalCatalog.InvalidatePolicy();
        ResetAllPendingWork();
        _secondsUntilEvaluation = 0.0f;
    }

    private bool TryAcquire(SuiteWorkRegistration? registration, out SuiteWorkLease lease)
    {
        lease = default;
        return registration is not null &&
               _coordinator is not null &&
               _readFrameIdentity is not null &&
               _coordinator.RequestWork(registration, _readFrameIdentity(), out lease) == SuiteWorkAdmission.Granted;
    }

    private void SetCoordinatorEnabled(bool enabled)
    {
        if (_readWork is not null && _readWork.IsEnabled != enabled)
        {
            _readWork.SetEnabled(enabled);
        }

        if (_mutationWork is not null && _mutationWork.IsEnabled != enabled)
        {
            _mutationWork.SetEnabled(enabled);
        }

        if (!enabled)
        {
            SetCoordinatorPending(false, false);
        }
    }

    private void SetCoordinatorPending(bool readPending, bool mutationPending)
    {
        _readWork?.SetPending(readPending);
        _mutationWork?.SetPending(mutationPending);
    }

    public void Dispose()
    {
        ResetAllPendingWork();
        SetCoordinatorEnabled(false);
        _readWork?.Dispose();
        _mutationWork?.Dispose();
        _catalog.Dispose();
    }

    public void InvalidateLifecycle()
    {
        ResetAllPendingWork();
        _cachedDecisions.Clear();
        _rankedRecommendations.Clear();
        _rejectionTelemetry.ClearCurrentStates();
        _lastPolicy = null;
        _registryReconciliationPending = false;
        _nativeStateSignalPending = false;
        _incrementalCatalog?.InvalidateLifecycle();
        _secondsUntilEvaluation = 0.0f;
        SetCoordinatorPending(false, false);
    }

    public void NotifyStructureQueueChanged(object nativeIdentity)
    {
        _incrementalCatalog?.NotifyStructureQueueChanged(nativeIdentity);
        _nativeStateSignalPending = true;
    }

    public void NotifyUpgradeQueueChanged(object nativeIdentity)
    {
        _incrementalCatalog?.NotifyUpgradeQueueChanged(nativeIdentity);
        _nativeStateSignalPending = true;
    }

    public void NotifyNativeCompletion()
    {
        _nativeCompletionGeneration++;
        _incrementalCatalog?.NotifyNativeCompletion();
        if (_pendingPurchaseRecommendations is not null && _pendingWaitingForQueue)
        {
            // A native completion is exact evidence that queue occupancy
            // decreased. Resume the prepared fast lane immediately; its live
            // queue-room check still fails closed if another action consumed
            // the reopened slot before this engine tick.
            _pendingWaitingForQueue = false;
            _secondsUntilQueuePoll = 0.0f;
        }

        if (_pendingCandidates is null && _pendingPurchaseRecommendations is null)
        {
            // Completion effects are coalesced by the catalog. Do not restart
            // a CPU-sliced scan on every native completion: its eventual
            // recommendation is revalidated from authoritative live state
            // before mutation, and the following scan settles broad effects.
            _secondsUntilEvaluation = 0.0f;
        }
    }

    private void EvaluateBatch()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!BeginScanIfNeeded())
        {
            return;
        }

        var budget = EffectiveReadSliceBudget();
        while (_pendingCandidates is not null && _pendingIndex < _pendingCandidates.Count)
        {
            var candidate = _pendingCandidates[_pendingIndex];
            var decision = EvaluateCandidate(candidate, out var suppressResourceTracking);
            _rejectionTelemetry.Record(decision);
            _pendingDecisions.Add(decision);
            if (_incrementalCatalog is not null)
            {
                UpdateCachedDecision(decision);
                _incrementalCatalog.CompleteCandidateEvaluation(
                    candidate,
                    suppressResourceTracking,
                    IsPolicyExcluded(decision));
            }
            _pendingIndex++;

            if (_pendingIndex < _pendingCandidates.Count && _readElapsedMilliseconds(stopwatch) >= budget)
            {
                break;
            }
        }

        if (_pendingCandidates is not null && _pendingIndex < _pendingCandidates.Count)
        {
            LogScanProgress(_pendingIndex, _pendingCandidates.Count, _readElapsedMilliseconds(stopwatch));
            return;
        }

        CompleteScan(_readElapsedMilliseconds(stopwatch));
    }

    private bool BeginScanIfNeeded()
    {
        if (_pendingCandidates is not null)
        {
            return true;
        }

        if (!TryCaptureQueueCapacity(out _))
        {
            if (!_queueWaitingLogged)
            {
                _queueWaitingLogged = true;
                if (_config.IsOperationalLoggingEnabled)
                {
                    _log.LogAutomataInfo("Auto Buy is waiting for the gameplay action queue to initialize; it will retry without purchasing.");
                }
            }

            return false;
        }

        var limit = Math.Max(1, _config.AutoBuyMaxCandidatesPerScan.Value);
        if (_incrementalCatalog is not null)
        {
            var batch = _incrementalCatalog.BeginEvaluation(new AutoBuyEvaluationRequest(
                limit,
                _config.AutoBuyStructures.Value,
                _config.AutoBuyUpgrades.Value));
            _pendingCandidates = batch.DirtyCandidates;
            _pendingActiveCandidates = batch.ActiveCandidates;
            _registryReconciliationPending = batch.ReconciliationPending;
            RemoveInactiveCachedDecisions(batch.ActiveCandidates);
            if (batch.FirstExcludedCandidate is not null)
            {
                var excluded = AutoBuyDecision.Rejected(
                    batch.FirstExcludedCandidate.Snapshot(),
                    AutoBuyRejectionReason.CandidateScanLimit,
                    "candidate scan limit reached");
                _pendingDecisions.Add(excluded);
                _rejectionTelemetry.Record(excluded);
            }
        }
        else
        {
            var discovered = _catalog.Discover()
                .Where(IsIncludedKind)
                .Take(limit + 1)
                .ToArray();
            _pendingCandidates = discovered.Take(limit).ToArray();
            if (discovered.Length > limit)
            {
                var excluded = AutoBuyDecision.Rejected(
                    discovered[limit].Snapshot(),
                    AutoBuyRejectionReason.CandidateScanLimit,
                    "candidate scan limit reached");
                _pendingDecisions.Add(excluded);
                _rejectionTelemetry.Record(excluded);
            }
        }

        if (_incrementalCatalog is null)
        {
            PopulateUuidSet(_allowedUuids, _config.AllowedAutoBuyUuids.Value);
            PopulateUuidSet(_blockedUuids, _config.BlockedAutoBuyUuids.Value);
        }

        _pendingIndex = 0;

        return true;
    }

    private AutoBuyDecision EvaluateCandidate(IAutoBuyCandidate candidate)
    {
        return EvaluateCandidate(candidate, out _);
    }

    private AutoBuyDecision EvaluateCandidate(IAutoBuyCandidate candidate, out bool suppressResourceTracking)
    {
        suppressResourceTracking = false;
        var snapshot = candidate.Snapshot();
        if (snapshot.Kind == AutoBuyCandidateKind.Upgrade &&
            NativeMultiBuyScope.TryGetMutationQuarantine(out _))
        {
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.MutationQuarantined,
                "automated Upgrade mutations are quarantined for this process");
        }

        if (_allowedUuids.Count > 0 && !_allowedUuids.Contains(snapshot.Uuid))
        {
            suppressResourceTracking = true;
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.NotAllowed,
                "not included in the configured allowlist");
        }

        if (_blockedUuids.Contains(snapshot.Uuid))
        {
            suppressResourceTracking = true;
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.ConfigurationBlocked,
                "blocked by configuration");
        }

        var nativeCanPurchase = true;
        var nativeReason = string.Empty;
        if (snapshot.Kind == AutoBuyCandidateKind.Upgrade)
        {
            // The supported native UpgradeSO.CanPurchase contract includes
            // affordability as well as lifecycle, requirements, and queue
            // admission. Preserve that native call first, but decode its live
            // costs before classifying a false result so genuine resource waits
            // retain structured blockers and resource dependencies.
            nativeCanPurchase = candidate.CanPurchase(out nativeReason);
            if (!candidate.IsAvailable())
            {
                suppressResourceTracking = true;
                return AutoBuyDecision.Rejected(
                    snapshot,
                    AutoBuyRejectionReason.Unavailable,
                    "upgrade is not available");
            }
        }
        else if (!candidate.IsAvailable())
        {
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.Locked,
                "structure is locked");
        }

        // Structures remain resource-tracked once unlocked so affordability
        // changes wake them promptly. Upgrades reach this point only after the
        // native purchase contract says they can currently be bought.
        var costs = _incrementalCatalog is not null ? candidate.GetCosts() : null;
        var costsResolved = candidate is not IAutoBuyDirtyCandidate dirtyCandidate ||
                            dirtyCandidate.HasResolvedCosts;

        if (snapshot.Kind != AutoBuyCandidateKind.Upgrade &&
            !candidate.CanPurchase(out nativeReason))
        {
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.NativeNotPurchasable,
                nativeReason);
        }

        if (!costsResolved)
        {
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.CostSnapshotUnavailable,
                "native cost or resource snapshot unavailable");
        }

        costs ??= candidate.GetCosts();
        var reserve = _reservePolicy.Evaluate(costs);
        if (!reserve.Passed)
        {
            var rejectionReason = reserve.Failure switch
            {
                ReserveDecisionFailure.InvalidPolicy => AutoBuyRejectionReason.InvalidReservePolicy,
                ReserveDecisionFailure.InvalidResourceSnapshot => AutoBuyRejectionReason.InvalidResourceSnapshot,
                _ => AutoBuyRejectionReason.ReserveViolation,
            };
            return AutoBuyDecision.Rejected(
                snapshot,
                rejectionReason,
                reserve.Reason,
                reserve.ResourceBlockers);
        }

        var affordabilityMode = snapshot.Kind == AutoBuyCandidateKind.Upgrade
            ? _config.UpgradeAffordability.Value
            : _config.AutoBuyAffordability.Value;
        var maximumRatio = MaximumAllowedCostRatio(affordabilityMode);
        if (reserve.MaxCostToQuantityRatio > maximumRatio)
        {
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.AffordabilityThreshold,
                $"{snapshot.Kind} affordability mode {affordabilityMode} rejected " +
                $"maxCostRatio={reserve.MaxCostToQuantityRatio.ToString("0.###e+0", CultureInfo.InvariantCulture)}; " +
                $"limit={maximumRatio.ToString("0.###e+0", CultureInfo.InvariantCulture)}",
                BuildAffordabilityBlockers(costs, maximumRatio));
        }

        if (!nativeCanPurchase)
        {
            // The cost vector passed every Automata policy, so this false
            // result is normally a lifecycle/requirements/queue wait. Native
            // bandwidth affordability uses missing usage rather than quantity,
            // so retain those dependencies unless asset evidence proves the
            // cost is ordinary.
            suppressResourceTracking = !ContainsBandwidthCost(costs);
            return AutoBuyDecision.Rejected(
                snapshot,
                AutoBuyRejectionReason.NativeNotPurchasable,
                nativeReason);
        }

        var priorityRank = _config.PrioritizeCostAndQualityStructures.Value &&
                           snapshot.Kind == AutoBuyCandidateKind.Structure &&
                           candidate is IAutoBuyPriorityCandidate priorityCandidate &&
                           priorityCandidate.EconomicPriority != AutoBuyEconomicPriority.None
            ? 1
            : 0;
        return AutoBuyDecision.Recommended(
            snapshot,
            reserve.MaxCostToQuantityRatio,
            reserve.Summary,
            priorityRank);
    }

    private void CompleteScan(double elapsedMilliseconds)
    {
        IReadOnlyList<AutoBuyDecision> decisions;
        if (_incrementalCatalog is not null && _pendingActiveCandidates is not null)
        {
            _activeDecisionBuffer.Clear();
            for (var i = 0; i < _pendingActiveCandidates.Count; i++)
            {
                var uuid = _pendingActiveCandidates[i].Snapshot().Uuid;
                if (_cachedDecisions.TryGetValue(uuid, out var cached))
                {
                    _activeDecisionBuffer.Add(cached);
                }
            }

            decisions = _activeDecisionBuffer;
        }
        else
        {
            decisions = _pendingDecisions.ToArray();
        }

        var scanned = _pendingIndex;
        IReadOnlyList<AutoBuyDecision> recommendations;
        if (_incrementalCatalog is not null)
        {
            _recommendationBuffer.Clear();
            foreach (var ranked in _rankedRecommendations)
            {
                _recommendationBuffer.Add(ranked);
            }

            recommendations = _recommendationBuffer;
        }
        else
        {
            recommendations = decisions
                .Where(decision => decision.Kind == AutoBuyDecisionKind.Recommendation)
                .OrderByDescending(decision => decision.PriorityRank)
                .ThenBy(decision => decision.CostRatio)
                .ThenBy(decision => decision.Candidate.Uuid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var recommendation = recommendations.Count > 0 ? recommendations[0] : null;

        try
        {
            LogDecision(scanned, elapsedMilliseconds, recommendation, decisions);

            if (_registryReconciliationPending ||
                recommendation is null ||
                _config.AutoBuyMode.Value != AutoBuyOperationMode.Active)
            {
                return;
            }

            StartRankedBatch(recommendations);
        }
        finally
        {
            ResetPendingScan();
            if (_registryReconciliationPending)
            {
                _registryReconciliationPending = false;
                _secondsUntilEvaluation = 0.0f;
            }
        }
    }

    private void StartRankedBatch(IReadOnlyList<AutoBuyDecision> recommendations)
    {
        _pendingPurchaseRecommendations = recommendations;
        _pendingPurchaseIndex = 0;
        _pendingBatchPurchased = 0;
        _pendingBatchAttempted = 0;
        _pendingBatchElapsedMilliseconds = 0.0;
        _pendingBatchCpuSliced = false;
        _pendingBatchQueueWaitLogged = false;
        _pendingWaitingForQueue = false;
        _incrementalCatalog?.BeginMutationEvaluation();
        if (_coordinator is null)
        {
            ContinueRankedBatch();
        }
    }

    private void ContinueRankedBatch(bool singleStep = false)
    {
        var recommendations = _pendingPurchaseRecommendations;
        if (recommendations is null)
        {
            return;
        }

        if (singleStep)
        {
            // A shared-coordinator denial can defer this batch for several
            // frames. Start a fresh lazy resource epoch immediately before
            // final reserve and affordability validation.
            _incrementalCatalog?.BeginMutationEvaluation();
        }

        var maximumPurchases = _config.AutoBuyBatchSizing.Value == AutoBuyBatchSizingMode.FillAvailableQueue
            ? int.MaxValue
            : Math.Max(1, _config.MaxPurchasesPerBatch.Value);
        var targetLabel = maximumPurchases == int.MaxValue ? "queue" : maximumPurchases.ToString(CultureInfo.InvariantCulture);
        var cpuBudget = EffectiveCpuBudget(_config.CpuBudgetMilliseconds.Value);
        var stopwatch = Stopwatch.StartNew();
        var stoppedByQueue = false;
        _pendingWaitingForQueue = false;

        while (_pendingPurchaseIndex < recommendations.Count && _pendingBatchPurchased < maximumPurchases)
        {
            if (!TryCaptureQueueCapacity(RemainingAutomationUsage(maximumPurchases), out var queueCapacity) ||
                queueCapacity.UsableAutomationRoom <= 0)
            {
                stoppedByQueue = true;
                break;
            }

            var recommendation = recommendations[_pendingPurchaseIndex];
            if (_nativeCompletionGeneration > 0 &&
                _catalog is IAutoBuyCompletionRevalidationCatalog completionCatalog &&
                !completionCatalog.TryRefreshCandidateAfterCompletion(
                    recommendation.Candidate.Source,
                    _nativeCompletionGeneration,
                    out var completionRefreshReason))
            {
                if (ShouldLogNativeFailure(recommendation.Candidate.Uuid))
                {
                    _log.LogAutomataWarning(
                        $"Auto Buy deferred {recommendation.Candidate.DisplayName} " +
                        $"({recommendation.Candidate.Uuid}) because completion-state refresh failed: " +
                        completionRefreshReason);
                }

                AdvancePurchaseCandidate();
                _pendingPurchaseIndex = recommendations.Count;
                _secondsUntilEvaluation = 0.0f;
                break;
            }

            var revalidated = EvaluateCandidate(
                recommendation.Candidate.Source,
                out var suppressResourceTracking);
            _rejectionTelemetry.Record(revalidated);
            if (revalidated.Kind != AutoBuyDecisionKind.Recommendation)
            {
                if (_incrementalCatalog is not null)
                {
                    UpdateCachedDecision(revalidated);
                    _incrementalCatalog.CompleteCandidateEvaluation(
                        recommendation.Candidate.Source,
                        suppressResourceTracking,
                        IsPolicyExcluded(revalidated));
                }

                if (_config.IsOperationalLoggingEnabled &&
                    _config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Verbose)
                {
                    _log.LogAutomataInfo(
                        $"Auto Buy batch deferred {recommendation.Candidate.DisplayName} because its state changed: " +
                        revalidated.Detail);
                }

                AdvancePurchaseCandidate();
                if (IsAffordableRepeatGroup && recommendations.Count == 1)
                {
                    // The prepared group ends at its first failed live
                    // admission. Do not consider a lower cached rank until
                    // dirty resource and lifecycle state has settled.
                    _pendingPurchaseIndex = recommendations.Count;
                    _secondsUntilEvaluation = 0.0f;
                }

                if (singleStep)
                {
                    _pendingBatchCpuSliced = true;
                    break;
                }

                if (ReachedPurchaseSliceBudget(stopwatch, cpuBudget))
                {
                    _pendingBatchCpuSliced = true;
                    break;
                }

                continue;
            }

            // Availability, costs, and reserves can invoke native code. Read
            // the complete authoritative queue state again after those checks
            // so every queued mutation receives an immediately fresh snapshot.
            if (!TryCaptureQueueCapacity(RemainingAutomationUsage(maximumPurchases), out queueCapacity) ||
                queueCapacity.UsableAutomationRoom <= 0)
            {
                stoppedByQueue = true;
                break;
            }

            if (_pendingCandidateRepeatLimit <= 0)
            {
                _pendingCandidateRepeatLimit = GetCandidateRepeatLimit(
                    recommendation,
                    queueCapacity.UsableAutomationRoom,
                    recommendations.Count);
            }

            _pendingBatchAttempted++;
            _nativePurchaseAttempts++;
            bool purchaseSucceeded;
            string reason;
            using (AutoBuyLifecycleSignal.EnterAutomatedMutation(
                       GetNativeIdentity(recommendation.Candidate.Source)))
            {
                purchaseSucceeded = recommendation.Candidate.Source.TryPurchaseOne(out reason);
            }
            _incrementalCatalog?.NotifyPurchaseAttempted(recommendation.Candidate.Source);
            var upgradeQuarantineActive = SynchronizeUpgradeQuarantine(cancelPendingBatch: false);
            if (upgradeQuarantineActive && recommendation.Candidate.Kind == AutoBuyCandidateKind.Upgrade)
            {
                // The native cleanup failure may have happened after the
                // purchase call returned. Do not let any remaining cached
                // Upgrade consume another mutation lease in this batch.
                _pendingPurchaseIndex = recommendations.Count;
            }

            if (purchaseSucceeded)
            {
                _pendingBatchPurchased++;
                _successfulPurchasesThisSession++;
                _pendingCandidateRepeats++;
                if (_config.IsOperationalLoggingEnabled &&
                    _config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Verbose)
                {
                    _log.LogAutomataInfo(
                        $"Auto Buy purchased one {recommendation.Candidate.Kind} level: " +
                        $"{recommendation.Candidate.DisplayName} ({recommendation.Candidate.Uuid}); " +
                        $"BatchPurchases={_pendingBatchPurchased}/{targetLabel}; " +
                        $"SessionPurchases={_successfulPurchasesThisSession}.");
                }

                if (_pendingCandidateRepeats >= _pendingCandidateRepeatLimit)
                {
                    AdvancePurchaseCandidate();
                    if (_incrementalCatalog is not null &&
                        _pendingPurchaseIndex < recommendations.Count &&
                        _pendingBatchPurchased < maximumPurchases)
                    {
                        if (!TryCaptureQueueCapacity(
                                RemainingAutomationUsage(maximumPurchases),
                                out var refreshedQueueCapacity) ||
                            refreshedQueueCapacity.UsableAutomationRoom <= 0)
                        {
                            // Keep the already ranked next candidate prepared
                            // while the native queue is full. It will still be
                            // live-revalidated when a slot reopens.
                            stoppedByQueue = true;
                            break;
                        }

                        if (!IsAffordableRepeatGroup)
                        {
                            // Fixed, Bulk Development, and action-multiplier
                            // groups retain their settle-and-rerank boundary.
                            _pendingPurchaseIndex = recommendations.Count;
                        }
                    }
                }
            }
            else
            {
                _nativePurchaseFailures++;
                if (ShouldLogNativeFailure(recommendation.Candidate.Uuid))
                {
                    _log.LogAutomataWarning(
                        $"Auto Buy could not purchase {recommendation.Candidate.DisplayName} " +
                        $"({recommendation.Candidate.Uuid}): {reason}");
                }
                AdvancePurchaseCandidate();
                if (_incrementalCatalog is not null || IsAffordableRepeatGroup)
                {
                    // A native call may have spent resources or changed queue
                    // state even when post-call verification failed. Settle
                    // the invalidations before another candidate mutates.
                    _pendingPurchaseIndex = recommendations.Count;
                    _secondsUntilEvaluation = 0.0f;
                }
            }

            if (singleStep &&
                _pendingPurchaseIndex < recommendations.Count &&
                _pendingBatchPurchased < maximumPurchases)
            {
                _pendingBatchCpuSliced = true;
                break;
            }

            if (ReachedPurchaseSliceBudget(stopwatch, cpuBudget) &&
                _pendingPurchaseIndex < recommendations.Count &&
                _pendingBatchPurchased < maximumPurchases)
            {
                _pendingBatchCpuSliced = true;
                break;
            }
        }

        _pendingBatchElapsedMilliseconds += _readPurchaseElapsedMilliseconds(stopwatch);
        if (stoppedByQueue)
        {
            _pendingWaitingForQueue = true;
            if (!_pendingBatchQueueWaitLogged)
            {
                _pendingBatchQueueWaitLogged = true;
                if (_config.IsOperationalLoggingEnabled &&
                    (_config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Verbose ||
                     _queueWaitLogGate.ShouldLog("autobuy-queue-wait", _lifetime.Elapsed)))
                {
                    _log.LogAutomataInfo(
                        "Auto Buy preserved its next prepared ranked candidate while waiting for native queue room; " +
                        "it will feed the first available slot without rescanning.");
                }
            }

            return;
        }

        var batchComplete = _pendingBatchPurchased >= maximumPurchases ||
                            _pendingPurchaseIndex >= recommendations.Count;
        if (!batchComplete)
        {
            return;
        }

        if (_pendingBatchAttempted > 0)
        {
            RecordAndMaybeLogBatchSummary(recommendations.Count);
        }

        var replenishImmediately = _pendingBatchPurchased > 0;
        ResetPendingPurchaseBatch();
        if (replenishImmediately)
        {
            // The next scan runs on the next Unity frame while already queued
            // native work continues. Candidate access stays on the main thread.
            _secondsUntilEvaluation = 0.0f;
        }
    }

    private bool ReachedPurchaseSliceBudget(Stopwatch stopwatch, double cpuBudget)
    {
        return _readPurchaseElapsedMilliseconds(stopwatch) >= cpuBudget;
    }

    private void RecordAndMaybeLogBatchSummary(int eligibleCount)
    {
        if (!_config.IsOperationalLoggingEnabled)
        {
            ResetBatchDiagnostics();
            return;
        }

        _diagnosticBatchCount++;
        _diagnosticBatchPurchased += _pendingBatchPurchased;
        _diagnosticBatchAttempted += _pendingBatchAttempted;
        _diagnosticBatchElapsedMilliseconds += _pendingBatchElapsedMilliseconds;
        if (_pendingBatchCpuSliced)
        {
            _diagnosticCpuSlicedBatchCount++;
        }

        if (_pendingBatchQueueWaitLogged)
        {
            _diagnosticQueueWaitedBatchCount++;
        }

        if (!_batchSummaryLogGate.ShouldLog("autobuy-batch-summary", _lifetime.Elapsed))
        {
            return;
        }

        _log.LogAutomataInfo(
            $"Auto Buy batch summary: Batches={_diagnosticBatchCount}, " +
            $"Purchased={_diagnosticBatchPurchased}, Attempted={_diagnosticBatchAttempted}, " +
            $"Failed={_diagnosticBatchAttempted - _diagnosticBatchPurchased}, " +
            $"LifetimeAttempts={_nativePurchaseAttempts}, LifetimeFailures={_nativePurchaseFailures}, " +
            $"Eligible={eligibleCount}, Sizing={_config.AutoBuyBatchSizing.Value}, " +
            $"QueueWaited={_diagnosticQueueWaitedBatchCount > 0}, " +
            $"CpuSliced={_diagnosticCpuSlicedBatchCount > 0}, " +
            $"WorkElapsedMs={_diagnosticBatchElapsedMilliseconds:0.###}.");

        ResetBatchDiagnostics();
    }

    private void ResetBatchDiagnostics()
    {
        _diagnosticBatchCount = 0;
        _diagnosticBatchPurchased = 0;
        _diagnosticBatchAttempted = 0;
        _diagnosticCpuSlicedBatchCount = 0;
        _diagnosticQueueWaitedBatchCount = 0;
        _diagnosticBatchElapsedMilliseconds = 0.0;
    }

    private int GetCandidateRepeatLimit(
        AutoBuyDecision recommendation,
        int availableQueueSlots,
        int recommendationCount)
    {
        if (_config.RespectActionMultiplier.Value &&
            _catalog.TryGetActionMultiplier(out var multiplier))
        {
            return Math.Max(1, Math.Min(availableQueueSlots, multiplier));
        }

        if (_config.RepeatWhileAffordable.Value)
        {
            // A lone candidate may consume all usable room. When several are
            // ready, give each one independently revalidated level before the
            // next scan so one cheap candidate cannot monopolize the queue.
            return recommendationCount > 1
                ? 1
                : Math.Max(1, availableQueueSlots);
        }

        if (recommendation.Candidate.Kind != AutoBuyCandidateKind.Structure)
        {
            return 1;
        }

        return _config.StructureRepeatMode.Value switch
        {
            AutoBuyStructureRepeatMode.Fixed =>
                Math.Max(1, Math.Min(availableQueueSlots, Math.Min(100, _config.FixedStructureLevelsPerCandidate.Value))),
            AutoBuyStructureRepeatMode.BulkDevelopment when _catalog.TryGetBulkDevelopment(out var levels) =>
                Math.Max(1, Math.Min(availableQueueSlots, Math.Min(100, levels))),
            _ => 1,
        };
    }

    private bool IsAffordableRepeatGroup =>
        !_config.RespectActionMultiplier.Value && _config.RepeatWhileAffordable.Value;

    private bool TryCaptureQueueCapacity(out QueueCapacitySnapshot snapshot)
    {
        var automationUsageLimit = _config.AutoBuyBatchSizing.Value == AutoBuyBatchSizingMode.FillAvailableQueue
            ? int.MaxValue
            : Math.Max(1, _config.MaxPurchasesPerBatch.Value);
        return TryCaptureQueueCapacity(RemainingAutomationUsage(automationUsageLimit), out snapshot);
    }

    private bool TryCaptureQueueCapacity(
        int automationUsageLimit,
        out QueueCapacitySnapshot snapshot)
    {
        return _catalog.TryCaptureQueueCapacity(
            automationUsageLimit,
            _config.LeaveQueueSlots.Value,
            out snapshot);
    }

    private int RemainingAutomationUsage(int automationUsageLimit)
    {
        return automationUsageLimit == int.MaxValue
            ? int.MaxValue
            : Math.Max(0, automationUsageLimit - _pendingBatchPurchased);
    }

    private static bool IsPolicyExcluded(AutoBuyDecision decision)
    {
        return decision.RejectionReason == AutoBuyRejectionReason.NotAllowed ||
               decision.RejectionReason == AutoBuyRejectionReason.ConfigurationBlocked;
    }

    private bool ShouldLogNativeFailure(string uuid)
    {
        var now = _lifetime.Elapsed;
        if (_nativeFailureLogTimes.TryGetValue(uuid, out var lastLoggedAt) &&
            now - lastLoggedAt < TimeSpan.FromSeconds(30))
        {
            return false;
        }

        _nativeFailureLogTimes[uuid] = now;
        return true;
    }

    private void AdvancePurchaseCandidate()
    {
        _pendingPurchaseIndex++;
        _pendingCandidateRepeats = 0;
        _pendingCandidateRepeatLimit = 0;
    }

    private static object GetNativeIdentity(IAutoBuyCandidate candidate)
    {
        return candidate is IAutoBuyNativeIdentity identity ? identity.NativeIdentity : candidate;
    }

    private void LogDecision(
        int scanned,
        double elapsedMilliseconds,
        AutoBuyDecision? recommendation,
        IReadOnlyList<AutoBuyDecision> decisions)
    {
        if (!_config.IsOperationalLoggingEnabled)
        {
            return;
        }

        var state = _config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Verbose
            ? recommendation is null
                ? $"{_config.AutoBuyMode.Value}:autobuy:none"
                : $"{_config.AutoBuyMode.Value}:autobuy:{recommendation.Candidate.Uuid}"
            : $"{_config.AutoBuyMode.Value}:autobuy-summary";
        if (!_decisionLogGate.ShouldLog(state, _lifetime.Elapsed))
        {
            return;
        }

        if (recommendation is null)
        {
            _log.LogAutomataInfo($"Auto Buy found no eligible purchase. Scanned={scanned}, ElapsedMs={elapsedMilliseconds:0.###}.");
        }
        else
        {
            _log.LogAutomataInfo(
                $"Auto Buy recommendation: {recommendation.Candidate.DisplayName} ({recommendation.Candidate.Uuid}); " +
                $"Kind={recommendation.Candidate.Kind}; MaxCostRatio={recommendation.CostRatio.ToString("0.###e+0", CultureInfo.InvariantCulture)}; " +
                $"Admission={recommendation.Detail}; Scanned={scanned}, ElapsedMs={elapsedMilliseconds:0.###}.");
        }

        var rejectionTelemetry = _rejectionTelemetry.Snapshot();
        _log.LogAutomataInfo(
            "Auto Buy rejection summary: " +
            $"Evaluations={rejectionTelemetry.Evaluations}; " +
            $"Recommendations={rejectionTelemetry.Recommendations}; " +
            $"Rejections={rejectionTelemetry.Rejections}; " +
            $"RepeatedUnchanged={rejectionTelemetry.RepeatedUnchangedRejections}; " +
            $"StateChanges={rejectionTelemetry.RejectionStateChanges}; " +
            $"StateExits={rejectionTelemetry.RejectionExits}; " +
            $"ScanLimitDeferrals={rejectionTelemetry.ScanLimitDeferrals}; " +
            $"CurrentRejected={rejectionTelemetry.CurrentRejectedCandidates}; " +
            $"ByReason={rejectionTelemetry.FormatReasonCounts()}.");

        if (_config.DecisionLogLevel.Value != AutomataDecisionLogLevel.Verbose)
        {
            return;
        }

        var remaining = Math.Max(0, _config.MaxLoggedRejections.Value);
        foreach (var decision in decisions.Where(decision => decision.Kind == AutoBuyDecisionKind.Rejection))
        {
            if (remaining-- <= 0)
            {
                break;
            }

            _log.LogAutomataInfo(
                $"Auto Buy rejected: {decision.Candidate.DisplayName} ({decision.Candidate.Uuid}) " +
                $"[{decision.Candidate.Kind}/{decision.RejectionReason}] - {decision.Detail}");
        }
    }

    private void LogScanProgress(int processed, int total, double elapsedMilliseconds)
    {
        if (!_config.IsOperationalLoggingEnabled ||
            !_scanProgressLogGate.ShouldLog($"{_config.AutoBuyMode.Value}:autobuy-scan", _lifetime.Elapsed))
        {
            return;
        }

        _log.LogAutomataInfo(
            $"Auto Buy scan in progress: Processed={processed}, Total={total}, ElapsedMs={elapsedMilliseconds:0.###}. " +
            $"The next evaluation resumes at candidate {processed + 1}.");
    }

    private bool IsIncludedKind(IAutoBuyCandidate candidate)
    {
        return candidate.Snapshot().Kind switch
        {
            AutoBuyCandidateKind.Structure => _config.AutoBuyStructures.Value,
            AutoBuyCandidateKind.Upgrade => _config.AutoBuyUpgrades.Value,
            _ => false,
        };
    }

    private void ResetPendingScan()
    {
        _pendingCandidates = null;
        _pendingActiveCandidates = null;
        _pendingIndex = 0;
        _pendingDecisions.Clear();
    }

    private void RemoveInactiveCachedDecisions(IReadOnlyList<IAutoBuyCandidate> activeCandidates)
    {
        if (_cachedDecisions.Count == 0)
        {
            return;
        }

        _activeCandidateUuids.Clear();
        for (var i = 0; i < activeCandidates.Count; i++)
        {
            _activeCandidateUuids.Add(activeCandidates[i].Snapshot().Uuid);
        }

        _inactiveCandidateUuids.Clear();
        foreach (var uuid in _cachedDecisions.Keys)
        {
            if (!_activeCandidateUuids.Contains(uuid))
            {
                _inactiveCandidateUuids.Add(uuid);
            }
        }

        for (var i = 0; i < _inactiveCandidateUuids.Count; i++)
        {
            var uuid = _inactiveCandidateUuids[i];
            if (_cachedDecisions.TryGetValue(uuid, out var previous) &&
                previous.Kind == AutoBuyDecisionKind.Recommendation)
            {
                _rankedRecommendations.Remove(previous);
            }

            _cachedDecisions.Remove(uuid);
            _rejectionTelemetry.Remove(uuid);
        }
    }

    private void UpdateCachedDecision(AutoBuyDecision decision)
    {
        if (decision.Candidate.Kind == AutoBuyCandidateKind.Upgrade &&
            (_upgradeQuarantineObserved || NativeMultiBuyScope.TryGetMutationQuarantine(out _)))
        {
            if (_cachedDecisions.TryGetValue(decision.Candidate.Uuid, out var quarantinedPrevious) &&
                quarantinedPrevious.Kind == AutoBuyDecisionKind.Recommendation)
            {
                _rankedRecommendations.Remove(quarantinedPrevious);
            }

            _cachedDecisions.Remove(decision.Candidate.Uuid);
            return;
        }

        if (_cachedDecisions.TryGetValue(decision.Candidate.Uuid, out var previous) &&
            previous.Kind == AutoBuyDecisionKind.Recommendation)
        {
            _rankedRecommendations.Remove(previous);
        }

        _cachedDecisions[decision.Candidate.Uuid] = decision;
        if (decision.Kind == AutoBuyDecisionKind.Recommendation)
        {
            _rankedRecommendations.Add(decision);
        }
    }

    private bool SynchronizeUpgradeQuarantine(bool cancelPendingBatch)
    {
        if (!NativeMultiBuyScope.TryGetMutationQuarantine(out var reason))
        {
            return false;
        }

        if (_upgradeQuarantineObserved)
        {
            return true;
        }

        _upgradeQuarantineObserved = true;
        UpgradeQuarantinePurgePasses++;

        _quarantinedUpgradeUuids.Clear();
        foreach (var pair in _cachedDecisions)
        {
            UpgradeQuarantineCacheEntriesInspected++;
            if (pair.Value.Candidate.Kind == AutoBuyCandidateKind.Upgrade)
            {
                _quarantinedUpgradeUuids.Add(pair.Key);
            }
        }

        for (var i = 0; i < _quarantinedUpgradeUuids.Count; i++)
        {
            var uuid = _quarantinedUpgradeUuids[i];
            if (_cachedDecisions.TryGetValue(uuid, out var decision) &&
                decision.Kind == AutoBuyDecisionKind.Recommendation)
            {
                _rankedRecommendations.Remove(decision);
            }

            _cachedDecisions.Remove(uuid);
            _rejectionTelemetry.Remove(uuid);
            UpgradeQuarantineDecisionsRemoved++;
        }

        if (cancelPendingBatch && PendingBatchContainsUpgrade())
        {
            ResetPendingPurchaseBatch();
        }

        if (_upgradeQuarantineLogGate.ShouldLog("upgrade-mutation-quarantine", _lifetime.Elapsed))
        {
            _log.LogAutomataError(
                "Auto Buy removed automated Upgrades from admission and ranking because native " +
                $"multi-buy restoration is unverified; Structures remain eligible. {reason}");
        }

        _secondsUntilEvaluation = 0.0f;

        return true;
    }

    private bool PendingBatchContainsUpgrade()
    {
        if (_pendingPurchaseRecommendations is null)
        {
            return false;
        }

        for (var i = _pendingPurchaseIndex; i < _pendingPurchaseRecommendations.Count; i++)
        {
            if (_pendingPurchaseRecommendations[i].Candidate.Kind == AutoBuyCandidateKind.Upgrade)
            {
                return true;
            }
        }

        return false;
    }

    private void ResetPendingPurchaseBatch()
    {
        _incrementalCatalog?.CompleteMutationGroup();
        _pendingPurchaseRecommendations = null;
        _pendingPurchaseIndex = 0;
        _pendingCandidateRepeats = 0;
        _pendingCandidateRepeatLimit = 0;
        _pendingBatchPurchased = 0;
        _pendingBatchAttempted = 0;
        _pendingBatchElapsedMilliseconds = 0.0;
        _pendingBatchCpuSliced = false;
        _pendingBatchQueueWaitLogged = false;
        _pendingWaitingForQueue = false;
        _secondsUntilQueuePoll = 0.0f;
    }

    private void ResetAllPendingWork()
    {
        ResetPendingScan();
        ResetPendingPurchaseBatch();
    }

    private static void PopulateUuidSet(HashSet<string> destination, string value)
    {
        destination.Clear();
        var start = 0;
        while (start <= value.Length)
        {
            var separator = value.IndexOf(',', start);
            var end = separator >= 0 ? separator : value.Length;
            var item = value.Substring(start, end - start).Trim();
            if (item.Length > 0)
            {
                destination.Add(item);
            }

            if (separator < 0)
            {
                break;
            }

            start = separator + 1;
        }
    }

    private static double MaximumAllowedCostRatio(AutoBuyAffordabilityMode mode)
    {
        return mode switch
        {
            AutoBuyAffordabilityMode.BuyAll => double.PositiveInfinity,
            AutoBuyAffordabilityMode.Excess10 => 0.1,
            AutoBuyAffordabilityMode.Excess100 => 0.01,
            AutoBuyAffordabilityMode.Excess1000 => 0.001,
            _ => 0.01,
        };
    }

    internal static IReadOnlyList<AutoBuyResourceBlocker> BuildAffordabilityBlockers(
        IReadOnlyList<ResourceAdmissionCost> costs,
        double maximumRatio)
    {
        if (double.IsPositiveInfinity(maximumRatio) || maximumRatio <= 0.0)
        {
            return Array.Empty<AutoBuyResourceBlocker>();
        }

        var blockers = new List<AutoBuyResourceBlocker>();
        for (var i = 0; i < costs.Count; i++)
        {
            var cost = costs[i];
            if (cost.Cost.IsZero || cost.Cost.DivideApprox(cost.CurrentQuantity) <= maximumRatio)
            {
                continue;
            }

            blockers.Add(new AutoBuyResourceBlocker(
                AutoBuyResourceBlockerKind.AffordabilityThreshold,
                cost.ResourceId,
                cost.ResourceName,
                cost.Cost,
                cost.CurrentQuantity,
                cost.Cost.Multiply(1.0 / maximumRatio)));
        }

        return blockers;
    }

    private static bool ContainsBandwidthCost(IReadOnlyList<ResourceAdmissionCost> costs)
    {
        for (var i = 0; i < costs.Count; i++)
        {
            if (costs[i].IsBandwidth)
            {
                return true;
            }
        }

        return false;
    }

    private static float ClampInterval(float value)
    {
        return Math.Max(0.1f, value);
    }

    internal static double EffectiveCpuBudget(double configuredMilliseconds)
    {
        return Math.Min(MaximumCpuBudgetMilliseconds, Math.Max(0.1, configuredMilliseconds));
    }

    private double EffectiveReadSliceBudget()
    {
        var configured = EffectiveCpuBudget(_config.CpuBudgetMilliseconds.Value);
        if (_coordinator is null)
        {
            return configured;
        }

        var remaining = _coordinator.SoftBudgetMilliseconds - _coordinator.CurrentFrameElapsedMilliseconds;
        return Math.Min(configured, Math.Max(0.01, remaining));
    }

    private readonly struct AutoBuyPolicyFingerprint : IEquatable<AutoBuyPolicyFingerprint>
    {
        private AutoBuyPolicyFingerprint(
            bool includeStructures,
            bool includeUpgrades,
            AutoBuyAffordabilityMode structureAffordability,
            AutoBuyAffordabilityMode upgradeAffordability,
            string absoluteReserve,
            float relativeReserve,
            string allowedUuids,
            string blockedUuids,
            bool respectActionMultiplier,
            bool repeatWhileAffordable,
            AutoBuyBatchSizingMode batchSizing,
            int batchSize,
            AutoBuyStructureRepeatMode repeatMode,
            int fixedRepeats,
            bool prioritizeCostAndQualityStructures,
            int leaveQueueSlots)
        {
            IncludeStructures = includeStructures;
            IncludeUpgrades = includeUpgrades;
            StructureAffordability = structureAffordability;
            UpgradeAffordability = upgradeAffordability;
            AbsoluteReserve = absoluteReserve;
            RelativeReserve = relativeReserve;
            AllowedUuids = allowedUuids;
            BlockedUuids = blockedUuids;
            RespectActionMultiplier = respectActionMultiplier;
            RepeatWhileAffordable = repeatWhileAffordable;
            BatchSizing = batchSizing;
            BatchSize = batchSize;
            RepeatMode = repeatMode;
            FixedRepeats = fixedRepeats;
            PrioritizeCostAndQualityStructures = prioritizeCostAndQualityStructures;
            LeaveQueueSlots = leaveQueueSlots;
        }

        private bool IncludeStructures { get; }
        private bool IncludeUpgrades { get; }
        private AutoBuyAffordabilityMode StructureAffordability { get; }
        private AutoBuyAffordabilityMode UpgradeAffordability { get; }
        private string AbsoluteReserve { get; }
        private float RelativeReserve { get; }
        private string AllowedUuids { get; }
        private string BlockedUuids { get; }
        private bool RespectActionMultiplier { get; }
        private bool RepeatWhileAffordable { get; }
        private AutoBuyBatchSizingMode BatchSizing { get; }
        private int BatchSize { get; }
        private AutoBuyStructureRepeatMode RepeatMode { get; }
        private int FixedRepeats { get; }
        private bool PrioritizeCostAndQualityStructures { get; }
        private int LeaveQueueSlots { get; }

        public static AutoBuyPolicyFingerprint Capture(AutomataConfig config)
        {
            return new AutoBuyPolicyFingerprint(
                config.AutoBuyStructures.Value,
                config.AutoBuyUpgrades.Value,
                config.AutoBuyAffordability.Value,
                config.UpgradeAffordability.Value,
                config.AbsoluteReserve.Value,
                config.RelativeReserveMultiplier.Value,
                config.AllowedAutoBuyUuids.Value,
                config.BlockedAutoBuyUuids.Value,
                config.RespectActionMultiplier.Value,
                config.RepeatWhileAffordable.Value,
                config.AutoBuyBatchSizing.Value,
                config.MaxPurchasesPerBatch.Value,
                config.StructureRepeatMode.Value,
                config.FixedStructureLevelsPerCandidate.Value,
                config.PrioritizeCostAndQualityStructures.Value,
                config.LeaveQueueSlots.Value);
        }

        public bool Equals(AutoBuyPolicyFingerprint other)
        {
            return IncludeStructures == other.IncludeStructures &&
                   IncludeUpgrades == other.IncludeUpgrades &&
                   StructureAffordability == other.StructureAffordability &&
                   UpgradeAffordability == other.UpgradeAffordability &&
                   string.Equals(AbsoluteReserve, other.AbsoluteReserve, StringComparison.Ordinal) &&
                   RelativeReserve.Equals(other.RelativeReserve) &&
                   string.Equals(AllowedUuids, other.AllowedUuids, StringComparison.Ordinal) &&
                   string.Equals(BlockedUuids, other.BlockedUuids, StringComparison.Ordinal) &&
                   RespectActionMultiplier == other.RespectActionMultiplier &&
                   RepeatWhileAffordable == other.RepeatWhileAffordable &&
                   BatchSizing == other.BatchSizing &&
                   BatchSize == other.BatchSize &&
                   RepeatMode == other.RepeatMode &&
                   FixedRepeats == other.FixedRepeats &&
                   PrioritizeCostAndQualityStructures == other.PrioritizeCostAndQualityStructures &&
                   LeaveQueueSlots == other.LeaveQueueSlots;
        }
    }

    private sealed class AutoBuyDecisionComparer : IComparer<AutoBuyDecision>
    {
        public static readonly AutoBuyDecisionComparer Instance = new AutoBuyDecisionComparer();

        public int Compare(AutoBuyDecision? left, AutoBuyDecision? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var priority = right.PriorityRank.CompareTo(left.PriorityRank);
            if (priority != 0)
            {
                return priority;
            }

            var ratio = left.CostRatio.CompareTo(right.CostRatio);
            return ratio != 0
                ? ratio
                : StringComparer.OrdinalIgnoreCase.Compare(left.Candidate.Uuid, right.Candidate.Uuid);
        }
    }

}
