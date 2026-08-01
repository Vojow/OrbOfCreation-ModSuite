#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata.GameMcp;

internal enum GameMcpCommandKind
{
    Purchase = 1,
    Cast = 2,
    Concept = 3,
    Harvest = 4,
    SpellLevel = 5,
    ConfigurationSet = 6,
    EmergencyStop = 7,
    Screenshot = 8,
    Navigation = 9,
    Probe = 10,
    ScreenCatalog = 11,
    TooltipCatalog = 12,
    TooltipRead = 13,
    ContinueRun = 14,
    ChronicleStart = 15,
    ChroniclePause = 16,
    ChronicleResume = 17,
    ChronicleAbandon = 18,
    ChronicleSelectComparison = 19,
}

/// <summary>
/// One immutable request copied off an HTTP worker and consumed on Unity's main thread.
/// No JSON token, game object, native reference, or mutable configuration crosses this seam.
/// </summary>
internal sealed class GameMcpCommand
{
    internal GameMcpCommand(
        long sequence,
        GameMcpCommandKind kind,
        ulong? decisionWorldGeneration,
        long expectedLifecycleGeneration,
        ulong expectedConfigurationGeneration,
        string mode,
        Guid targetId,
        Guid secondaryId,
        string derivedNativeType,
        string expectedNativeType,
        int amount,
        string payloadKey,
        string payloadValue,
        bool capture,
        bool saveCapture)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        var nativeAction = kind is >= GameMcpCommandKind.Purchase and <= GameMcpCommandKind.SpellLevel;
        if (nativeAction && expectedLifecycleGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLifecycleGeneration));
        if ((nativeAction || kind is GameMcpCommandKind.ConfigurationSet or GameMcpCommandKind.EmergencyStop) &&
            expectedConfigurationGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(expectedConfigurationGeneration));
        if (string.IsNullOrWhiteSpace(mode)) throw new ArgumentException("A mode is required.", nameof(mode));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        Sequence = sequence;
        Kind = kind;
        DecisionWorldGeneration = decisionWorldGeneration;
        ExpectedLifecycleGeneration = expectedLifecycleGeneration;
        ExpectedConfigurationGeneration = expectedConfigurationGeneration;
        Mode = mode;
        TargetId = targetId;
        SecondaryId = secondaryId;
        DerivedNativeType = derivedNativeType ?? string.Empty;
        ExpectedNativeType = expectedNativeType ?? string.Empty;
        Amount = amount;
        PayloadKey = payloadKey ?? string.Empty;
        PayloadValue = payloadValue ?? string.Empty;
        Capture = capture;
        SaveCapture = saveCapture;
        SubmittedAtUtcTicks = DateTime.UtcNow.Ticks;
        Completion = new GameMcpCommandCompletion();
    }

    internal long Sequence { get; }
    internal GameMcpCommandKind Kind { get; }
    internal ulong? DecisionWorldGeneration { get; }
    internal long ExpectedLifecycleGeneration { get; }
    internal ulong ExpectedConfigurationGeneration { get; }
    internal string Mode { get; }
    internal Guid TargetId { get; }
    internal Guid SecondaryId { get; }
    internal string DerivedNativeType { get; }
    internal string ExpectedNativeType { get; }
    internal int Amount { get; }
    internal string PayloadKey { get; }
    internal string PayloadValue { get; }
    internal bool Capture { get; }
    internal bool SaveCapture { get; }
    internal long SubmittedAtUtcTicks { get; }
    internal GameMcpCommandCompletion Completion { get; }
}

internal sealed class GameMcpCommandCompletion
{
    private readonly object _sync = new();
    private GameMcpCommandResult? _result;

    internal bool TryWait(TimeSpan timeout, out GameMcpCommandResult result)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var deadline = DateTime.UtcNow + timeout;
        lock (_sync)
        {
            while (_result is null)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_sync, remaining))
                {
                    result = null!;
                    return false;
                }
            }
            result = _result;
            return true;
        }
    }

    internal void Complete(GameMcpCommandResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        lock (_sync)
        {
            if (_result is not null)
                throw new InvalidOperationException("an MCP command completed more than once");
            _result = result;
            Monitor.PulseAll(_sync);
        }
    }
}

internal sealed class GameMcpCommandResult
{
    private GameMcpCommandResult(
        string status,
        string code,
        string reason,
        ulong observedWorldGeneration,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        string detailsJson,
        bool hasActionResult,
        ServiceActionResult actionResult,
        byte[]? inlinePng)
    {
        Status = status;
        Code = code;
        Reason = reason;
        ObservedWorldGeneration = observedWorldGeneration;
        ObservedLifecycleGeneration = observedLifecycleGeneration;
        ObservedConfigurationGeneration = observedConfigurationGeneration;
        DetailsJson = detailsJson ?? string.Empty;
        HasActionResult = hasActionResult;
        ActionResult = actionResult;
        InlinePng = inlinePng;
        ProcessedAtUtcTicks = DateTime.UtcNow.Ticks;
    }

    internal string Status { get; }
    internal string Code { get; }
    internal string Reason { get; }
    internal ulong ObservedWorldGeneration { get; }
    internal long ObservedLifecycleGeneration { get; }
    internal ulong ObservedConfigurationGeneration { get; }
    internal string DetailsJson { get; }
    internal bool HasActionResult { get; }
    internal ServiceActionResult ActionResult { get; }
    internal byte[]? InlinePng { get; }
    internal long ProcessedAtUtcTicks { get; }

    internal static GameMcpCommandResult Rejected(
        string code,
        string reason,
        ulong observedWorldGeneration = 0,
        long observedLifecycleGeneration = 0,
        ulong observedConfigurationGeneration = 0) =>
        new(
            "rejected",
            code,
            reason,
            observedWorldGeneration,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            string.Empty,
            false,
            default,
            null);

    internal static GameMcpCommandResult Faulted(
        string code,
        string reason,
        ulong observedWorldGeneration = 0,
        long observedLifecycleGeneration = 0,
        ulong observedConfigurationGeneration = 0) =>
        new(
            "faulted",
            code,
            reason,
            observedWorldGeneration,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            string.Empty,
            false,
            default,
            null);

    internal static GameMcpCommandResult FromAction(
        in ServiceActionResult result,
        GameMcpCommandKind commandKind,
        ulong observedWorldGeneration,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        string? exactReason = null)
    {
        var status = result.Disposition switch
        {
            ServiceActionDisposition.Committed => "committed",
            ServiceActionDisposition.Rejected => "rejected",
            ServiceActionDisposition.Faulted => "faulted",
            ServiceActionDisposition.Skipped => "skipped",
            _ => "faulted",
        };
        var code = GameMcpActionResultCodeNames.Name(result.Code, commandKind);
        return new GameMcpCommandResult(
            status,
            code,
            string.IsNullOrWhiteSpace(exactReason)
                ? GameMcpActionResultCodeNames.Reason(result.Code, commandKind, result.Disposition)
                : exactReason!,
            observedWorldGeneration,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            string.Empty,
            true,
            result,
            null);
    }

    internal static GameMcpCommandResult Committed(
        string code,
        string reason,
        ulong observedWorldGeneration,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        string detailsJson = "",
        byte[]? inlinePng = null) =>
        new(
            "committed",
            code,
            reason,
            observedWorldGeneration,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            detailsJson,
            false,
            default,
            inlinePng);

    internal GameMcpCommandResult WithInlinePng(string detailsJson, byte[] inlinePng)
    {
        if (inlinePng is null || inlinePng.Length == 0)
            throw new ArgumentException("A captured PNG is required.", nameof(inlinePng));
        return new GameMcpCommandResult(
            Status,
            Code,
            Reason,
            ObservedWorldGeneration,
            ObservedLifecycleGeneration,
            ObservedConfigurationGeneration,
            detailsJson,
            HasActionResult,
            ActionResult,
            inlinePng);
    }

    internal JObject Project(GameMcpCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        var nativeCalls = 0;
        var mutationAttempts = 0;
        var mutationsCommitted = 0;
        var verifiedMutations = 0;
        var disposition = Title(Status);
        JToken resultCode = Code;
        var resultCodeName = Code;
        var effect = string.Empty;
        var nativeOutcome = string.Empty;
        var hasNativeEvidence = false;
        if (HasActionResult)
        {
            var calls = ActionResult.NativeCallOutcome;
            nativeCalls = calls.NativeCallsAttempted;
            mutationAttempts = calls.MutationAttempts;
            mutationsCommitted = calls.MutationsCommitted;
            hasNativeEvidence = ActionResult.HasNativeEvidence;
            if (hasNativeEvidence)
            {
                nativeOutcome = ActionResult.NativeEvidence.Outcome.ToString();
                if (ActionResult.NativeEvidence.Outcome == NativeMutationOutcome.Verified)
                    verifiedMutations = mutationsCommitted;
            }
            disposition = ActionResult.Disposition.ToString();
            resultCode = ActionResult.Code.Value;
            resultCodeName = GameMcpActionResultCodeNames.Name(ActionResult.Code, command.Kind);
            effect = ActionResult.Effect.ToString();
        }

        var projected = new JObject
        {
            ["sequence"] = command.Sequence,
            ["status"] = Status,
            ["disposition"] = disposition,
            ["resultCode"] = resultCode,
            ["resultCodeName"] = resultCodeName,
            ["reason"] = Reason,
            ["command"] = command.Kind.ToString(),
            ["mode"] = command.Mode,
            ["observedWorldGeneration"] = ObservedWorldGeneration,
            ["observedLifecycleGeneration"] = ObservedLifecycleGeneration,
            ["observedConfigurationGeneration"] = ObservedConfigurationGeneration,
            ["nativeCallsAttempted"] = nativeCalls,
            ["mutationAttempts"] = mutationAttempts,
            ["mutationsCommitted"] = mutationsCommitted,
            ["verifiedMutations"] = verifiedMutations,
            ["submittedAtUtc"] = FormatTicks(command.SubmittedAtUtcTicks),
            ["processedAtUtc"] = FormatTicks(ProcessedAtUtcTicks),
        };
        if (command.DecisionWorldGeneration.HasValue)
            projected["decisionWorldGeneration"] = command.DecisionWorldGeneration.Value;
        if (command.ExpectedLifecycleGeneration > 0)
            projected["expectedLifecycleGeneration"] = command.ExpectedLifecycleGeneration;
        if (command.ExpectedConfigurationGeneration > 0)
            projected["expectedConfigurationGeneration"] = command.ExpectedConfigurationGeneration;
        if (command.TargetId != Guid.Empty)
            projected["targetUuid"] = command.TargetId.ToString("D");
        if (command.SecondaryId != Guid.Empty)
            projected["secondaryUuid"] = command.SecondaryId.ToString("D");
        if (command.DerivedNativeType.Length > 0)
            projected["derivedNativeType"] = command.DerivedNativeType;
        if (command.ExpectedNativeType.Length > 0)
            projected["expectedNativeTypeAssertion"] = command.ExpectedNativeType;
        if (command.Kind == GameMcpCommandKind.ConfigurationSet)
        {
            projected["section"] = command.Mode;
            projected["key"] = command.PayloadKey;
            projected["serializedValue"] = command.PayloadValue;
        }
        if (command.Kind == GameMcpCommandKind.Cast)
            projected["slotIndex"] = command.Amount - 1;
        else if (command.Amount != 1)
            projected["amount"] = command.Amount;
        if (DetailsJson.Length > 0)
        {
            try { projected["details"] = JObject.Parse(DetailsJson); }
            catch { projected["detailsDecodeError"] = "main-thread details were not valid JSON"; }
        }
        if (HasActionResult)
        {
            projected["effect"] = effect;
            projected["hasNativeEvidence"] = hasNativeEvidence;
            projected["nativeOutcome"] = nativeOutcome;
        }
        if (InlinePng is not null)
            projected["inlineImageBytes"] = InlinePng.Length;
        return projected;
    }

    private static string Title(string value) =>
        value.Length == 0
            ? value
            : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private static string FormatTicks(long ticks) =>
        ticks <= 0 || ticks > DateTime.MaxValue.Ticks
            ? string.Empty
            : new DateTime(ticks, DateTimeKind.Utc).ToString("O");
}

/// <summary>
/// Bounded HTTP-to-main-thread mailbox. Terminal outcomes are signaled directly to the
/// submitting HTTP request; the server intentionally retains no receipt or polling history.
/// </summary>
internal sealed class GameMcpCommandBus
{
    internal const int MaximumPending = 64;
    internal const int MaximumPriorityPending = 8;
    internal const int EmergencyStopPrioritySlots = 1;
    internal const int MaximumTotalPending =
        MaximumPending + MaximumPriorityPending + EmergencyStopPrioritySlots;

    private readonly object _sync = new();
    private readonly Queue<GameMcpCommand> _pending = new();
    private readonly Queue<GameMcpCommand> _priorityPending = new();
    private GameMcpCommand? _pendingStopEngage;
    private long _nextSequence;
    private bool _closed;
    private string _closeCode = string.Empty;
    private string _closeReason = string.Empty;
    private bool _emergencyStopObserved;
    private int _pendingStopEngages;

    internal int PendingCount
    {
        get
        {
            lock (_sync)
            {
                return _pending.Count +
                    _priorityPending.Count +
                    (_pendingStopEngage is null ? 0 : 1);
            }
        }
    }

    internal GameMcpCommand Submit(
        GameMcpCommandKind kind,
        ulong? decisionWorldGeneration,
        long expectedLifecycleGeneration,
        ulong expectedConfigurationGeneration,
        string mode,
        Guid targetId,
        Guid secondaryId,
        string derivedNativeType,
        string expectedNativeType,
        int amount)
    {
        var command = NewCommand(
            kind,
            decisionWorldGeneration,
            expectedLifecycleGeneration,
            expectedConfigurationGeneration,
            mode,
            targetId,
            secondaryId,
            derivedNativeType,
            expectedNativeType,
            amount,
            string.Empty,
            string.Empty,
            capture: false,
            saveCapture: false);
        return Enqueue(command);
    }

    internal GameMcpCommand SubmitConfiguration(
        ulong expectedConfigurationGeneration,
        string section,
        string key,
        string serializedValue)
    {
        var command = NewCommand(
            GameMcpCommandKind.ConfigurationSet,
            null,
            0,
            expectedConfigurationGeneration,
            section,
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            string.Empty,
            1,
            key,
            serializedValue,
            capture: false,
            saveCapture: false);
        return Enqueue(command);
    }

    internal GameMcpCommand SubmitEmergencyStop(
        ulong expectedConfigurationGeneration,
        bool engaged)
    {
        var command = NewCommand(
            GameMcpCommandKind.EmergencyStop,
            null,
            0,
            expectedConfigurationGeneration,
            engaged ? "engage" : "resume",
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            string.Empty,
            1,
            string.Empty,
            string.Empty,
            capture: false,
            saveCapture: false);
        return Enqueue(command, priority: true, closesNativeAdmission: engaged);
    }

    internal GameMcpCommand SubmitGadget(
        GameMcpCommandKind kind,
        string mode,
        Guid targetId,
        int amount,
        string payloadValue,
        bool capture,
        bool saveCapture)
    {
        if (kind is < GameMcpCommandKind.Screenshot or > GameMcpCommandKind.ChronicleSelectComparison)
            throw new ArgumentOutOfRangeException(nameof(kind));
        return Enqueue(NewCommand(
            kind,
            null,
            0,
            0,
            mode,
            targetId,
            Guid.Empty,
            string.Empty,
            string.Empty,
            amount,
            string.Empty,
            payloadValue,
            capture,
            saveCapture));
    }

    private GameMcpCommand NewCommand(
        GameMcpCommandKind kind,
        ulong? decisionWorldGeneration,
        long expectedLifecycleGeneration,
        ulong expectedConfigurationGeneration,
        string mode,
        Guid targetId,
        Guid secondaryId,
        string derivedNativeType,
        string expectedNativeType,
        int amount,
        string payloadKey,
        string payloadValue,
        bool capture,
        bool saveCapture) =>
        new(
            Interlocked.Increment(ref _nextSequence),
            kind,
            decisionWorldGeneration,
            expectedLifecycleGeneration,
            expectedConfigurationGeneration,
            mode,
            targetId,
            secondaryId,
            derivedNativeType,
            expectedNativeType,
            amount,
            payloadKey,
            payloadValue,
            capture,
            saveCapture);

    private GameMcpCommand Enqueue(
        GameMcpCommand command,
        bool priority = false,
        bool closesNativeAdmission = false)
    {
        lock (_sync)
        {
            if (_closed)
            {
                command.Completion.Complete(GameMcpCommandResult.Rejected(
                    _closeCode,
                    _closeReason));
                return command;
            }
            var nativeAction =
                command.Kind is >= GameMcpCommandKind.Purchase and <= GameMcpCommandKind.SpellLevel;
            if (nativeAction && (_emergencyStopObserved || _pendingStopEngages > 0))
            {
                command.Completion.Complete(GameMcpCommandResult.Rejected(
                    _emergencyStopObserved
                        ? "emergency_stop"
                        : "emergency_stop_pending",
                    _emergencyStopObserved
                        ? "the suite emergency stop is engaged"
                        : "an accepted priority emergency-stop command closes native action admission"));
                return command;
            }
            if (priority)
            {
                if (closesNativeAdmission)
                {
                    if (_pendingStopEngages > 0)
                    {
                        command.Completion.Complete(GameMcpCommandResult.Rejected(
                            "emergency_stop_transition_pending",
                            "an emergency-stop engage command is already accepted"));
                        return command;
                    }
                    while (_priorityPending.Count > 0)
                    {
                        RejectPending(
                            _priorityPending.Dequeue(),
                            "superseded_by_emergency_stop",
                            "an accepted emergency-stop engage superseded this queued resume");
                    }
                    _pendingStopEngages++;
                    _pendingStopEngage = command;
                    return command;
                }
                if (_pendingStopEngages > 0)
                {
                    command.Completion.Complete(GameMcpCommandResult.Rejected(
                        "superseded_by_emergency_stop",
                        "an accepted emergency-stop engage supersedes this resume"));
                    return command;
                }
                if (_priorityPending.Count >= MaximumPriorityPending)
                {
                    command.Completion.Complete(GameMcpCommandResult.Rejected(
                        "priority_command_queue_full",
                        "the dedicated priority MCP queue already holds " +
                        MaximumPriorityPending + " administrative transitions"));
                    return command;
                }
                _priorityPending.Enqueue(command);
                return command;
            }
            if (_pending.Count >= MaximumPending)
            {
                command.Completion.Complete(GameMcpCommandResult.Rejected(
                    "command_queue_full",
                    "the bounded MCP queue already holds " + MaximumPending + " commands"));
                return command;
            }
            _pending.Enqueue(command);
            return command;
        }
    }

    internal bool TryDequeue(out GameMcpCommand command)
    {
        lock (_sync)
        {
            if (_pendingStopEngage is not null)
            {
                command = _pendingStopEngage;
                _pendingStopEngage = null;
                return true;
            }
            if (_priorityPending.Count > 0)
            {
                command = _priorityPending.Dequeue();
                return true;
            }
            if (_pending.Count == 0)
            {
                command = null!;
                return false;
            }
            command = _pending.Dequeue();
            return true;
        }
    }

    internal void Complete(GameMcpCommand command, GameMcpCommandResult result)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (result is null) throw new ArgumentNullException(nameof(result));
        lock (_sync)
        {
            if (command.Kind == GameMcpCommandKind.EmergencyStop)
            {
                if (command.Mode == "engage")
                {
                    if (_pendingStopEngages <= 0)
                        throw new InvalidOperationException(
                            "MCP emergency-stop admission accounting underflowed");
                    _pendingStopEngages--;
                    if (string.Equals(result.Status, "committed", StringComparison.Ordinal))
                        _emergencyStopObserved = true;
                }
                else if (command.Mode == "resume" &&
                         string.Equals(result.Status, "committed", StringComparison.Ordinal))
                {
                    _emergencyStopObserved = false;
                }
            }
        }
        command.Completion.Complete(result);
    }

    internal void ObserveEmergencyStop(bool engaged)
    {
        lock (_sync)
        {
            _emergencyStopObserved = engaged;
        }
    }

    internal void Close(string code, string reason)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A close code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A close reason is required.", nameof(reason));
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            _closeCode = code;
            _closeReason = reason;
            if (_pendingStopEngage is not null)
            {
                RejectPendingForClose(_pendingStopEngage);
                _pendingStopEngage = null;
                _pendingStopEngages--;
            }
            while (_priorityPending.Count > 0)
                RejectPendingForClose(_priorityPending.Dequeue());
            while (_pending.Count > 0)
                RejectPendingForClose(_pending.Dequeue());
        }
    }

    private void RejectPendingForClose(GameMcpCommand command) =>
        RejectPending(command, _closeCode, _closeReason);

    private static void RejectPending(GameMcpCommand command, string code, string reason) =>
        command.Completion.Complete(GameMcpCommandResult.Rejected(code, reason));
}

internal static class GameMcpActionResultCodeNames
{
    internal static string Reason(
        ServiceActionResultCode code,
        GameMcpCommandKind commandKind,
        ServiceActionDisposition disposition)
    {
        var exact = code.Value;
        if (code == CommonActionResultCodes.Committed)
            return "the audited native mutation committed and its postcondition was verified";
        if (code == CommonActionResultCodes.EmergencyStop)
            return "the suite emergency stop rejected the action before native mutation";
        if (code == CommonActionResultCodes.LifecycleReplaced)
            return "the live game lifecycle no longer matches the collected world epoch";
        if (code == CommonActionResultCodes.ServiceDisabled)
            return "the owning suite service is disabled";
        if (code == CommonActionResultCodes.NativeRejected)
            return "live native admission rejected the UUID-resolved target after revalidation";
        if (code == CommonActionResultCodes.PolicyRejected)
            return "the owning service policy rejected the action";
        if (code == CommonActionResultCodes.AdapterFault)
            return "the native adapter could not prove a safe, verified mutation";
        if (code == CommonActionResultCodes.Skipped)
            return "live revalidation or the native call produced no mutation, so the action was skipped";
        if (code == AutoCastActionResultCodes.ManualPause)
            return "the spell slot is under the player's manual-pause authority";
        if (code == AutoCastActionResultCodes.TargetingInProgress)
            return "the spell slot is already in a native targeting interaction";
        if (code == SpellLevelActionResultCodes.ProgressionLocked)
            return "native spell-level progression is not unlocked";
        if (code == SpellLevelActionResultCodes.LevelNotAffordable)
            return "the live spell-level cost is not affordable";
        if (code == AutoHarvestActionResultCodes.PairContractUnavailable)
            return "the published plot/action pair contract is unavailable";
        if (code == AutoHarvestActionResultCodes.FeatureContractUnavailable)
            return "the native harvest feature contract is unavailable";
        if (code == AutoHarvestActionResultCodes.PairFaulted)
            return "the native harvest pair faulted before a verified mutation";
        if (code == AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet)
            return "native prerequisites are currently unmet according to one fresh action-boundary check";
        if (code == AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable)
            return "the exact native harvest prerequisite validation was unreadable, so no quantity mutation was attempted";
        if (code == AutoHarvestActionResultCodes.ActionFamilyUnavailable ||
            code == AutoBuyActionResultCodes.ActionFamilyUnavailable ||
            code == AutoCastActionResultCodes.ActionFamilyUnavailable)
        {
            return "the suite does not own the requested native action family";
        }
        return "the native action boundary returned " + disposition +
            " with exact result code " + exact;
    }

    internal static string Name(
        ServiceActionResultCode code,
        GameMcpCommandKind commandKind)
    {
        if (code == CommonActionResultCodes.Committed) return "committed";
        if (code == CommonActionResultCodes.EmergencyStop) return "emergency_stop";
        if (code == CommonActionResultCodes.LifecycleReplaced) return "lifecycle_replaced";
        if (code == CommonActionResultCodes.ServiceDisabled) return "service_disabled";
        if (code == CommonActionResultCodes.NativeRejected) return "native_rejected";
        if (code == CommonActionResultCodes.PolicyRejected) return "policy_rejected";
        if (code == CommonActionResultCodes.AdapterFault) return "adapter_fault";
        if (code == CommonActionResultCodes.Skipped) return "skipped";
        if (code == AutoHarvestActionResultCodes.ActionFamilyUnavailable)
            return "action_family_unavailable";
        if (code == AutoHarvestActionResultCodes.PairContractUnavailable)
            return "pair_contract_unavailable";
        if (code == AutoHarvestActionResultCodes.FeatureContractUnavailable)
            return "feature_contract_unavailable";
        if (code == AutoHarvestActionResultCodes.PairFaulted)
            return "pair_faulted";
        if (code == AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet)
            return "native_prerequisites_currently_unmet";
        if (code == AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable)
            return "native_prerequisite_validation_unavailable";
        if (code == AutoBuyActionResultCodes.ActionFamilyUnavailable &&
            (commandKind == GameMcpCommandKind.Purchase ||
             commandKind == GameMcpCommandKind.SpellLevel))
            return "action_family_unavailable";
        if (code == SpellLevelActionResultCodes.ProgressionLocked)
            return "progression_locked";
        if (code == SpellLevelActionResultCodes.LevelNotAffordable)
            return "level_not_affordable";
        if (code == AutoCastActionResultCodes.ActionFamilyUnavailable)
            return "action_family_unavailable";
        if (code == AutoCastActionResultCodes.ManualPause) return "manual_pause";
        if (code == AutoCastActionResultCodes.TargetingInProgress)
            return "targeting_in_progress";
        if (code == AutoCastActionResultCodes.NativeCasterBusy)
            return "native_caster_busy";
        if (code == AutoCastActionResultCodes.SlotIdentityChanged)
            return "slot_identity_changed";
        if (code == AutoCastActionResultCodes.SpellNotReady) return "spell_not_ready";
        if (code == AutoCastActionResultCodes.NoValidTarget) return "no_valid_target";
        if (code == AutoCastActionResultCodes.ChargeHoldRefused)
            return "charge_hold_refused";
        if (code == AutoConceptActionResultCodes.ActionFamilyUnavailable)
            return "action_family_unavailable";
        if (code == AutoConceptActionResultCodes.RecipeIdentityChanged)
            return "recipe_identity_changed";
        if (code == AutoConceptActionResultCodes.AssignmentUnsettled)
            return "assignment_unsettled";
        if (code == AutoConceptActionResultCodes.OwnershipChanged)
            return "ownership_changed";
        if (code == AutoConceptActionResultCodes.SlotUnavailable)
            return "slot_unavailable";
        if (code == AutoConceptActionResultCodes.ProjectionRefused)
            return "projection_refused";
        if (code == AutoConceptActionResultCodes.MasteryLimitChanged)
            return "mastery_limit_changed";
        return "feature_" + code.Value;
    }
}

/// <summary>Pure, ordered admission checks applied before a main-thread native adapter is selected.</summary>
internal static class GameMcpNativeActionAdmission
{
    internal static bool TryReject(
        GameMcpCommand command,
        ulong currentWorldGeneration,
        long currentLifecycleGeneration,
        ulong currentConfigurationGeneration,
        bool emergencyStopEngaged,
        out GameMcpCommandResult rejection)
    {
        if (command.ExpectedLifecycleGeneration != currentLifecycleGeneration)
        {
            rejection = GameMcpCommandResult.Rejected(
                "lifecycle_replaced",
                "command expected lifecycle " + command.ExpectedLifecycleGeneration +
                " but the main thread now has lifecycle " + currentLifecycleGeneration,
                currentWorldGeneration,
                currentLifecycleGeneration,
                currentConfigurationGeneration);
            return true;
        }
        if (command.ExpectedConfigurationGeneration != currentConfigurationGeneration)
        {
            rejection = GameMcpCommandResult.Rejected(
                "stale_configuration_generation",
                "command expected configuration generation " +
                command.ExpectedConfigurationGeneration +
                " but the main thread now has generation " +
                currentConfigurationGeneration,
                currentWorldGeneration,
                currentLifecycleGeneration,
                currentConfigurationGeneration);
            return true;
        }
        if (emergencyStopEngaged)
        {
            rejection = GameMcpCommandResult.Rejected(
                "emergency_stop",
                "the suite emergency stop is engaged; no MCP native action was attempted",
                currentWorldGeneration,
                currentLifecycleGeneration,
                currentConfigurationGeneration);
            return true;
        }
        rejection = null!;
        return false;
    }

    internal static void AssertNativeType(GameMcpCommand command, string derived)
    {
        if (!string.Equals(command.DerivedNativeType, derived, StringComparison.Ordinal))
            throw new ArgumentException(
                "the server-derived native type must be exactly " + derived +
                " for " + command.Kind + ", not " + command.DerivedNativeType);
        if (command.ExpectedNativeType.Length > 0 &&
            !string.Equals(command.ExpectedNativeType, derived, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "expectedNativeType assertion must be exactly " + derived +
                " for " + command.Kind + ", not " + command.ExpectedNativeType);
        }
    }
}
#endif
