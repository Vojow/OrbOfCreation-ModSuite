using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;

namespace OrbAutomata;

internal sealed class AutoBuyEngine : IDisposable
{
    private const double MaximumCpuBudgetMilliseconds = 1.0;
    private const float QueuePollIntervalSeconds = 0.1f;
    private readonly AutomataConfig _config;
    private readonly IAutoBuyCatalog _catalog;
    private readonly ReservePolicy _reservePolicy;
    private readonly ManualLogSource _log;
    private readonly Func<Stopwatch, double> _readElapsedMilliseconds;
    private readonly Func<Stopwatch, double> _readPurchaseElapsedMilliseconds;
    private readonly DecisionLogGate _decisionLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly DecisionLogGate _scanProgressLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly Stopwatch _lifetime = Stopwatch.StartNew();
    private IReadOnlyList<IAutoBuyCandidate>? _pendingCandidates;
    private readonly List<AutoBuyDecision> _pendingDecisions = new List<AutoBuyDecision>();
    private HashSet<string>? _pendingAllowedUuids;
    private HashSet<string>? _pendingBlockedUuids;
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

    public AutoBuyEngine(
        AutomataConfig config,
        IAutoBuyCatalog catalog,
        ReservePolicy reservePolicy,
        ManualLogSource log,
        Func<Stopwatch, double>? readElapsedMilliseconds = null,
        Func<Stopwatch, double>? readPurchaseElapsedMilliseconds = null)
    {
        _config = config;
        _catalog = catalog;
        _reservePolicy = reservePolicy;
        _log = log;
        _readElapsedMilliseconds = readElapsedMilliseconds ?? (stopwatch => stopwatch.Elapsed.TotalMilliseconds);
        _readPurchaseElapsedMilliseconds = readPurchaseElapsedMilliseconds ?? (stopwatch => stopwatch.Elapsed.TotalMilliseconds);
        _secondsUntilEvaluation = ClampInterval(config.AutoBuyIntervalSeconds.Value);
    }

    public void Tick(float unscaledDeltaTime)
    {
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

    public void Dispose()
    {
        ResetAllPendingWork();
        _catalog.Dispose();
    }

    private void EvaluateBatch()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!BeginScanIfNeeded())
        {
            return;
        }

        var budget = EffectiveCpuBudget(_config.CpuBudgetMilliseconds.Value);
        while (_pendingCandidates is not null && _pendingIndex < _pendingCandidates.Count)
        {
            var candidate = _pendingCandidates[_pendingIndex];
            _pendingDecisions.Add(EvaluateCandidate(candidate));
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

        if (!_catalog.TryGetRemainingQueueRoom(out _))
        {
            if (!_queueWaitingLogged)
            {
                _queueWaitingLogged = true;
                if (_config.EnableOperationalLogging.Value)
                {
                    _log.LogInfo("Auto Buy is waiting for the gameplay action queue to initialize; it will retry without purchasing.");
                }
            }

            return false;
        }

        var limit = Math.Max(1, _config.AutoBuyMaxCandidatesPerScan.Value);
        var discovered = _catalog.Discover()
            .Where(IsIncludedKind)
            .Take(limit + 1)
            .ToArray();
        _pendingCandidates = discovered.Take(limit).ToArray();
        _pendingAllowedUuids = ParseUuidSet(_config.AllowedAutoBuyUuids.Value);
        _pendingBlockedUuids = ParseUuidSet(_config.BlockedAutoBuyUuids.Value);
        _pendingIndex = 0;
        _pendingDecisions.Clear();

        if (discovered.Length > limit)
        {
            _pendingDecisions.Add(AutoBuyDecision.Rejected(discovered[limit].Snapshot(), "candidate scan limit reached"));
        }

        return true;
    }

    private AutoBuyDecision EvaluateCandidate(IAutoBuyCandidate candidate)
    {
        var snapshot = candidate.Snapshot();
        if (_pendingAllowedUuids is { Count: > 0 } && !_pendingAllowedUuids.Contains(snapshot.Uuid))
        {
            return AutoBuyDecision.Rejected(snapshot, "not included in the configured allowlist");
        }

        if (_pendingBlockedUuids?.Contains(snapshot.Uuid) == true)
        {
            return AutoBuyDecision.Rejected(snapshot, "blocked by configuration");
        }

        if (!candidate.IsAvailable())
        {
            return AutoBuyDecision.Rejected(snapshot, "not available");
        }

        if (!candidate.CanPurchase(out var nativeReason))
        {
            return AutoBuyDecision.Rejected(snapshot, nativeReason);
        }

        var reserve = _reservePolicy.Evaluate(candidate.GetCosts());
        if (!reserve.Passed)
        {
            return AutoBuyDecision.Rejected(snapshot, reserve.Reason);
        }

        var affordabilityMode = snapshot.Kind == AutoBuyCandidateKind.Upgrade
            ? _config.UpgradeAffordability.Value
            : _config.AutoBuyAffordability.Value;
        var maximumRatio = MaximumAllowedCostRatio(affordabilityMode);
        if (reserve.MaxCostToQuantityRatio > maximumRatio)
        {
            return AutoBuyDecision.Rejected(
                snapshot,
                $"{snapshot.Kind} affordability mode {affordabilityMode} rejected " +
                $"maxCostRatio={reserve.MaxCostToQuantityRatio.ToString("0.###e+0", CultureInfo.InvariantCulture)}; " +
                $"limit={maximumRatio.ToString("0.###e+0", CultureInfo.InvariantCulture)}");
        }

        return AutoBuyDecision.Recommended(snapshot, reserve.MaxCostToQuantityRatio, reserve.Summary);
    }

    private void CompleteScan(double elapsedMilliseconds)
    {
        var decisions = _pendingDecisions.ToArray();
        var scanned = _pendingIndex;
        var recommendations = decisions
            .Where(decision => decision.Kind == AutoBuyDecisionKind.Recommendation)
            .OrderBy(decision => decision.CostRatio)
            .ThenBy(decision => decision.Candidate.Uuid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var recommendation = recommendations.FirstOrDefault();

        try
        {
            LogDecision(scanned, elapsedMilliseconds, recommendation, decisions);

            if (recommendation is null || _config.AutoBuyMode.Value != AutoBuyOperationMode.Active)
            {
                return;
            }

            StartRankedBatch(recommendations);
        }
        finally
        {
            ResetPendingScan();
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
        ContinueRankedBatch();
    }

    private void ContinueRankedBatch()
    {
        var recommendations = _pendingPurchaseRecommendations;
        if (recommendations is null)
        {
            return;
        }

        var maximumPurchases = _config.AutoBuyBatchSizing.Value == AutoBuyBatchSizingMode.FillAvailableQueue
            ? int.MaxValue
            : Math.Max(1, _config.MaxPurchasesPerBatch.Value);
        var targetLabel = maximumPurchases == int.MaxValue ? "queue" : maximumPurchases.ToString(CultureInfo.InvariantCulture);
        var queueReserve = Math.Max(0, _config.LeaveQueueSlots.Value);
        var cpuBudget = EffectiveCpuBudget(_config.CpuBudgetMilliseconds.Value);
        var stopwatch = Stopwatch.StartNew();
        var stoppedByQueue = false;
        _pendingWaitingForQueue = false;

        while (_pendingPurchaseIndex < recommendations.Count && _pendingBatchPurchased < maximumPurchases)
        {
            if (!_catalog.TryGetRemainingQueueRoom(out var room) || room <= queueReserve)
            {
                stoppedByQueue = true;
                break;
            }

            var recommendation = recommendations[_pendingPurchaseIndex];
            var revalidated = EvaluateCandidate(recommendation.Candidate.Source);
            if (revalidated.Kind != AutoBuyDecisionKind.Recommendation)
            {
                if (_config.EnableOperationalLogging.Value &&
                    _config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Verbose)
                {
                    _log.LogInfo(
                        $"Auto Buy batch deferred {recommendation.Candidate.DisplayName} because its state changed: " +
                        revalidated.Detail);
                }

                AdvancePurchaseCandidate();

                if (ReachedPurchaseSliceBudget(stopwatch, cpuBudget))
                {
                    _pendingBatchCpuSliced = true;
                    break;
                }

                continue;
            }

            if (_pendingCandidateRepeatLimit <= 0)
            {
                _pendingCandidateRepeatLimit = GetCandidateRepeatLimit(
                    recommendation,
                    Math.Max(1, room - queueReserve));
            }

            _pendingBatchAttempted++;
            if (recommendation.Candidate.Source.TryPurchaseOne(out var reason))
            {
                _pendingBatchPurchased++;
                _successfulPurchasesThisSession++;
                _pendingCandidateRepeats++;
                if (_config.EnableOperationalLogging.Value)
                {
                    _log.LogInfo(
                        $"Auto Buy purchased one {recommendation.Candidate.Kind} level: " +
                        $"{recommendation.Candidate.DisplayName} ({recommendation.Candidate.Uuid}); " +
                        $"BatchPurchases={_pendingBatchPurchased}/{targetLabel}; " +
                        $"SessionPurchases={_successfulPurchasesThisSession}.");
                }

                if (_pendingCandidateRepeats >= _pendingCandidateRepeatLimit)
                {
                    AdvancePurchaseCandidate();
                }
            }
            else
            {
                _log.LogWarning(
                    $"Auto Buy could not purchase {recommendation.Candidate.DisplayName} " +
                    $"({recommendation.Candidate.Uuid}): {reason}");
                AdvancePurchaseCandidate();
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
                if (_config.EnableOperationalLogging.Value)
                {
                    _log.LogInfo(
                        "Auto Buy prepared its next ranked batch and is waiting for native queue room; " +
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

        if (_config.EnableOperationalLogging.Value && _pendingBatchPurchased > 0)
        {
            _log.LogInfo(
                $"Auto Buy batch complete: Purchased={_pendingBatchPurchased}, Attempted={_pendingBatchAttempted}, " +
                $"Eligible={recommendations.Count}, Sizing={_config.AutoBuyBatchSizing.Value}, " +
                $"QueueWaited={_pendingBatchQueueWaitLogged}, CpuSliced={_pendingBatchCpuSliced}, " +
                $"ElapsedMs={_pendingBatchElapsedMilliseconds:0.###}.");
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

    private int GetCandidateRepeatLimit(AutoBuyDecision recommendation, int availableQueueSlots)
    {
        if (_config.RespectActionMultiplier.Value &&
            _catalog.TryGetActionMultiplier(out var multiplier))
        {
            return Math.Max(1, Math.Min(availableQueueSlots, multiplier));
        }

        if (recommendation.Candidate.Kind != AutoBuyCandidateKind.Structure)
        {
            return 1;
        }

        return _config.StructureRepeatMode.Value switch
        {
            AutoBuyStructureRepeatMode.Fixed => Math.Max(1, Math.Min(100, _config.FixedStructureLevelsPerCandidate.Value)),
            AutoBuyStructureRepeatMode.BulkDevelopment when _catalog.TryGetBulkDevelopment(out var levels) =>
                Math.Max(1, Math.Min(100, levels)),
            _ => 1,
        };
    }

    private void AdvancePurchaseCandidate()
    {
        _pendingPurchaseIndex++;
        _pendingCandidateRepeats = 0;
        _pendingCandidateRepeatLimit = 0;
    }

    private void LogDecision(
        int scanned,
        double elapsedMilliseconds,
        AutoBuyDecision? recommendation,
        IReadOnlyList<AutoBuyDecision> decisions)
    {
        if (!_config.EnableOperationalLogging.Value ||
            _config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Off)
        {
            return;
        }

        var state = recommendation is null
            ? $"{_config.AutoBuyMode.Value}:autobuy:none"
            : $"{_config.AutoBuyMode.Value}:autobuy:{recommendation.Candidate.Uuid}";
        if (!_decisionLogGate.ShouldLog(state, _lifetime.Elapsed))
        {
            return;
        }

        if (recommendation is null)
        {
            _log.LogInfo($"Auto Buy found no eligible purchase. Scanned={scanned}, ElapsedMs={elapsedMilliseconds:0.###}.");
        }
        else
        {
            _log.LogInfo(
                $"Auto Buy recommendation: {recommendation.Candidate.DisplayName} ({recommendation.Candidate.Uuid}); " +
                $"Kind={recommendation.Candidate.Kind}; MaxCostRatio={recommendation.CostRatio.ToString("0.###e+0", CultureInfo.InvariantCulture)}; " +
                $"Admission={recommendation.Detail}; Scanned={scanned}, ElapsedMs={elapsedMilliseconds:0.###}.");
        }

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

            _log.LogInfo(
                $"Auto Buy rejected: {decision.Candidate.DisplayName} ({decision.Candidate.Uuid}) " +
                $"[{decision.Candidate.Kind}] - {decision.Detail}");
        }
    }

    private void LogScanProgress(int processed, int total, double elapsedMilliseconds)
    {
        if (!_config.EnableOperationalLogging.Value ||
            _config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Off ||
            !_scanProgressLogGate.ShouldLog($"{_config.AutoBuyMode.Value}:autobuy-scan", _lifetime.Elapsed))
        {
            return;
        }

        _log.LogInfo(
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
        _pendingAllowedUuids = null;
        _pendingBlockedUuids = null;
        _pendingIndex = 0;
        _pendingDecisions.Clear();
    }

    private void ResetPendingPurchaseBatch()
    {
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

    private static HashSet<string> ParseUuidSet(string value)
    {
        return new HashSet<string>(
            value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0),
            StringComparer.OrdinalIgnoreCase);
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

    private static float ClampInterval(float value)
    {
        return Math.Max(0.1f, value);
    }

    internal static double EffectiveCpuBudget(double configuredMilliseconds)
    {
        return Math.Min(MaximumCpuBudgetMilliseconds, Math.Max(0.1, configuredMilliseconds));
    }

}
