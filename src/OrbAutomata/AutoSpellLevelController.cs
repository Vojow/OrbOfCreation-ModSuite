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
    private readonly AutomataFeatureStatusReporter? _featureStatus;
    private readonly Func<bool> _ownsActionFamily;
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
    private bool _postconditionFaulted;
    private NativeMutationCallOutcome _activeMutationOutcome;

    public AutoSpellLevelController(
        AutomataConfig config,
        ReflectionSpellLevelRuntime runtime,
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
        var evaluateIdentity = SuitePerformanceWorkIdentities.AutoSpellLevelEvaluate;
        _readWork = coordinator.Register(
            evaluateIdentity.Subsystem,
            evaluateIdentity.WorkName,
            evaluateIdentity.BudgetClass,
            evaluateIdentity.ExecutionKind);
        var mutationIdentity = SuitePerformanceWorkIdentities.AutoSpellLevelMutation;
        _mutationWork = coordinator.Register(
            mutationIdentity.Subsystem,
            mutationIdentity.WorkName,
            mutationIdentity.BudgetClass,
            mutationIdentity.ExecutionKind);
    }

    public AutoSpellLevelCapability Capability => _capability;

    public void Tick(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        var enabled = _config.CanStartAutoBuyActively && _config.AutoLevelSpells.Value;
        ObserveConfigurationStatus();
        if (enabled && !_ownsActionFamily())
        {
            _pending = null;
            SetEnabled(false);
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds the native spell-level purchase action family.");
            return;
        }
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
        _mutationWork.SetPending(_pending is not null);
    }

    public void NotifyNativeChange() => _secondsUntilEvaluation = 0.0f;

    public void InvalidateLifecycle()
    {
        _runtime.InvalidateLifecycle();
        _pending = null;
        _capability = AutoSpellLevelCapability.Locked;
        _capabilityReported = false;
        _postconditionFaulted = false;
        _secondsUntilEvaluation = 0.0f;
    }

    private void Evaluate()
    {
        var snapshot = _runtime.ReadSnapshot(out var reason);
        if (!string.IsNullOrWhiteSpace(reason))
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
                        ? "Native spell-level progression is not ready."
                        : "The native spell-level contract is unavailable.");
            }
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
        if (_capability == AutoSpellLevelCapability.Locked)
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.Locked,
                FeatureStatusReasonCode.ProgressionLocked,
                "Spell leveling has not been unlocked.");
        }
        else
        {
            _featureStatus?.ObserveOperational();
        }
        if (snapshot.Candidate is null)
        {
            _secondsUntilEvaluation = _capability == AutoSpellLevelCapability.Locked ? 10.0f : 1.0f;
            return;
        }
        _pendingCapability = snapshot.Capability;
        _pending = snapshot.Candidate;
    }

    private SuiteWorkCompletion ExecuteMutation()
    {
        var candidate = _pending;
        var capability = _pendingCapability;
        _pending = null;
        if (candidate is null || !_config.CanStartAutoBuyActively || !_config.AutoLevelSpells.Value)
            return NoMutationCompletion();
        if (!_ownsActionFamily())
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Spell-level ownership changed before mutation.");
            return NoMutationCompletion();
        }
        bool succeeded;
        string reason;
        try
        {
            succeeded = capability == AutoSpellLevelCapability.All
                ? _runtime.TryLevelAll(out reason)
                : _runtime.TryLevelSingle(candidate, out reason);
        }
        finally
        {
            _activeMutationOutcome = _runtime.LastNativeMutationOutcome;
        }
        if (!succeeded)
        {
            if (!string.IsNullOrWhiteSpace(reason)) LogFailure($"Auto Spell Leveling rejected: {reason}");
            _secondsUntilEvaluation = _runtime.BlockedReason is null ? 1.0f : 10.0f;
            if (_runtime.BlockedReason is not null)
            {
                _postconditionFaulted = true;
                ObservePostconditionFault();
            }
            else
            {
                _featureStatus?.Observe(
                    true,
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.TemporarySafetyBlock,
                    "Spell-level state changed before mutation; Automata will retry.");
            }
            return _activeMutationOutcome.ToWorkCompletion();
        }
        if (_config.IsOperationalLoggingEnabled)
            _log.LogAutomataInfo(capability == AutoSpellLevelCapability.All
                ? "Auto Spell Leveling completed the native level-all action."
                : $"Auto Spell Leveling raised {candidate.DisplayName} from mastery level {candidate.MasteryLevel}.");
        _secondsUntilEvaluation = 1.0f;
        _featureStatus?.ObserveOperational();
        return _activeMutationOutcome.ToWorkCompletion();
    }

    private static SuiteWorkCompletion NoMutationCompletion() => new(1);

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

    private void ObserveConfigurationStatus()
    {
        if (!_config.AutoLevelSpells.Value)
        {
            _featureStatus?.Observe(
                false,
                FeatureStatusState.ConfigurationDisabled,
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Spell Leveling is disabled by configuration.");
            return;
        }
        if (_config.AutoBuyMode.Value != AutoBuyOperationMode.Active)
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Auto Buy is disabled by configuration.");
            return;
        }
        if (!_config.CanStartAutoBuyActively)
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        }
    }

    private void ObservePostconditionFault() =>
        _featureStatus?.Observe(
            true,
            FeatureStatusState.Faulted,
            FeatureStatusReasonCode.PostconditionFailed,
            "A native spell-level mutation could not be verified and is blocked until lifecycle recovery.");

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

    internal void CancelPreparedWork()
    {
        _pending = null;
        _wasEnabled = false;
        _secondsUntilEvaluation = 0.0f;
        SetEnabled(false);
    }
}
