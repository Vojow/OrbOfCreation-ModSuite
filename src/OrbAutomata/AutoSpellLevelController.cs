using System;
using BepInEx.Logging;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoSpellLevelController : IDisposable
{
    private readonly AutomataConfig _config;
    private readonly ReflectionSpellLevelRuntime _runtime;
    private readonly ManualLogSource _log;
    private readonly SuitePerformanceCoordinator _coordinator;
    private readonly Func<long> _readFrameIdentity;
    private readonly SuiteWorkRegistration _readWork;
    private readonly SuiteWorkRegistration _mutationWork;
    private readonly DecisionLogGate _failureLogGate = new(TimeSpan.FromSeconds(30));
    private NativeSpellLevelCandidate? _pending;
    private AutoSpellLevelCapability _pendingCapability;
    private AutoSpellLevelCapability _capability = AutoSpellLevelCapability.Locked;
    private float _secondsUntilEvaluation;
    private double _elapsedSeconds;
    private bool _wasEnabled;
    private bool _capabilityReported;

    public AutoSpellLevelController(
        AutomataConfig config,
        ReflectionSpellLevelRuntime runtime,
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
            "OrbAutomata.AutoSpellLevel",
            "Evaluate native spell leveling",
            SuiteBudgetClass.SoftLimited,
            SuiteWorkExecutionKind.Cooperative);
        _mutationWork = coordinator.Register(
            "OrbAutomata.AutoSpellLevel",
            "Level native spells",
            SuiteBudgetClass.HardLimited,
            SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
    }

    public AutoSpellLevelCapability Capability => _capability;

    public void Tick(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        var enabled = _config.CanStartAutoBuyActively && _config.AutoLevelSpells.Value;
        SetEnabled(enabled);
        if (!enabled)
        {
            _pending = null;
            _wasEnabled = false;
            return;
        }
        if (!_wasEnabled)
        {
            _secondsUntilEvaluation = 0.0f;
            _capabilityReported = false;
        }
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

    public void NotifyNativeChange() => _secondsUntilEvaluation = 0.0f;

    public void InvalidateLifecycle()
    {
        _runtime.InvalidateLifecycle();
        _pending = null;
        _capability = AutoSpellLevelCapability.Locked;
        _capabilityReported = false;
        _secondsUntilEvaluation = 0.0f;
    }

    private void Evaluate()
    {
        var snapshot = _runtime.ReadSnapshot(out var reason);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            LogFailure($"Auto Spell Leveling unavailable: {reason}");
            _secondsUntilEvaluation = 10.0f;
            return;
        }
        if (!_capabilityReported || _capability != snapshot.Capability)
        {
            _capability = snapshot.Capability;
            _capabilityReported = true;
            _log.LogAutomataInfo($"Auto Spell Leveling capability is {_capability}.");
        }
        if (snapshot.Candidate is null)
        {
            _secondsUntilEvaluation = _capability == AutoSpellLevelCapability.Locked ? 10.0f : 1.0f;
            return;
        }
        _pendingCapability = snapshot.Capability;
        _pending = snapshot.Candidate;
    }

    private void ExecuteMutation()
    {
        var candidate = _pending;
        var capability = _pendingCapability;
        _pending = null;
        if (candidate is null || !_config.CanStartAutoBuyActively || !_config.AutoLevelSpells.Value) return;
        var succeeded = capability == AutoSpellLevelCapability.All
            ? _runtime.TryLevelAll(out var reason)
            : _runtime.TryLevelSingle(candidate, out reason);
        if (!succeeded)
        {
            if (!string.IsNullOrWhiteSpace(reason)) LogFailure($"Auto Spell Leveling rejected: {reason}");
            _secondsUntilEvaluation = _runtime.BlockedReason is null ? 1.0f : 10.0f;
            return;
        }
        if (_config.IsOperationalLoggingEnabled)
            _log.LogAutomataInfo(capability == AutoSpellLevelCapability.All
                ? "Auto Spell Leveling completed the native level-all action."
                : $"Auto Spell Leveling raised {candidate.DisplayName} from mastery level {candidate.MasteryLevel}.");
        _secondsUntilEvaluation = 1.0f;
    }

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
    }
}
