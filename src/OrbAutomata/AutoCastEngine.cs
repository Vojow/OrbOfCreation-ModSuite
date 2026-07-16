using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using OrbModding.Common;
using UnityEngine.SceneManagement;

namespace OrbAutomata;

internal sealed class AutoCastEngine : IDisposable
{
    private readonly AutomataConfig _config;
    private readonly IAutoCastCatalog _catalog;
    private readonly ReservePolicy _reservePolicy;
    private readonly ResourceFullnessPolicy _fullnessPolicy;
    private readonly ManualLogSource _log;
    private readonly Func<bool> _isGameplayScene;
    private readonly SuitePerformanceCoordinator? _coordinator;
    private readonly Func<long>? _readFrameIdentity;
    private readonly SuiteWorkRegistration? _readWork;
    private readonly SuiteWorkRegistration? _mutationWork;
    private readonly Dictionary<int, DecisionLogGate> _slotLogGates = new Dictionary<int, DecisionLogGate>();
    private readonly Dictionary<int, string> _activeChannels = new Dictionary<int, string>();
    private float _secondsUntilEvaluation;
    private float _manualPauseRemaining;
    private double _elapsedSeconds;
    private int _nextSlotIndex;
    private bool _operationalLoggingWasEnabled;
    private IAutoCastCandidate? _pendingCandidate;
    private string _pendingResourceSummary = string.Empty;

    public AutoCastEngine(
        AutomataConfig config,
        IAutoCastCatalog catalog,
        ReservePolicy reservePolicy,
        ResourceFullnessPolicy fullnessPolicy,
        ManualLogSource log,
        Func<bool>? isGameplayScene = null,
        SuitePerformanceCoordinator? coordinator = null,
        Func<long>? readFrameIdentity = null)
    {
        _config = config;
        _catalog = catalog;
        _reservePolicy = reservePolicy;
        _fullnessPolicy = fullnessPolicy;
        _log = log;
        _isGameplayScene = isGameplayScene ?? (() => SceneManager.GetActiveScene().name == "Main");
        _coordinator = coordinator;
        _readFrameIdentity = readFrameIdentity;
        if (coordinator is not null)
        {
            _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
            _readWork = coordinator.Register(
                "OrbAutomata.AutoCast",
                "Evaluate loadout",
                SuiteBudgetClass.SoftLimited,
                SuiteWorkExecutionKind.Cooperative);
            _mutationWork = coordinator.Register(
                "OrbAutomata.AutoCast",
                "Fire one spell",
                SuiteBudgetClass.HardLimited,
                SuiteWorkExecutionKind.NonPreemptibleNativeMutation);
        }
        _secondsUntilEvaluation = ClampInterval(config.AutoCastIntervalSeconds.Value);
        _operationalLoggingWasEnabled = config.EnableOperationalLogging.Value;
        AutoCastManualSignal.ManualSpellFired += OnManualSpellFired;
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
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        _manualPauseRemaining = Math.Max(0.0f, _manualPauseRemaining - elapsed);
        ResetDiagnosticStateWhenLoggingIsEnabled();

        if (_config.AutoCastMode.Value != AutoCastOperationMode.Active)
        {
            return;
        }

        _secondsUntilEvaluation -= elapsed;
        if (_secondsUntilEvaluation > 0.0f)
        {
            return;
        }

        _secondsUntilEvaluation = ClampInterval(_config.AutoCastIntervalSeconds.Value);
        Evaluate();
    }

    private void TickCoordinated(float unscaledDeltaTime)
    {
        var elapsed = Math.Max(0.0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        _manualPauseRemaining = Math.Max(0.0f, _manualPauseRemaining - elapsed);
        ResetDiagnosticStateWhenLoggingIsEnabled();

        if (_config.AutoCastMode.Value != AutoCastOperationMode.Active ||
            !_config.CanStartAutoCastActively)
        {
            ClearPendingCandidate();
            SetCoordinatorEnabled(false);
            return;
        }

        SetCoordinatorEnabled(true);
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
                _secondsUntilEvaluation = ClampInterval(_config.AutoCastIntervalSeconds.Value);
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
                FirePreparedCandidate();
                mutationLease.Complete();
            }
        }

        SetCoordinatorPending(!readCompleted && readDue, _pendingCandidate is not null);
    }

    private void PrepareCandidate()
    {
        ClearPendingCandidate();
        if (!_isGameplayScene() || _manualPauseRemaining > 0.0f || _catalog.IsTargeting())
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
            _log.LogError($"Auto Cast could not read the active loadout: {ex.Message}");
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
        if (activeChannels.Length > 0 || _catalog.IsNativeCastBusy())
        {
            return;
        }

        var start = NormalizeCursor(_nextSlotIndex, loadout.Count);
        for (var offset = 0; offset < loadout.Count; offset++)
        {
            var index = (start + offset) % loadout.Count;
            var candidate = loadout[index];
            if (!TryAdmit(candidate, out var reason, out var resourceSummary))
            {
                LogVerboseRejection(candidate, reason);
                continue;
            }

            _pendingCandidate = candidate;
            _pendingResourceSummary = resourceSummary;
            return;
        }
    }

    private void FirePreparedCandidate()
    {
        var candidate = _pendingCandidate;
        var resourceSummary = _pendingResourceSummary;
        if (candidate is null)
        {
            return;
        }

        try
        {
            if (!_isGameplayScene() ||
                _manualPauseRemaining > 0.0f ||
                _catalog.IsTargeting() ||
                !_config.CanStartAutoCastActively ||
                _catalog.IsNativeCastBusy())
            {
                return;
            }

            if (!TryAdmit(candidate, out var reason, out resourceSummary))
            {
                LogVerboseRejection(candidate, reason);
                return;
            }

            if (!candidate.TryFireAndResolveTargets(out reason))
            {
                _log.LogWarning($"Auto Cast could not fire slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}");
                return;
            }

            MarkSlotState(candidate, "active fired");
            LogOperation(
                $"Auto Cast fired slot {candidate.SlotIndex + 1}: " +
                $"{candidate.DisplayName} [{candidate.Kind}]; {resourceSummary}.");
            _nextSlotIndex = candidate.SlotIndex + 1;
        }
        finally
        {
            ClearPendingCandidate();
        }
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
        _pendingResourceSummary = string.Empty;
    }

    public void Dispose()
    {
        AutoCastManualSignal.ManualSpellFired -= OnManualSpellFired;
        ClearPendingCandidate();
        SetCoordinatorEnabled(false);
        _readWork?.Dispose();
        _mutationWork?.Dispose();
        _catalog.Dispose();
    }

    private void Evaluate()
    {
        if (!_isGameplayScene() || _manualPauseRemaining > 0.0f || _catalog.IsTargeting())
        {
            return;
        }

        if (!_config.CanStartAutoCastActively)
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
            _log.LogError($"Auto Cast could not read the active loadout: {ex.Message}");
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
            if (!TryAdmit(candidate, out var reason, out var resourceSummary))
            {
                LogVerboseRejection(candidate, reason);
                continue;
            }

            if (!candidate.TryFireAndResolveTargets(out reason))
            {
                _log.LogWarning($"Auto Cast could not fire slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}");
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

    private bool TryAdmit(IAutoCastCandidate candidate, out string reason, out string resourceSummary)
    {
        resourceSummary = string.Empty;
        if (candidate.IsEmpty)
        {
            reason = "empty slot";
            return false;
        }

        if (candidate.IsCharged)
        {
            reason = "charged spells are deferred";
            return false;
        }

        if (candidate.IsCasting)
        {
            reason = candidate.Kind == AutoCastSpellKind.Aura ? "aura already active" : "already casting";
            return false;
        }

        if (!candidate.CanCast(out reason) ||
            !candidate.TryGetImmediateCosts(out var immediateCosts) ||
            !candidate.TryGetDrainCosts(out var drainCosts))
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "native readiness or costs unavailable";
            }

            return false;
        }

        resourceSummary = _fullnessPolicy.Describe(
            immediateCosts,
            drainCosts,
            _config.AutoCastStartResourcePercent.Value);

        var positiveImmediateCosts = immediateCosts.Where(cost => !cost.Cost.IsZero).ToArray();
        if (positiveImmediateCosts.Length > 0)
        {
            var reserve = _reservePolicy.Evaluate(positiveImmediateCosts);
            if (!reserve.Passed)
            {
                reason = reserve.Reason;
                return false;
            }
        }

        if (!_fullnessPolicy.Evaluate(immediateCosts, drainCosts, _config.AutoCastStartResourcePercent.Value, out reason))
        {
            return false;
        }

        return candidate.HasValidTargets(out reason);
    }

    private void OnManualSpellFired()
    {
        _manualPauseRemaining = Math.Max(0.0f, Math.Min(60.0f, _config.AutoCastManualPauseSeconds.Value));
        ClearPendingCandidate();
        _mutationWork?.SetPending(false);
    }

    private void LogOperation(string message)
    {
        if (_config.EnableOperationalLogging.Value)
        {
            _log.LogInfo(message);
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

        if (_config.EnableOperationalLogging.Value &&
            _config.DecisionLogLevel.Value == AutomataDecisionLogLevel.Verbose &&
            ShouldLogSlotState(candidate, state))
        {
            _log.LogInfo($"Auto Cast skipped slot {candidate.SlotIndex + 1}, {candidate.DisplayName}: {reason}.");
        }
    }

    private void UpdateChannelLifecycle(IReadOnlyList<IAutoCastCandidate> activeChannels)
    {
        var current = activeChannels.ToDictionary(channel => channel.SlotIndex, channel => channel.DisplayName);
        foreach (var ended in _activeChannels.Where(channel => !current.ContainsKey(channel.Key)).ToArray())
        {
            LogOperation($"Auto Cast channel ended: slot {ended.Key + 1}, {ended.Value}; rotation resumed.");
        }

        foreach (var started in current.Where(channel => !_activeChannels.ContainsKey(channel.Key)))
        {
            LogOperation($"Auto Cast channel active: slot {started.Key + 1}, {started.Value}; rotation paused.");
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
        var enabled = _config.EnableOperationalLogging.Value;
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
