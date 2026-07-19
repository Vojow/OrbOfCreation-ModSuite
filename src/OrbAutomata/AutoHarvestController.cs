using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common;

namespace OrbAutomata;

internal interface IAutoHarvestRuntime : IDisposable
{
    bool TryReadCandidate(
        AutoHarvestPair pair,
        bool selected,
        out NativeAutoHarvestCandidate? candidate,
        out AutoHarvestCandidateSnapshot snapshot,
        out string reason);

    AutoHarvestSubmissionResult TrySubmit(NativeAutoHarvestCandidate candidate);
    void InvalidateLifecycle();
}

internal sealed class NativeAutoHarvestCandidate
{
    public NativeAutoHarvestCandidate(
        AutoHarvestPair pair,
        long lifecycleEpoch,
        object plot,
        object action,
        object prototype)
    {
        Pair = pair;
        LifecycleEpoch = lifecycleEpoch;
        Plot = plot;
        Action = action;
        Prototype = prototype;
    }

    public AutoHarvestPair Pair { get; }
    public long LifecycleEpoch { get; }
    public object Plot { get; }
    public object Action { get; }
    public object Prototype { get; }
}

internal readonly struct AutoHarvestSubmissionResult
{
    public AutoHarvestSubmissionResult(bool verified, bool mutationAttempted, string reason)
    {
        Verified = verified;
        MutationAttempted = mutationAttempted;
        Reason = reason ?? string.Empty;
    }

    public bool Verified { get; }
    public bool MutationAttempted { get; }
    public string Reason { get; }
}

internal sealed class AutoHarvestController : IDisposable
{
    private readonly AutomataConfig _config;
    private readonly IAutoHarvestRuntime _runtime;
    private readonly ManualLogSource _log;
    private readonly SuitePerformanceCoordinator _coordinator;
    private readonly Func<long> _readFrameIdentity;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly SuiteWorkRegistration _readWork;
    private readonly SuiteWorkRegistration _mutationWork;
    private readonly HashSet<AutoHarvestPair> _blockedUntilLifecycle = new();
    private readonly DecisionLogGate _failureLogGate = new(TimeSpan.FromSeconds(30));
    private NativeAutoHarvestCandidate? _pending;
    private AutoHarvestPair _nextPair = AutoHarvestPair.FruitTree;
    private float _secondsUntilEvaluation;
    private double _elapsedSeconds;
    private bool _wasEnabled;

    public AutoHarvestController(
        AutomataConfig config,
        IAutoHarvestRuntime runtime,
        ManualLogSource log,
        SuitePerformanceCoordinator coordinator,
        Func<long> readFrameIdentity,
        Func<long>? readLifecycleEpoch = null)
    {
        _config = config;
        _runtime = runtime;
        _log = log;
        _coordinator = coordinator;
        _readFrameIdentity = readFrameIdentity;
        _readLifecycleEpoch = readLifecycleEpoch ?? (() => GameLifecycleMonitor.Shared.Current.Generation);
        _readWork = coordinator.Register(
            "OrbAutomata.AutoHarvest",
            "Evaluate audited native harvest actions",
            SuiteBudgetClass.SoftLimited,
            SuiteWorkExecutionKind.Cooperative);
        _mutationWork = coordinator.Register(
            "OrbAutomata.AutoHarvest",
            "Queue one audited native harvest action",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
    }

    public void Tick(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        var enabled = _config.CanStartAutoHarvestActively &&
            (_config.AutoHarvestFruitTrees.Value || _config.AutoHarvestTreasureTrees.Value);
        SetEnabled(enabled);
        if (!enabled)
        {
            _pending = null;
            _wasEnabled = false;
            return;
        }

        if (!_wasEnabled) _secondsUntilEvaluation = 0.0f;
        _wasEnabled = true;
        _secondsUntilEvaluation -= elapsed;

        var readDue = _pending is null && _secondsUntilEvaluation <= 0.0f;
        _readWork.SetPending(readDue);
        _mutationWork.SetPending(_pending is not null);
        if (readDue && TryAcquire(_readWork, out var readLease))
        {
            using (readLease)
            {
                Evaluate();
                readLease.Complete();
            }
        }

        _readWork.SetPending(false);
        _mutationWork.SetPending(_pending is not null);
        if (_pending is not null && TryAcquire(_mutationWork, out var mutationLease))
        {
            using (mutationLease)
            {
                ExecuteMutation();
                mutationLease.Complete();
            }
        }
        _mutationWork.SetPending(_pending is not null);
    }

    public void InvalidateLifecycle()
    {
        _runtime.InvalidateLifecycle();
        _pending = null;
        _blockedUntilLifecycle.Clear();
        _secondsUntilEvaluation = 0.0f;
    }

    private void Evaluate()
    {
        _pending = null;
        var first = _nextPair;
        var second = first == AutoHarvestPair.FruitTree
            ? AutoHarvestPair.TreasureTree
            : AutoHarvestPair.FruitTree;
        if (!TrySelect(first)) TrySelect(second);
        _secondsUntilEvaluation = Math.Max(0.25f, _config.AutoHarvestEvaluationIntervalSeconds.Value);
    }

    private bool TrySelect(AutoHarvestPair pair)
    {
        var selected = IsSelected(pair);
        if (!selected || _blockedUntilLifecycle.Contains(pair)) return false;
        if (!_runtime.TryReadCandidate(pair, selected, out var candidate, out var snapshot, out var reason))
        {
            if (!string.IsNullOrWhiteSpace(reason)) LogFailure($"Auto Harvest {pair} unavailable: {reason}");
            return false;
        }

        var decision = AutoHarvestPolicy.Evaluate(snapshot, _readLifecycleEpoch());
        if (!decision.ShouldSubmit || candidate is null) return false;
        _pending = candidate;
        return true;
    }

    private void ExecuteMutation()
    {
        var candidate = _pending;
        _pending = null;
        if (candidate is null || !_config.CanStartAutoHarvestActively || !IsSelected(candidate.Pair)) return;

        var result = _runtime.TrySubmit(candidate);
        if (!result.Verified)
        {
            if (result.MutationAttempted) _blockedUntilLifecycle.Add(candidate.Pair);
            if (!string.IsNullOrWhiteSpace(result.Reason))
                LogFailure($"Auto Harvest {candidate.Pair} rejected: {result.Reason}");
            _secondsUntilEvaluation = Math.Max(0.25f, _config.AutoHarvestEvaluationIntervalSeconds.Value);
            return;
        }

        _nextPair = candidate.Pair == AutoHarvestPair.FruitTree
            ? AutoHarvestPair.TreasureTree
            : AutoHarvestPair.FruitTree;
        if (_config.IsOperationalLoggingEnabled)
            _log.LogAutomataInfo($"Auto Harvest queued one native {candidate.Pair} collect action.");
        _secondsUntilEvaluation = Math.Max(0.25f, _config.AutoHarvestEvaluationIntervalSeconds.Value);
    }

    private bool IsSelected(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => _config.AutoHarvestFruitTrees.Value,
        AutoHarvestPair.TreasureTree => _config.AutoHarvestTreasureTrees.Value,
        _ => false,
    };

    private bool TryAcquire(SuiteWorkRegistration registration, out SuiteWorkLease lease) =>
        _coordinator.RequestWork(registration, _readFrameIdentity(), out lease) == SuiteWorkAdmission.Granted;

    private void SetEnabled(bool enabled)
    {
        if (_readWork.IsEnabled != enabled) _readWork.SetEnabled(enabled);
        if (_mutationWork.IsEnabled != enabled) _mutationWork.SetEnabled(enabled);
        if (!enabled)
        {
            _readWork.SetPending(false);
            _mutationWork.SetPending(false);
        }
    }

    private void LogFailure(string message)
    {
        if (_failureLogGate.ShouldLog(message, TimeSpan.FromSeconds(_elapsedSeconds)))
            _log.LogAutomataWarning(message);
    }

    public void Dispose()
    {
        SetEnabled(false);
        _runtime.Dispose();
        _pending = null;
        _blockedUntilLifecycle.Clear();
    }
}
