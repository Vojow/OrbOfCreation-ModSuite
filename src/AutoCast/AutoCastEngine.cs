using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using UnityEngine.SceneManagement;

namespace OrbAutomata;

internal sealed class AutoCastEngine : IAutomataService
{
    private readonly IAutomataConfigurationSource _config;
    private readonly IAutoCastCatalog _catalog;
    private readonly ReservePolicy _reservePolicy;
    private readonly ResourceFullnessPolicy _fullnessPolicy;
    private readonly ManualLogSource _log;
    private readonly Func<bool> _isGameplayScene;
    private readonly SuitePerformanceCoordinator? _coordinator;
    private readonly Func<long>? _readFrameIdentity;
    private readonly AutomataFeatureStatusReporter? _featureStatus;
    private readonly Func<bool> _ownsActionFamily;
    private readonly SuiteWorkRegistration? _readWork;
    private readonly SuiteWorkRegistration? _mutationWork;
    private readonly DecisionLogGate _operationLogGate = new DecisionLogGate(TimeSpan.FromSeconds(30));
    private readonly Dictionary<int, DecisionLogGate> _slotLogGates = new Dictionary<int, DecisionLogGate>();
    private readonly Dictionary<int, string> _activeChannels = new Dictionary<int, string>();
    private float _secondsUntilEvaluation;
    private float _manualPauseRemaining;
    private double _elapsedSeconds;
    private int _nextSlotIndex;
    private bool _operationalLoggingWasEnabled;
    private IAutoCastCandidate? _pendingCandidate;
    private AutoCastCandidateIdentity _pendingIdentity;
    private NativeMutationCallOutcome _activeMutationOutcome;
    private string _pendingResourceSummary = string.Empty;
    private IAutoCastCandidate? _fullChargeCandidate;

    public AutoCastEngine(
        IAutomataConfigurationSource config,
        IAutoCastCatalog catalog,
        ReservePolicy reservePolicy,
        ResourceFullnessPolicy fullnessPolicy,
        ManualLogSource log,
        Func<bool>? isGameplayScene = null,
        SuitePerformanceCoordinator? coordinator = null,
        Func<long>? readFrameIdentity = null,
        AutomataFeatureStatusReporter? featureStatus = null,
        Func<bool>? ownsActionFamily = null)
    {
        _config = config;
        _catalog = catalog;
        _reservePolicy = reservePolicy;
        _fullnessPolicy = fullnessPolicy;
        _log = log;
        _isGameplayScene = isGameplayScene ?? (() => SceneManager.GetActiveScene().name == "Main");
        _coordinator = coordinator;
        _readFrameIdentity = readFrameIdentity;
        _featureStatus = featureStatus;
        _ownsActionFamily = ownsActionFamily ?? (() => true);
        if (coordinator is not null)
        {
            _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
            var evaluateIdentity = SuitePerformanceWorkIdentities.AutoCastEvaluate;
            _readWork = coordinator.Register(
                evaluateIdentity.Subsystem,
                evaluateIdentity.WorkName,
                evaluateIdentity.BudgetClass,
                evaluateIdentity.ExecutionKind);
            var mutationIdentity = SuitePerformanceWorkIdentities.AutoCastMutation;
            _mutationWork = coordinator.Register(
                mutationIdentity.Subsystem,
                mutationIdentity.WorkName,
                mutationIdentity.BudgetClass,
                mutationIdentity.ExecutionKind);
        }
        _secondsUntilEvaluation = ClampInterval(config.Current.AutoCast.EvaluationIntervalSeconds);
        _operationalLoggingWasEnabled = config.Current.Diagnostics.IsOperationalLoggingEnabled;
        AutoCastManualSignal.ManualSpellFired += OnManualSpellFired;
    }

    private SuiteRuntimeConfiguration Config => _config.Current;

    public void Tick(float unscaledDeltaTime)
    {
        if (Config.AutoCast.Mode == AutoCastOperationMode.Active && !_ownsActionFamily())
        {
            ReleaseFullChargeHold("action-family ownership lost");
            ClearPendingCandidate();
            SetCoordinatorEnabled(false);
            _featureStatus?.Observe(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds the native spell-cast action family.");
            return;
        }
        if (_coordinator is null)
        {
            TickLegacy(unscaledDeltaTime);
            return;
        }

        TickCoordinated(unscaledDeltaTime);
    }

    private void TickLegacy(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        _manualPauseRemaining = Math.Max(0.0f, _manualPauseRemaining - elapsed);
        ObserveTickStatus();
        ResetDiagnosticStateWhenLoggingIsEnabled();

        if (!MaintainFullChargeHold())
        {
            return;
        }

        if (Config.AutoCast.Mode != AutoCastOperationMode.Active)
        {
            return;
        }

        _secondsUntilEvaluation -= elapsed;
        if (_secondsUntilEvaluation > 0.0f)
        {
            return;
        }

        _secondsUntilEvaluation = ClampInterval(Config.AutoCast.EvaluationIntervalSeconds);
        Evaluate();
    }

    private void TickCoordinated(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        _manualPauseRemaining = Math.Max(0.0f, _manualPauseRemaining - elapsed);
        ObserveTickStatus();
        ResetDiagnosticStateWhenLoggingIsEnabled();

        if (Config.AutoCast.Mode != AutoCastOperationMode.Active ||
            !Config.CanStartAutoCastActively)
        {
            ReleaseFullChargeHold("Auto Cast stopped");
            ClearPendingCandidate();
            SetCoordinatorEnabled(false);
            return;
        }

        SetCoordinatorEnabled(true);
        if (_fullChargeCandidate is not null)
        {
            if (!Config.AutoCast.FullCharge || !_isGameplayScene())
            {
                ReleaseFullChargeHold("full-charge mode or gameplay stopped");
                SetCoordinatorPending(false, false);
                return;
            }

            if (_fullChargeCandidate.IsReadyingCast)
            {
                SetCoordinatorPending(false, false);
                return;
            }

            SetCoordinatorPending(false, true);
            if (TryAcquire(_mutationWork, out var releaseLease))
            {
                using (releaseLease)
                {
                    _activeMutationOutcome = default;
                    try
                    {
                        releaseLease.Complete(ReleaseFullChargeHold("charge completed"));
                    }
                    catch
                    {
                        releaseLease.Fail(_activeMutationOutcome.ToWorkCompletion());
                        throw;
                    }
                }
            }

            SetCoordinatorPending(false, _fullChargeCandidate is not null);
            return;
        }

        var readDue = _pendingCandidate is null && _secondsUntilEvaluation <= 0.0f;
        if (_pendingCandidate is null && !readDue)
        {
            _secondsUntilEvaluation -= elapsed;
            readDue = _secondsUntilEvaluation <= 0.0f;
        }

        SetCoordinatorPending(readDue, _pendingCandidate is not null);
        var readCompleted = false;
        if (readDue && TryAcquire(_readWork, out var readLease))
        {
            using (readLease)
            {
                _secondsUntilEvaluation = ClampInterval(Config.AutoCast.EvaluationIntervalSeconds);
                PrepareCandidate();
                readLease.Complete();
                readCompleted = true;
            }
        }

        SetCoordinatorPending(!readCompleted && readDue, _pendingCandidate is not null);
        if (_pendingCandidate is not null && TryAcquire(_mutationWork, out var mutationLease))
        {
            using (mutationLease)
            {
                _activeMutationOutcome = default;
                try
                {
                    mutationLease.Complete(FirePreparedCandidate());
                }
                catch
                {
                    mutationLease.Fail(_activeMutationOutcome.ToWorkCompletion());
                    throw;
                }
            }
        }

        SetCoordinatorPending(!readCompleted && readDue, _pendingCandidate is not null);
    }

    private void PrepareCandidate()
    {
        ClearPendingCandidate();
        if (!_isGameplayScene() || _manualPauseRemaining > 0.0f)
        {
            return;
        }
        if (_catalog.IsTargeting())
        {
            ObserveTemporaryBlock(FeatureStatusReasonCode.TargetingInProgress, "Native spell targeting is in progress.");
            return;
        }

        IReadOnlyList<IAutoCastCandidate> loadout;
        try
        {
            loadout = _catalog.DiscoverActiveLoadout();
        }
        catch (Exception ex)
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Auto Cast could not read the active loadout.");
            _log.LogAutomataError($"Auto Cast could not read the active loadout: {ex.Message}");
            return;
        }

        if (loadout.Count == 0)
        {
            _featureStatus?.ObserveOperational();
            return;
        }

        var activeChannels = loadout
            .Where(spell => spell.Kind == AutoCastSpellKind.Channel && spell.IsCasting)
            .ToArray();
        UpdateChannelLifecycle(activeChannels);
        if (activeChannels.Length > 0 || _catalog.IsNativeCastBusy())
        {
            ObserveTemporaryBlock(FeatureStatusReasonCode.NativeBusy, "The native spell system is busy.");
            return;
        }

        var start = NormalizeCursor(_nextSlotIndex, loadout.Count);
        var sawContractFailure = false;
        for (var offset = 0; offset < loadout.Count; offset++)
        {
            var index = (start + offset) % loadout.Count;
            var candidate = loadout[index];
            if (!TryAdmit(candidate, out var reason, out var resourceSummary, out var failureKind))
            {
                sawContractFailure |= failureKind == AutoCastAdmissionFailureKind.ContractUnavailable;
                LogVerboseRejection(candidate, reason);
                continue;
            }

            if (candidate.TryGetIdentity(out var identity, out reason))
            {
                _pendingCandidate = candidate;
                _pendingIdentity = identity;
                _pendingResourceSummary = resourceSummary;
                _featureStatus?.ObserveOperational();
                return;
            }

            sawContractFailure = true;
            LogVerboseRejection(candidate, reason);
        }

        if (sawContractFailure)
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "One or more equipped spell contracts are unavailable; other slots remain eligible.");
        }
        else
        {
            _featureStatus?.ObserveOperational();
        }
    }

    private SuiteWorkCompletion FirePreparedCandidate()
    {
        var candidate = _pendingCandidate;
        var resourceSummary = _pendingResourceSummary;
        var chargeHoldAcquired = false;
        var fireSucceeded = false;
        if (candidate is null)
        {
            return new SuiteWorkCompletion(1);
        }

        try
        {
            if (!_isGameplayScene() ||
                _manualPauseRemaining > 0.0f ||
                _catalog.IsTargeting() ||
                !Config.CanStartAutoCastActively ||
                _catalog.IsNativeCastBusy())
            {
                goto Complete;
            }

            if (!TryResolvePreparedCandidate(out candidate, out var identityReason))
            {
                _featureStatus?.Observe(
                    true,
                    FeatureStatusState.Degraded,
                    FeatureStatusReasonCode.IdentityMismatch,
                    "A prepared spell identity changed before casting.");
                _secondsUntilEvaluation = 0.0f;
                LogVerboseRejection(_pendingCandidate!, identityReason);
                goto Complete;
            }

            if (!TryAdmit(candidate, out var reason, out resourceSummary, out _))
            {
                LogVerboseRejection(candidate, reason);
                goto Complete;
            }

            if (!_ownsActionFamily())
            {
                _featureStatus?.Observe(
                    true,
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.ActionFamilyConflict,
                    "Spell-cast ownership changed before mutation.");
                goto Complete;
            }

            var shouldFullCharge = candidate.IsCharged && Config.AutoCast.FullCharge;
            if (shouldFullCharge)
            {
                bool held;
                try
                {
                    held = candidate.TrySetChargeHold(true, out reason);
                }
                catch
                {
                    _activeMutationOutcome = _activeMutationOutcome.Add(
                        ReadMutationOutcome(candidate, succeeded: false));
                    throw;
                }
                _activeMutationOutcome = _activeMutationOutcome.Add(
                    ReadMutationOutcome(candidate, held));
                if (!held)
                {
                    ObserveMutationFailure("A charged-spell hold could not be established.");
                    _log.LogAutomataWarning($"Auto Cast could not hold slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}");
                    goto Complete;
                }
            }

            if (shouldFullCharge)
            {
                _fullChargeCandidate = candidate;
                chargeHoldAcquired = true;
            }

            if (!_ownsActionFamily())
            {
                _featureStatus?.Observe(
                    true,
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.ActionFamilyConflict,
                    "Spell-cast ownership changed before firing.");
                goto Complete;
            }

            bool fired;
            try
            {
                fired = candidate.TryFireAndResolveTargets(out reason);
            }
            catch
            {
                _activeMutationOutcome = _activeMutationOutcome.Add(
                    ReadMutationOutcome(candidate, succeeded: false));
                throw;
            }
            _activeMutationOutcome = _activeMutationOutcome.Add(
                ReadMutationOutcome(candidate, fired));
            if (!fired)
            {
                ObserveMutationFailure("An equipped spell mutation failed; other slots remain eligible.");
                _log.LogAutomataWarning($"Auto Cast could not fire slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}");
                goto Complete;
            }

            fireSucceeded = true;
            _featureStatus?.ObserveOperational();

            MarkSlotState(candidate, "active fired");
            LogOperation(
                $"Auto Cast fired slot {candidate.SlotIndex + 1}: " +
                $"{candidate.DisplayName} [{candidate.Kind}]; {resourceSummary}.");
            _nextSlotIndex = candidate.SlotIndex + 1;
        }
        finally
        {
            if (chargeHoldAcquired && !fireSucceeded)
            {
                ReleaseFullChargeHold("native fire or target resolution failed");
            }
            ClearPendingCandidate();
        }

Complete:
        return _activeMutationOutcome.ToWorkCompletion();
    }

    private bool TryResolvePreparedCandidate(out IAutoCastCandidate candidate, out string reason)
    {
        candidate = null!;
        IReadOnlyList<IAutoCastCandidate> loadout;
        try
        {
            loadout = _catalog.DiscoverActiveLoadout();
        }
        catch (Exception ex)
        {
            reason = $"active loadout refresh failed: {ex.Message}";
            return false;
        }

        for (var index = 0; index < loadout.Count; index++)
        {
            var current = loadout[index];
            if (current.SlotIndex != _pendingIdentity.SlotIndex)
            {
                continue;
            }

            if (!current.TryGetIdentity(out var identity, out reason))
            {
                return false;
            }

            if (!_pendingIdentity.Matches(identity))
            {
                reason = "prepared spell identity changed before mutation";
                return false;
            }

            candidate = current;
            reason = string.Empty;
            return true;
        }

        reason = "prepared spell slot is no longer equipped";
        return false;
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

    private void ClearPendingCandidate()
    {
        _pendingCandidate = null;
        _pendingIdentity = default;
        _pendingResourceSummary = string.Empty;
    }

    public void Dispose()
    {
        AutoCastManualSignal.ManualSpellFired -= OnManualSpellFired;
        ReleaseFullChargeHold("Auto Cast disposed");
        ClearPendingCandidate();
        SetCoordinatorEnabled(false);
        _readWork?.Dispose();
        _mutationWork?.Dispose();
        _catalog.Dispose();
    }

    public void CancelPreparedWork()
    {
        ReleaseFullChargeHold("automation ownership released");
        ClearPendingCandidate();
        SetCoordinatorEnabled(false);
        _secondsUntilEvaluation = 0.0f;
    }

    public void InvalidateLifecycle()
    {
        ReleaseFullChargeHold("Auto Cast lifecycle invalidated");
        (_catalog as IAutoCastMutationRecoveryCatalog)?.RecoverMutationBlocks();
        ClearPendingCandidate();
        SetCoordinatorPending(false, false);
        _secondsUntilEvaluation = 0.0f;
    }

    private void Evaluate()
    {
        if (!_isGameplayScene() || _manualPauseRemaining > 0.0f || _catalog.IsTargeting())
        {
            return;
        }

        if (!Config.CanStartAutoCastActively)
        {
            return;
        }

        IReadOnlyList<IAutoCastCandidate> loadout;
        try
        {
            loadout = _catalog.DiscoverActiveLoadout();
        }
        catch (Exception ex)
        {
            _log.LogAutomataError($"Auto Cast could not read the active loadout: {ex.Message}");
            return;
        }

        if (loadout.Count == 0)
        {
            return;
        }

        var activeChannels = loadout
            .Where(spell => spell.Kind == AutoCastSpellKind.Channel && spell.IsCasting)
            .ToArray();
        UpdateChannelLifecycle(activeChannels);
        if (activeChannels.Length > 0)
        {
            return;
        }

        if (_catalog.IsNativeCastBusy())
        {
            return;
        }

        var start = NormalizeCursor(_nextSlotIndex, loadout.Count);
        for (var offset = 0; offset < loadout.Count; offset++)
        {
            var index = (start + offset) % loadout.Count;
            var candidate = loadout[index];
            if (!TryAdmit(candidate, out var reason, out var resourceSummary, out _))
            {
                LogVerboseRejection(candidate, reason);
                continue;
            }

            if (!_ownsActionFamily())
            {
                _featureStatus?.Observe(
                    true,
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.ActionFamilyConflict,
                    "Spell-cast ownership changed before mutation.");
                return;
            }

            var shouldFullCharge = candidate.IsCharged && Config.AutoCast.FullCharge;
            if (shouldFullCharge && !candidate.TrySetChargeHold(true, out reason))
            {
                _log.LogAutomataWarning($"Auto Cast could not hold slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}");
                return;
            }

            if (shouldFullCharge)
            {
                _fullChargeCandidate = candidate;
            }

            if (!_ownsActionFamily())
            {
                if (shouldFullCharge) ReleaseFullChargeHold("action-family ownership lost before firing");
                _featureStatus?.Observe(
                    true,
                    FeatureStatusState.TemporarilyBlocked,
                    FeatureStatusReasonCode.ActionFamilyConflict,
                    "Spell-cast ownership changed before firing.");
                return;
            }

            if (!candidate.TryFireAndResolveTargets(out reason))
            {
                if (shouldFullCharge)
                {
                    ReleaseFullChargeHold("native fire or target resolution failed");
                }
                _log.LogAutomataWarning($"Auto Cast could not fire slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}");
                return;
            }

            MarkSlotState(candidate, "active fired");
            LogOperation(
                $"Auto Cast fired slot {candidate.SlotIndex + 1}: " +
                $"{candidate.DisplayName} [{candidate.Kind}]; {resourceSummary}.");
            _nextSlotIndex = (index + 1) % loadout.Count;
            return;
        }
    }

    private bool TryAdmit(
        IAutoCastCandidate candidate,
        out string reason,
        out string resourceSummary,
        out AutoCastAdmissionFailureKind failureKind)
    {
        resourceSummary = string.Empty;
        failureKind = AutoCastAdmissionFailureKind.None;
        var admission = AutoCastAdmissionAdapter.Capture(candidate);
        if (!admission.IsAvailable)
        {
            reason = admission.AvailabilityReason;
            failureKind = AutoCastAdmissionFailureKind.OrdinaryRejection;
            return false;
        }

        if (!AutomationAdmissionPolicy.HasCompleteContract(admission, out reason))
        {
            if (!string.IsNullOrWhiteSpace(admission.NativeAdmissionReason))
            {
                reason = admission.NativeAdmissionReason;
            }

            failureKind = AutoCastAdmissionFailureKind.ContractUnavailable;
            return false;
        }

        if (!admission.NativeAdmissionAccepted)
        {
            reason = admission.NativeAdmissionReason;
            failureKind = ReadAdmissionFailure(candidate);
            return false;
        }

        var immediateCosts = admission.ImmediateCosts;
        var drainCosts = admission.DrainCosts;
        resourceSummary = _fullnessPolicy.Describe(
            immediateCosts,
            drainCosts,
            Config.AutoCast.StartResourcePercent);

        var positiveImmediateCosts = immediateCosts.Where(cost => !cost.Cost.IsZero).ToArray();
        if (positiveImmediateCosts.Length > 0)
        {
            var reserve = _reservePolicy.Evaluate(positiveImmediateCosts);
            if (!reserve.Passed)
            {
                reason = reserve.Reason;
                failureKind = AutoCastAdmissionFailureKind.OrdinaryRejection;
                return false;
            }
        }

        if (!_fullnessPolicy.Evaluate(immediateCosts, drainCosts, Config.AutoCast.StartResourcePercent, out reason))
        {
            failureKind = AutoCastAdmissionFailureKind.OrdinaryRejection;
            return false;
        }

        if (!AutoCastAdmissionAdapter.TryValidateTargets(candidate, out reason, out failureKind))
        {
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void OnManualSpellFired()
    {
        ReleaseFullChargeHold("manual spell input");
        _manualPauseRemaining = Math.Max(0.0f, Math.Min(60.0f, Config.AutoCast.ManualPauseSeconds));
        if (_manualPauseRemaining > 0.0f)
            ObserveTemporaryBlock(FeatureStatusReasonCode.ManualPause, "Auto Cast is paused after manual spell input.");
        ClearPendingCandidate();
        _mutationWork?.SetPending(false);
    }

    private void ObserveTickStatus()
    {
        if (Config.AutoCast.Mode != AutoCastOperationMode.Active)
        {
            _featureStatus?.Observe(
                false,
                FeatureStatusState.ConfigurationDisabled,
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Auto Cast is disabled by configuration.");
            return;
        }
        if (!Config.CanStartAutoCastActively)
        {
            ObserveTemporaryBlock(FeatureStatusReasonCode.EmergencyDisabled, "Automata Emergency Disable is active.");
            return;
        }
        if (!_isGameplayScene())
        {
            _featureStatus?.Observe(
                true,
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Gameplay lifecycle is not ready.");
            return;
        }
        if (_manualPauseRemaining > 0.0f)
        {
            ObserveTemporaryBlock(FeatureStatusReasonCode.ManualPause, "Auto Cast is paused after manual spell input.");
            return;
        }

        var currentReason = _featureStatus?.Current.Reason.Code;
        if (currentReason is FeatureStatusReasonCode.ConfigurationDisabled or
            FeatureStatusReasonCode.EmergencyDisabled or
            FeatureStatusReasonCode.GameplayNotReady or
            FeatureStatusReasonCode.ManualPause)
            _featureStatus?.ObserveOperational();
    }

    private void ObserveTemporaryBlock(FeatureStatusReasonCode code, string summary) =>
        _featureStatus?.Observe(true, FeatureStatusState.TemporarilyBlocked, code, summary);

    private void ObserveMutationFailure(string summary) =>
        _featureStatus?.Observe(
            true,
            FeatureStatusState.Degraded,
            FeatureStatusReasonCode.NativeMutationFailed,
            summary);

    private static AutoCastAdmissionFailureKind ReadAdmissionFailure(IAutoCastCandidate candidate) =>
        candidate is IAutoCastAdmissionFailureEvidence evidence &&
        evidence.LastAdmissionFailure != AutoCastAdmissionFailureKind.None
            ? evidence.LastAdmissionFailure
            : AutoCastAdmissionFailureKind.OrdinaryRejection;

    private bool MaintainFullChargeHold()
    {
        if (_fullChargeCandidate is null)
        {
            return true;
        }

        if (_isGameplayScene() &&
            Config.CanStartAutoCastActively &&
            Config.AutoCast.FullCharge &&
            _fullChargeCandidate.IsReadyingCast)
        {
            return false;
        }

        ReleaseFullChargeHold("charge completed or Auto Cast stopped");
        return true;
    }

    private SuiteWorkCompletion ReleaseFullChargeHold(string context)
    {
        var candidate = _fullChargeCandidate;
        _fullChargeCandidate = null;
        if (candidate is null)
        {
            return new SuiteWorkCompletion(1);
        }

        bool succeeded;
        string reason;
        try
        {
            succeeded = candidate.TrySetChargeHold(false, out reason);
        }
        catch
        {
            _activeMutationOutcome = _activeMutationOutcome.Add(
                ReadMutationOutcome(candidate, succeeded: false));
            throw;
        }
        var outcome = ReadMutationOutcome(candidate, succeeded);
        _activeMutationOutcome = _activeMutationOutcome.Add(outcome);
        if (!succeeded)
        {
            _log.LogAutomataWarning($"Auto Cast could not release full-charge hold for slot {candidate.SlotIndex + 1}, {candidate.DisplayName} ({context}): {reason}");
        }

        return outcome.ToWorkCompletion();
    }

    private static NativeMutationCallOutcome ReadMutationOutcome(
        IAutoCastCandidate candidate,
        bool succeeded) =>
        candidate is INativeMutationOutcomeSource source
            ? source.LastNativeMutationOutcome
            : new NativeMutationCallOutcome(1, 1, succeeded ? 1 : 0);

    private void LogOperation(string message)
    {
        if (!Config.Diagnostics.IsOperationalLoggingEnabled)
        {
            return;
        }

        if (Config.Diagnostics.DecisionLogLevel == SuiteDecisionLogLevel.Verbose ||
            _operationLogGate.ShouldLog("autocast-operation", TimeSpan.FromSeconds(_elapsedSeconds)))
        {
            _log.LogAutomataInfo(message);
        }
    }

    private void LogStateTransition(string message)
    {
        if (Config.Diagnostics.IsOperationalLoggingEnabled)
        {
            _log.LogAutomataInfo(message);
        }
    }

    private void LogVerboseRejection(IAutoCastCandidate candidate, string reason)
    {
        var state = RejectionState(reason);
        if (string.Equals(state, "empty slot", StringComparison.Ordinal))
        {
            MarkSlotState(candidate, state);
            return;
        }

        if (Config.Diagnostics.IsOperationalLoggingEnabled &&
            Config.Diagnostics.DecisionLogLevel == SuiteDecisionLogLevel.Verbose &&
            ShouldLogSlotState(candidate, state))
        {
            _log.LogAutomataInfo($"Auto Cast skipped slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}.");
        }
    }

    private void UpdateChannelLifecycle(IReadOnlyList<IAutoCastCandidate> activeChannels)
    {
        var current = activeChannels.ToDictionary(channel => channel.SlotIndex, channel => channel.DisplayName);
        foreach (var ended in _activeChannels.Where(channel => !current.ContainsKey(channel.Key)).ToArray())
        {
            LogStateTransition($"Auto Cast channel ended: slot {ended.Key + 1}, {ended.Value}; rotation resumed.");
        }

        foreach (var started in current.Where(channel => !_activeChannels.ContainsKey(channel.Key)))
        {
            LogStateTransition($"Auto Cast channel active: slot {started.Key + 1}, {started.Value}; rotation paused.");
        }

        _activeChannels.Clear();
        foreach (var channel in current)
        {
            _activeChannels[channel.Key] = channel.Value;
        }
    }

    private bool ShouldLogSlotState(IAutoCastCandidate candidate, string state)
    {
        if (!_slotLogGates.TryGetValue(candidate.SlotIndex, out var gate))
        {
            gate = new DecisionLogGate(TimeSpan.FromSeconds(30));
            _slotLogGates[candidate.SlotIndex] = gate;
        }

        return gate.ShouldLog(
            $"{candidate.DisplayName}:{state}",
            TimeSpan.FromSeconds(_elapsedSeconds));
    }

    private void MarkSlotState(IAutoCastCandidate candidate, string state)
    {
        ShouldLogSlotState(candidate, state);
    }

    private void ResetDiagnosticStateWhenLoggingIsEnabled()
    {
        var enabled = Config.Diagnostics.IsOperationalLoggingEnabled;
        if (enabled && !_operationalLoggingWasEnabled)
        {
            _slotLogGates.Clear();
            _activeChannels.Clear();
        }

        _operationalLoggingWasEnabled = enabled;
    }

    private static string RejectionState(string reason)
    {
        var detailSeparator = reason.IndexOf(':');
        return detailSeparator > 0 ? reason.Substring(0, detailSeparator) : reason;
    }

    private static int NormalizeCursor(int cursor, int count) => count <= 0 ? 0 : Math.Max(0, cursor) % count;

    private static float ClampInterval(float value) => Math.Max(0.1f, Math.Min(10.0f, value));
}
