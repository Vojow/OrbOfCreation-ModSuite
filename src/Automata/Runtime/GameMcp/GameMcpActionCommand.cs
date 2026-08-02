#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Threading;
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
    DiscoveryTreeOffer = 15,
    SpellWorkbench = 16,
    SpellComposition = 17,
    SpellLoadout = 18,
    Targeting = 19,
    Consumable = 20,
    Crafting = 21,
}

internal static class GameMcpCommandKinds
{
    internal static bool IsGameplayAction(GameMcpCommandKind kind) =>
        kind is >= GameMcpCommandKind.Purchase and <= GameMcpCommandKind.SpellLevel or
            GameMcpCommandKind.DiscoveryTreeOffer or GameMcpCommandKind.SpellWorkbench or
            GameMcpCommandKind.SpellComposition or GameMcpCommandKind.SpellLoadout or
            GameMcpCommandKind.Targeting or GameMcpCommandKind.Consumable or
            GameMcpCommandKind.Crafting;

    internal static GameMcpCommandKind FromToolName(string toolName) => toolName switch
    {
        "game_purchase" => GameMcpCommandKind.Purchase,
        "game_cast" => GameMcpCommandKind.Cast,
        "game_concept" => GameMcpCommandKind.Concept,
        "game_harvest" => GameMcpCommandKind.Harvest,
        "game_spell_level" => GameMcpCommandKind.SpellLevel,
        "game_discovery_offer" => GameMcpCommandKind.DiscoveryTreeOffer,
        "game_spell_workbench" => GameMcpCommandKind.SpellWorkbench,
        "game_spell_composition" => GameMcpCommandKind.SpellComposition,
        "game_spell_loadout" => GameMcpCommandKind.SpellLoadout,
        "game_targeting" => GameMcpCommandKind.Targeting,
        "game_consumable" => GameMcpCommandKind.Consumable,
        "game_craft" => GameMcpCommandKind.Crafting,
        "suite_config_set" => GameMcpCommandKind.ConfigurationSet,
        "suite_emergency_stop" => GameMcpCommandKind.EmergencyStop,
        "game_screenshot" => GameMcpCommandKind.Screenshot,
        "game_navigate" => GameMcpCommandKind.Navigation,
        "game_probe" => GameMcpCommandKind.Probe,
        "game_screen_catalog" => GameMcpCommandKind.ScreenCatalog,
        "game_tooltips" => GameMcpCommandKind.TooltipCatalog,
        "game_tooltip" => GameMcpCommandKind.TooltipRead,
        "game_continue" => GameMcpCommandKind.ContinueRun,
        _ => throw new ArgumentException(
            "no MCP command capability is registered for " + toolName,
            nameof(toolName)),
    };
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
        bool saveCapture,
        GameMcpFrameOperation? sourceOperation = null,
        GameMcpFrameContext? frameContext = null,
        GameMcpUuidCount[]? uuidCounts = null)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        var nativeAction = GameMcpCommandKinds.IsGameplayAction(kind);
        if (nativeAction && expectedLifecycleGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLifecycleGeneration));
        if ((nativeAction || kind is GameMcpCommandKind.ConfigurationSet or GameMcpCommandKind.EmergencyStop) &&
            expectedConfigurationGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(expectedConfigurationGeneration));
        if (string.IsNullOrWhiteSpace(mode)) throw new ArgumentException("A mode is required.", nameof(mode));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        Sequence = sequence;
        Kind = kind;
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
        SourceOperation = sourceOperation;
        FrameContext = frameContext;
        UuidCounts = uuidCounts is null
            ? Array.Empty<GameMcpUuidCount>()
            : (GameMcpUuidCount[])uuidCounts.Clone();
    }

    internal long Sequence { get; }
    internal GameMcpCommandKind Kind { get; }
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
    internal GameMcpFrameOperation? SourceOperation { get; }
    internal GameMcpFrameContext? FrameContext { get; }
    internal GameMcpUuidCount[] UuidCounts { get; }
}

internal sealed class GameMcpCommandResult
{
    private GameMcpCommandResult(
        string status,
        string code,
        string reason,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        GameMcpValue? details,
        bool hasActionResult,
        ServiceActionResult actionResult,
        byte[]? inlinePng)
    {
        Status = status;
        Code = code;
        Reason = reason;
        ObservedLifecycleGeneration = observedLifecycleGeneration;
        ObservedConfigurationGeneration = observedConfigurationGeneration;
        Details = details;
        HasActionResult = hasActionResult;
        ActionResult = actionResult;
        InlinePng = inlinePng;
    }

    internal string Status { get; }
    internal string Code { get; }
    internal string Reason { get; }
    internal long ObservedLifecycleGeneration { get; }
    internal ulong ObservedConfigurationGeneration { get; }
    internal GameMcpValue? Details { get; }
    internal bool HasActionResult { get; }
    /// <summary>
    /// True only when the MCP tool itself failed before it produced a canonical domain result.
    /// A faulted GameAction is still a successfully delivered tool result: its exact receipt must
    /// remain available to clients through structuredContent.
    /// </summary>
    internal bool IsProtocolError =>
        string.Equals(Status, "faulted", StringComparison.Ordinal) && !HasActionResult;
    internal ServiceActionResult ActionResult { get; }
    internal byte[]? InlinePng { get; }

    internal static GameMcpCommandResult Rejected(
        string code,
        string reason,
        long observedLifecycleGeneration = 0,
        ulong observedConfigurationGeneration = 0) =>
        new(
            "refused",
            code,
            reason,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            null,
            false,
            default,
            null);

    internal static GameMcpCommandResult Faulted(
        string code,
        string reason,
        long observedLifecycleGeneration = 0,
        ulong observedConfigurationGeneration = 0) =>
        new(
            "faulted",
            code,
            reason,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            null,
            false,
            default,
            null);

    internal static GameMcpCommandResult FromAction(
        in ServiceActionResult result,
        GameMcpCommandKind commandKind,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        string? exactReason = null,
        GameMcpValue? details = null)
    {
        var status = result.Disposition switch
        {
            ServiceActionDisposition.Committed => "committed",
            ServiceActionDisposition.Rejected => "refused",
            ServiceActionDisposition.Faulted => "faulted",
            ServiceActionDisposition.Skipped => "refused",
            _ => "faulted",
        };
        var code = GameMcpActionResultCodeNames.Name(result.Code, commandKind);
        return new GameMcpCommandResult(
            status,
            code,
            string.IsNullOrWhiteSpace(exactReason)
                ? GameMcpActionResultCodeNames.Reason(result.Code, commandKind, result.Disposition)
                : exactReason!,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            details,
            true,
            result,
            null);
    }

    internal static GameMcpCommandResult Committed(
        string code,
        string reason,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        GameMcpValue? details = null,
        byte[]? inlinePng = null) =>
        new(
            "committed",
            code,
            reason,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            details,
            false,
            default,
            inlinePng);

    internal GameMcpCommandResult WithInlinePng(GameMcpValue? details, byte[] inlinePng)
    {
        if (inlinePng is null || inlinePng.Length == 0)
            throw new ArgumentException("A captured PNG is required.", nameof(inlinePng));
        return new GameMcpCommandResult(
            Status,
            Code,
            Reason,
            ObservedLifecycleGeneration,
            ObservedConfigurationGeneration,
            details,
            HasActionResult,
            ActionResult,
            inlinePng);
    }

    internal GameMcpCommandResult WithDetails(GameMcpValue details) =>
        new(
            Status,
            Code,
            Reason,
            ObservedLifecycleGeneration,
            ObservedConfigurationGeneration,
            details,
            HasActionResult,
            ActionResult,
            InlinePng);

    internal GameMcpValue Project(GameMcpCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        var stableCode = Code;
        if (HasActionResult)
            stableCode = GameMcpActionResultCodeNames.Name(ActionResult.Code, command.Kind);
        var status = Status;
        if (command.SourceOperation?.Request.Classification == GameMcpOperationClass.ReadOnly)
        {
            status = status switch
            {
                "committed" => "available",
                "refused" => "unavailable",
                _ => status,
            };
        }

        var projected = new GameMcpObjectBuilder
        {
            ["status"] = status,
        };
        var succeeded = status is "committed" or "available";
        if (!succeeded)
        {
            projected["reasonCode"] = stableCode;
            projected["reason"] = Reason;
            if (command.TargetId != Guid.Empty) projected["targetId"] = command.TargetId;
            AddGenerationMismatch(projected, command);
            AddFailureAudit(projected, command);
        }
        if (Details is GameMcpObject details) projected.CopyFrom(details);
        else if (Details is not null) projected["result"] = Details;
        return projected.Freeze();
    }

    private void AddGenerationMismatch(GameMcpObjectBuilder projected, GameMcpCommand command)
    {
        if (command.ExpectedLifecycleGeneration > 0 &&
            command.ExpectedLifecycleGeneration != ObservedLifecycleGeneration)
        {
            projected["lifecycleGenerationMismatch"] = new GameMcpObjectBuilder
            {
                ["expected"] = command.ExpectedLifecycleGeneration,
                ["observed"] = ObservedLifecycleGeneration,
            };
        }
        if (command.ExpectedConfigurationGeneration > 0 &&
            command.ExpectedConfigurationGeneration != ObservedConfigurationGeneration)
        {
            projected["configurationGenerationMismatch"] = new GameMcpObjectBuilder
            {
                ["expected"] = command.ExpectedConfigurationGeneration,
                ["observed"] = ObservedConfigurationGeneration,
            };
        }
    }

    private void AddFailureAudit(GameMcpObjectBuilder projected, GameMcpCommand command)
    {
        if (!HasActionResult) return;
        if (ActionResult.HasNativeEvidence)
            projected["nativeOutcome"] = ActionResult.NativeEvidence.Outcome.ToString();
    }

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
        if (commandKind == GameMcpCommandKind.DiscoveryTreeOffer)
            return "the Discovery Tree offer boundary returned " + disposition +
                " with exact preflight code " + exact;
        if (commandKind == GameMcpCommandKind.SpellWorkbench)
            return "the spell workbench boundary returned " + disposition +
                " with exact preflight code " + exact;
        if (commandKind == GameMcpCommandKind.SpellComposition)
            return "the spell composition boundary returned " + disposition +
                " with exact preflight code " + exact;
        if (commandKind == GameMcpCommandKind.SpellLoadout)
            return "the spell loadout boundary returned " + disposition +
                " with exact preflight code " + exact;
        if (commandKind == GameMcpCommandKind.Targeting)
            return "the targeting boundary returned " + disposition +
                " with exact preflight code " + exact;
        if (commandKind == GameMcpCommandKind.Consumable)
            return "the consumable boundary returned " + disposition +
                " with exact preflight code " + exact;
        if (commandKind == GameMcpCommandKind.Crafting)
            return "the one-shot crafting boundary returned " + disposition +
                " with exact preflight code " + exact;
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
        if (commandKind == GameMcpCommandKind.DiscoveryTreeOffer)
        {
            if (code == DiscoveryTreeOfferActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.Quarantined) return "quarantined";
            if (code == DiscoveryTreeOfferActionResultCodes.WrongThread) return "wrong_thread";
            if (code == DiscoveryTreeOfferActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.TreeUnavailable) return "tree_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.WrongMode) return "wrong_mode";
            if (code == DiscoveryTreeOfferActionResultCodes.NoDiscoveries) return "no_discoveries";
            if (code == DiscoveryTreeOfferActionResultCodes.OfferUnavailable) return "offer_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.AlreadyDiscovered) return "already_discovered";
            if (code == DiscoveryTreeOfferActionResultCodes.RerollUnavailable) return "reroll_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.Unaffordable) return "unaffordable";
            if (code == DiscoveryTreeOfferActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == DiscoveryTreeOfferActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.SpellWorkbench)
        {
            if (code == SpellWorkbenchActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == SpellWorkbenchActionResultCodes.Quarantined) return "quarantined";
            if (code == SpellWorkbenchActionResultCodes.WrongThread) return "wrong_thread";
            if (code == SpellWorkbenchActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == SpellWorkbenchActionResultCodes.SelectionUnavailable) return "selection_unavailable";
            if (code == SpellWorkbenchActionResultCodes.WrongSelection) return "wrong_selection";
            if (code == SpellWorkbenchActionResultCodes.AlreadyDiscovered) return "already_discovered";
            if (code == SpellWorkbenchActionResultCodes.DiscoveryUnavailable) return "discovery_unavailable";
            if (code == SpellWorkbenchActionResultCodes.RecipeUnavailable) return "recipe_unavailable";
            if (code == SpellWorkbenchActionResultCodes.Unaffordable) return "unaffordable";
            if (code == SpellWorkbenchActionResultCodes.LoadoutFull) return "loadout_full";
            if (code == SpellWorkbenchActionResultCodes.CompositionUnsupported) return "composition_unsupported";
            if (code == SpellWorkbenchActionResultCodes.MutationPermitUnavailable)
                return "action_family_unavailable";
            if (code == SpellWorkbenchActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == SpellWorkbenchActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.SpellComposition)
        {
            if (code == SpellCompositionActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == SpellCompositionActionResultCodes.Quarantined) return "quarantined";
            if (code == SpellCompositionActionResultCodes.WrongThread) return "wrong_thread";
            if (code == SpellCompositionActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == SpellCompositionActionResultCodes.OutputLevelOutOfRange) return "output_level_out_of_range";
            if (code == SpellCompositionActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == SpellCompositionActionResultCodes.GlyphIdentityUnavailable) return "glyph_identity_unavailable";
            if (code == SpellCompositionActionResultCodes.DuplicateGlyph) return "duplicate_glyph";
            if (code == SpellCompositionActionResultCodes.GlyphUnavailable) return "glyph_unavailable";
            if (code == SpellCompositionActionResultCodes.NotAnAugment) return "not_an_augment";
            if (code == SpellCompositionActionResultCodes.UsageLimitExceeded) return "usage_limit_exceeded";
            if (code == SpellCompositionActionResultCodes.IncompatibleComposition) return "incompatible_composition";
            if (code == SpellCompositionActionResultCodes.MasteryRequirementUnmet) return "mastery_requirement_unmet";
            if (code == SpellCompositionActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == SpellCompositionActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == SpellCompositionActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.SpellLoadout)
        {
            if (code == SpellLoadoutActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == SpellLoadoutActionResultCodes.Quarantined) return "quarantined";
            if (code == SpellLoadoutActionResultCodes.WrongThread) return "wrong_thread";
            if (code == SpellLoadoutActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == SpellLoadoutActionResultCodes.NativeRemoveRefused) return "native_remove_refused";
            if (code == SpellLoadoutActionResultCodes.DestinationOutOfRange) return "destination_out_of_range";
            if (code == SpellLoadoutActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == SpellLoadoutActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == SpellLoadoutActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == SpellLoadoutActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Targeting)
        {
            if (code == TargetingActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == TargetingActionResultCodes.Quarantined) return "quarantined";
            if (code == TargetingActionResultCodes.WrongThread) return "wrong_thread";
            if (code == TargetingActionResultCodes.NoPendingRequest) return "no_pending_request";
            if (code == TargetingActionResultCodes.TargetUnavailable) return "target_unavailable";
            if (code == TargetingActionResultCodes.NativeTargetRefused) return "native_target_refused";
            if (code == TargetingActionResultCodes.CancelUnavailable) return "cancel_unavailable";
            if (code == TargetingActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == TargetingActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == TargetingActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Consumable)
        {
            if (code == ConsumablePlayerActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == ConsumablePlayerActionResultCodes.Quarantined) return "quarantined";
            if (code == ConsumablePlayerActionResultCodes.WrongThread) return "wrong_thread";
            if (code == ConsumablePlayerActionResultCodes.ItemUnavailable) return "item_unavailable";
            if (code == ConsumablePlayerActionResultCodes.NotVisible) return "not_visible";
            if (code == ConsumablePlayerActionResultCodes.TargetingInProgress) return "targeting_in_progress";
            if (code == ConsumablePlayerActionResultCodes.InventoryBusy) return "inventory_busy";
            if (code == ConsumablePlayerActionResultCodes.CanFireRefused) return "can_fire_refused";
            if (code == ConsumablePlayerActionResultCodes.NoCancellableUsage) return "no_cancellable_usage";
            if (code == ConsumablePlayerActionResultCodes.NothingToDiscard) return "nothing_to_discard";
            if (code == ConsumablePlayerActionResultCodes.RandomizationUnavailable) return "randomization_unavailable";
            if (code == ConsumablePlayerActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == ConsumablePlayerActionResultCodes.ListUnavailable) return "list_unavailable";
            if (code == ConsumablePlayerActionResultCodes.SourceUnavailable) return "source_unavailable";
            if (code == ConsumablePlayerActionResultCodes.DestinationOutOfRange) return "destination_out_of_range";
            if (code == ConsumablePlayerActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == ConsumablePlayerActionResultCodes.MultiBuyUnavailable) return "multi_buy_unavailable";
            if (code == ConsumablePlayerActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == ConsumablePlayerActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Crafting)
        {
            if (code == CraftingPlayerActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == CraftingPlayerActionResultCodes.Quarantined) return "quarantined";
            if (code == CraftingPlayerActionResultCodes.WrongThread) return "wrong_thread";
            if (code == CraftingPlayerActionResultCodes.RecipeUnavailable) return "recipe_unavailable";
            if (code == CraftingPlayerActionResultCodes.NotVisible) return "not_visible";
            if (code == CraftingPlayerActionResultCodes.PageRelationAmbiguous) return "page_relation_ambiguous";
            if (code == CraftingPlayerActionResultCodes.InvalidPurchaseAmount) return "invalid_purchase_amount";
            if (code == CraftingPlayerActionResultCodes.QueueFull) return "queue_full";
            if (code == CraftingPlayerActionResultCodes.Unaffordable) return "unaffordable";
            if (code == CraftingPlayerActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == CraftingPlayerActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == CraftingPlayerActionResultCodes.VerificationFailed) return "verification_failed";
        }
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
                currentLifecycleGeneration,
                currentConfigurationGeneration);
            return true;
        }
        if (emergencyStopEngaged)
        {
            rejection = GameMcpCommandResult.Rejected(
                "emergency_stop",
                "the suite emergency stop is engaged; no MCP native action was attempted",
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
