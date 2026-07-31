using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The single consumable-use GameAction: lifecycle-scoped complete bindings, live preflights,
/// one native transaction, and exact before/after evidence.
/// </summary>
internal sealed class AutoItemsConsumableUseGameAction : IDisposable
{
    private static readonly object[] EnableRandomizationArguments = { true };
    private const int MaximumExceptionMessageCharacters = 512;
    private const int MaximumExceptionStackCharacters = 2048;

    private readonly TypedRegistryResolver _registryResolver;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readMutationPermitFailure;
    private readonly Func<long> _readFrameIdentity;
    private readonly Action<long, long> _observeMutationAttempt;
    private readonly Dictionary<Guid, string> _temporaryQuarantine = new();
    private AutoItemsNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;
    private string _scrollQuarantineReason = string.Empty;

    internal AutoItemsConsumableUseGameAction(
        TypedRegistryResolver registryResolver,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readMutationPermitFailure,
        Func<long> readFrameIdentity,
        Action<long, long> observeMutationAttempt)
    {
        _registryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readMutationPermitFailure = readMutationPermitFailure ??
            throw new ArgumentNullException(nameof(readMutationPermitFailure));
        _readFrameIdentity = readFrameIdentity ??
            throw new ArgumentNullException(nameof(readFrameIdentity));
        _observeMutationAttempt = observeMutationAttempt ??
            throw new ArgumentNullException(nameof(observeMutationAttempt));
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;
    internal string QuarantineReason => _quarantineReason.Length != 0
        ? _quarantineReason
        : _scrollQuarantineReason;

    internal AutoItemsSubmission Submit(in AutoItemsCycleAction action)
    {
        if (_temporaryQuarantine.TryGetValue(action.ItemId, out var exactQuarantineReason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                exactQuarantineReason);
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            _scrollQuarantineReason.Length != 0)
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                _scrollQuarantineReason);
        }
        if (_quarantineReason.Length != 0)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                _quarantineReason);
        if (_bindings is not { } native)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped Auto Items binding set is unavailable."
                    : _bindingFailure);

        var resolution = _registryResolver.Resolve(action.ItemId, native.ConsumableType);
        if (!resolution.IsResolved)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ItemUnavailable,
                resolution.Format());
        var item = resolution.Value!;
        var temporary = AutoItemsConsumableFamilies.IsTemporary(action.Family);
        if (!HasExpectedFamily(item, action.Family, native, out var reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.FamilyChanged, reason);
        if (!InvokeBool(native.IsVisible, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NotVisible,
                $"ConsumableSO.IsVisible() refused {action.ItemId:D}.");
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            native.CanBeRandomized.GetValue(item) is not true)
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.RandomizationUnavailable,
                $"Scroll {action.ItemId:D} no longer has ConsumableSO.canBeRandomized=true.");
        }
        var liveLevel = temporary ? 0 : Invoke<int>(native.StrongestLevel, item);
        if (!temporary && liveLevel != action.PlannedLevel)
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TargetUnavailable,
                $"The strongest live {action.Family} level changed from planned " +
                $"{action.PlannedLevel} to {liveLevel} before native preparation.");
        }
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            !AutoItemsScrollTargetPreflight.TryHasValidTarget(
                item,
                action.PlannedLevel,
                native,
                out reason))
        {
            return AutoItemsSubmission.Reject(AutoItemsPreflight.TargetUnavailable, reason);
        }

        if (temporary &&
            !TryHasFinitePositiveDuration(item, action.ItemId, native, out reason))
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryDurationChanged,
                reason);
        }
        if (temporary &&
            !TryHasSafeTemporaryCosts(item, action.ItemId, native, out reason))
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryCostChanged,
                reason);
        }
        if (!TryAnyTemporaryUsage(native, out var temporaryUsagePresent, out reason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                reason);
        if (temporaryUsagePresent)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryEffectPresent,
                reason);
        if (InvokeBool(native.IsTargeting, null))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NativeBusy,
                "TargetingManager.IsTargeting() reported an active native target request.");
        if (!InvokeBool(native.CanUseConsumable, null))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NativeBusy,
                "Inventory.CanUseConsumable() refused because native consumable preparation is busy.");
        if (!TryReadQueuedOrPendingState(native, out var queueOccupied, out reason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                reason);
        if (queueOccupied)
        {
            var queueReason =
                $"The native consumable queue was queued or pending while " +
                $"Inventory.CanUseConsumable() reported idle before {action.Family} " +
                $"{action.ItemId:D}: {reason}";
            if (action.Family == AutoItemsConsumableFamily.Scroll)
            {
                _scrollQuarantineReason =
                    "Auto Items Scroll use is quarantined for this lifecycle after inconsistent " +
                    "native queue evidence. " + queueReason;
                return AutoItemsSubmission.Reject(
                    AutoItemsPreflight.Quarantined,
                    _scrollQuarantineReason);
            }
            return AutoItemsSubmission.Reject(AutoItemsPreflight.NativeBusy, queueReason);
        }
        if (!InvokeBool(native.CanFire, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.CanFireRefused,
                $"ConsumableSO.CanFire() refused live {action.Family} {action.ItemId:D}.");
        if (!TryCaptureMutationPermit(out reason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.MutationPermitUnavailable,
                reason);
        if (!NativeMultiBuyScope.TryEnterOne(out var multiBuy, out reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.MultiBuyUnavailable, reason);

        var itemId = action.ItemId;
        var family = action.Family;
        var plannedLevel = action.PlannedLevel;
        NativeMutationEvidence<ItemState> evidence;
        using (multiBuy)
        {
            if (!TryReadMutationFrame(out var mutationFrame, out reason))
                return AutoItemsSubmission.Reject(
                    AutoItemsPreflight.ContractUnavailable,
                    reason);
            if (!temporary && !TryHasReadyAudioPool(native, out reason))
                return AutoItemsSubmission.Reject(
                    AutoItemsPreflight.AudioUnavailable,
                    reason);
            var mutationLifecycle = action.CollectedAtEpoch;
            evidence = NativeMutationVerifier.Execute(
                "Auto Items consumable use",
                itemId.ToString("D"),
                temporary
                    ? "one item leaves stock, one enters the native queue, and one temporary usage appears"
                    : "one item leaves stock, one enters the native preparation queue, and one exact-level usage appears",
                () => Capture(item, native),
                () => MutateAndObserve(
                    item,
                    family,
                    native,
                    mutationLifecycle,
                    mutationFrame),
                (before, after) =>
                    after.Quantity == before.Quantity - 1 &&
                    after.Queued == before.Queued + 1 &&
                    after.Usages == before.Usages + 1 &&
                    after.Preparation.CompareTo(BigDouble.Zero) > 0 &&
                    (temporary || after.UsageLevel == plannedLevel) &&
                    (family != AutoItemsConsumableFamily.Scroll || after.Randomized));
        }

        var callOutcome = new NativeMutationCallOutcome(
            evidence.MutationWasAttempted
                ? action.Family == AutoItemsConsumableFamily.Scroll ? 2 : 1
                : 0,
            evidence.MutationWasAttempted ? 1 : 0,
            evidence.IsVerified ? 1 : 0);
        var preflight = AutoItemsPreflight.Proceeded;
        var failureReason = string.Empty;
        if (evidence.MutationWasAttempted && !evidence.IsVerified)
        {
            var diagnostic = evidence.Format(FormatItemState);
            if (temporary)
            {
                failureReason =
                    $"Temporary item {action.ItemId:D} is quarantined for this lifecycle after " +
                    $"an ambiguous consumable mutation: {diagnostic}";
                _temporaryQuarantine[action.ItemId] = failureReason;
            }
            else if (action.Family == AutoItemsConsumableFamily.Scroll)
            {
                _scrollQuarantineReason =
                    "Auto Items Scroll use is quarantined for this lifecycle after an ambiguous " +
                    $"consumable mutation on Scroll {action.ItemId:D}: {diagnostic}";
                failureReason = _scrollQuarantineReason;
            }
            else
            {
                _quarantineReason =
                    "Auto Items is quarantined for this lifecycle after an ambiguous consumable " +
                    $"mutation on {action.ItemId:D}: {diagnostic}";
                failureReason = _quarantineReason;
            }
            preflight = AutoItemsPreflight.Quarantined;
        }
        else if (!evidence.IsVerified)
        {
            preflight = AutoItemsPreflight.ContractUnavailable;
            failureReason =
                "Auto Items could not capture consumable before-state evidence for " +
                $"{action.ItemId:D}: {evidence.Detail}";
        }
        return new AutoItemsSubmission(
            preflight,
            evidence.Outcome,
            callOutcome,
            evidence.IsVerified
                ? $"Verified {action.Family} {action.ItemId:D}: stock -1, queue +1" +
                  (temporary ? ", usage +1." : ".")
                : failureReason);
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        _scrollQuarantineReason = string.Empty;
        _temporaryQuarantine.Clear();
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        _scrollQuarantineReason = string.Empty;
        _temporaryQuarantine.Clear();
    }

    private void BindLifecycle()
    {
        if (AutoItemsNativeBindings.TryCreate(out var bindings, out var reason))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private bool TryCaptureMutationPermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readMutationPermitFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Auto Items no longer owns ConsumableUse and NativeMultiBuyOverride.";
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            reason =
                "Auto Items could not capture its consumable-use ownership permit: " +
                ex.GetBaseException().Message;
            return false;
        }
    }

    private static bool HasExpectedFamily(
        object item,
        AutoItemsConsumableFamily expected,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (!TryReadSupportedFamilyEvidence(
                item,
                native,
                out var families,
                out reason))
        {
            return false;
        }
        if (families.TryResolveExecutionFamily(out var actual) && actual == expected)
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"Expected live execution family {expected} for the planned consumable, but " +
            $"observed supported memberships [{families.Describe()}] and resolved {actual}.";
        return false;
    }

    private static bool TryHasFinitePositiveDuration(
        object item,
        Guid itemId,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (native.HasDuration.GetValue(item) is not true)
        {
            reason = $"Temporary item {itemId:D} no longer has ConsumableSO.hasDuration=true.";
            return false;
        }
        if (native.DurationBase.GetValue(item) is not double durationBase)
        {
            reason = $"Temporary item {itemId:D} did not expose ConsumableSO.durationBase as Double.";
            return false;
        }
        if (durationBase <= 0d || double.IsNaN(durationBase) || double.IsInfinity(durationBase))
        {
            reason =
                $"Temporary item {itemId:D} has non-finite or non-positive " +
                $"ConsumableSO.durationBase={durationBase}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryHasSafeTemporaryCosts(
        object item,
        Guid itemId,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (!TryHasToxicityOnlyCosts(
                native.ConsumeCost.GetValue(item),
                "ConsumableSO.consumeCost",
                itemId,
                native,
                requireToxicity: true,
                out reason))
        {
            return false;
        }
        return TryHasToxicityOnlyCosts(
            native.UsageCost.GetValue(item),
            "ConsumableSO.usageCost",
            itemId,
            native,
            requireToxicity: false,
            out reason);
    }

    private static bool TryHasToxicityOnlyCosts(
        object? costList,
        string category,
        Guid itemId,
        AutoItemsNativeBindings native,
        bool requireToxicity,
        out string reason)
    {
        if (costList is null)
        {
            reason = $"Temporary item {itemId:D} has null {category}.";
            return false;
        }
        if (native.Costs.GetValue(costList) is not IEnumerable costs)
        {
            reason = $"Temporary item {itemId:D} has unreadable {category}.costs.";
            return false;
        }

        var hasToxicity = false;
        var index = 0;
        foreach (var entry in costs)
        {
            if (entry is null || entry.GetType() != native.CostEntryType)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}] is not the exact " +
                    "ResourceTuple type.";
                return false;
            }
            var resource = native.CostResource.GetValue(entry);
            if (resource is null || resource.GetType() != native.ResourceType)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}].resource is not " +
                    "the exact ResourceSO type.";
                return false;
            }
            var resourceId = Invoke<Guid>(native.ResourceGuid, resource);
            if (resourceId != KnownEntities.PotionToxicity.Uuid)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}] names extra resource " +
                    $"{resourceId:D}; only Potion Toxicity is permitted.";
                return false;
            }
            if (native.CostAmount.GetValue(entry) is not BigDouble amount ||
                BigDouble.IsNaN(amount) ||
                BigDouble.IsInfinity(amount) ||
                amount.CompareTo(BigDouble.Zero) < 0)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}].valueBig is invalid.";
                return false;
            }
            hasToxicity = true;
            index++;
        }

        if (requireToxicity && !hasToxicity)
        {
            reason = $"Temporary item {itemId:D} {category} has no Potion Toxicity entry.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryAnyTemporaryUsage(
        AutoItemsNativeBindings native,
        out bool present,
        out string reason)
    {
        present = false;
        if (native.AllConsumables.GetValue(null) is not IEnumerable all)
        {
            reason = "ConsumableSO.All was unavailable while checking temporary usage exclusion.";
            return false;
        }

        foreach (var candidate in all)
        {
            if (candidate is null || candidate.GetType() != native.ConsumableType)
            {
                reason =
                    "ConsumableSO.All contained a value that was not the exact ConsumableSO type.";
                return false;
            }
            if (!TryReadSupportedFamilyEvidence(
                    candidate,
                    native,
                    out var families,
                    out reason))
            {
                return false;
            }
            if (families.Count == 0) continue;
            if (!families.TryResolveExecutionFamily(out var family))
            {
                reason =
                    "A live consumable has incoherent supported family memberships [" +
                    families.Describe() + "].";
                return false;
            }
            if (!AutoItemsConsumableFamilies.IsTemporary(family)) continue;
            if (native.Usages.GetValue(candidate) is not ICollection usages)
            {
                reason =
                    $"Temporary-family consumable family={family}, " +
                    $"supportedFamilies=[{families.Describe()}] did not expose " +
                    "ConsumableSO.consumableUsages as ICollection.";
                return false;
            }
            if (usages.Count <= 0) continue;
            present = true;
            reason =
                "A native temporary-item usage is already pending or active: " +
                $"family={family}, supportedFamilies=[{families.Describe()}], usages={usages.Count}.";
            return true;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Detects the native gap where <c>Inventory.CanUseConsumable()</c> reports no item is actively
    /// preparing even though a queued item or unresolved usage still exists. Starting another use
    /// in that state can increment a queue without synchronously preparing and consuming its stock.
    /// This is read-only evidence; native queue and usage fields remain game-owned.
    /// </summary>
    private static bool TryReadQueuedOrPendingState(
        AutoItemsNativeBindings native,
        out bool occupied,
        out string reason)
    {
        occupied = false;
        if (native.AllConsumables.GetValue(null) is not IEnumerable all)
        {
            reason =
                "ConsumableSO.All was unavailable while checking queued and pending native state.";
            return false;
        }

        var queuedItems = 0;
        var queuedQuantity = 0L;
        var pendingItems = 0;
        var pendingUsages = 0L;
        foreach (var candidate in all)
        {
            if (candidate is null || candidate.GetType() != native.ConsumableType)
            {
                reason =
                    "ConsumableSO.All contained a value that was not the exact ConsumableSO type " +
                    "while checking queued and pending native state.";
                return false;
            }
            var queued = Invoke<int>(native.GetQueued, candidate);
            if (queued < 0)
            {
                reason = $"ConsumableSO.GetQueued() returned invalid quantity {queued}.";
                return false;
            }
            if (native.Usages.GetValue(candidate) is not ICollection usages)
            {
                reason =
                    "ConsumableSO.consumableUsages was unavailable while checking queued and " +
                    "pending native state.";
                return false;
            }
            if (queued > 0)
            {
                queuedItems++;
                queuedQuantity = checked(queuedQuantity + queued);
            }
            if (usages.Count > 0)
            {
                pendingItems++;
                pendingUsages = checked(pendingUsages + usages.Count);
            }
        }

        occupied = queuedQuantity > 0 || pendingUsages > 0;
        reason = occupied
            ? $"queuedItems={queuedItems}; queuedQuantity={queuedQuantity}; " +
              $"pendingItems={pendingItems}; pendingUsages={pendingUsages}."
            : string.Empty;
        return true;
    }

    private static bool TryReadSupportedFamilyEvidence(
        object item,
        AutoItemsNativeBindings native,
        out AutoItemsConsumableFamilySet families,
        out string reason)
    {
        families = new AutoItemsConsumableFamilySet();
        if (native.Families.GetValue(item) is not IList entries)
        {
            reason = "ConsumableSO.consumableTypes was unavailable on the live item.";
            return false;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.GetType() != native.FamilyType)
            {
                reason =
                    "ConsumableSO.consumableTypes contained a non-ConsumableTypeSO value.";
                return false;
            }
            var familyId = Invoke<Guid>(native.FamilyGuid, entry);
            if (familyId == Guid.Empty)
            {
                reason =
                    "ConsumableSO.consumableTypes contained an empty or duplicate stable family UUID.";
                return false;
            }
            for (var previous = 0; previous < index; previous++)
            {
                if (Invoke<Guid>(native.FamilyGuid, entries[previous]!) != familyId) continue;
                reason =
                    "ConsumableSO.consumableTypes contained an empty or duplicate stable family UUID.";
                return false;
            }
            var candidate = AutoItemsConsumableFamilies.FromTypeId(familyId);
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            if (families.TryAdd(candidate)) continue;
            reason =
                $"ConsumableSO.consumableTypes repeated supported family {candidate}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static void Mutate(
        object item,
        AutoItemsConsumableFamily family,
        AutoItemsNativeBindings native)
    {
        var stage = family == AutoItemsConsumableFamily.Scroll
            ? "SetRandomization"
            : "SelectAndFire";
        try
        {
            if (family == AutoItemsConsumableFamily.Scroll)
            {
                native.SetRandomization.Invoke(item, EnableRandomizationArguments);
                stage = "RandomizationConfirmation";
                if (!InvokeBool(native.IsRandomized, item))
                    throw new InvalidOperationException(
                        "ConsumableSO.SetRandomization(true) was not confirmed by IsRandomized().");
            }
            stage = "SelectAndFire";
            native.SelectAndFire.Invoke(item, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            // NativeMutationVerifier keeps only the base exception message. Embed the bounded
            // native exception evidence without an InnerException so it survives that boundary.
            throw new InvalidOperationException(FormatMutationException(stage, ex));
        }
    }

    private void MutateAndObserve(
        object item,
        AutoItemsConsumableFamily family,
        AutoItemsNativeBindings native,
        long lifecycle,
        long mutationFrame)
    {
        try
        {
            Mutate(item, family, native);
        }
        finally
        {
            // The verifier invokes this delegate exactly when native mutation begins. Recording in
            // finally includes thrown and ambiguous attempts, not only verified commits.
            _observeMutationAttempt(lifecycle, mutationFrame);
        }
    }

    private bool TryReadMutationFrame(out long frame, out string reason)
    {
        try
        {
            frame = _readFrameIdentity();
            if (frame >= 0)
            {
                reason = string.Empty;
                return true;
            }
            reason = "The shared frame identity was negative immediately before consumable use.";
        }
        catch (Exception ex) when (AutoItemsReflectionAccess.IsExpectedFailure(ex))
        {
            frame = 0;
            reason =
                "The shared frame identity was unavailable immediately before consumable use: " +
                ex.GetBaseException().Message;
        }
        return false;
    }

    private static bool TryHasReadyAudioPool(
        AutoItemsNativeBindings native,
        out string reason)
    {
        try
        {
            return TryHasReadyAudioPoolCore(native, out reason);
        }
        catch (Exception ex) when (AutoItemsReflectionAccess.IsExpectedFailure(ex))
        {
            reason =
                "The permanent consumable audio-readiness check failed without mutation: " +
                ex.GetBaseException().Message;
            return false;
        }
    }

    private static bool TryHasReadyAudioPoolCore(
        AutoItemsNativeBindings native,
        out string reason)
    {
        var manager = native.SoundManagerInstance.GetValue(null);
        if (manager is null || manager.GetType() != native.SoundManagerType)
        {
            reason =
                "SoundManager.instance was unavailable immediately before permanent " +
                "consumable preparation.";
            return false;
        }
        if (native.AudioMaximum.GetValue(manager) is not int maximum || maximum <= 0)
        {
            reason =
                "SoundManager.audioMaximum was not positive immediately before permanent " +
                "consumable preparation.";
            return false;
        }
        if (native.AudioElements.GetValue(manager) is not IList elements)
        {
            reason =
                "SoundManager.audioElements was unavailable immediately before permanent " +
                "consumable preparation.";
            return false;
        }
        if (elements.Count < maximum)
        {
            reason =
                $"SoundManager.audioElements contained {elements.Count} entries for " +
                $"audioMaximum={maximum} immediately before permanent consumable preparation.";
            return false;
        }
        if (native.AudioCurrentIndex.GetValue(manager) is not int currentIndex ||
            currentIndex < 0 ||
            currentIndex >= maximum)
        {
            reason =
                $"SoundManager.currentIndex was outside [0,{maximum}) immediately before " +
                "permanent consumable preparation.";
            return false;
        }

        for (var index = 0; index < maximum; index++)
        {
            var element = elements[index];
            if (element is null || element.GetType() != native.AudioElementType)
            {
                reason =
                    $"SoundManager.audioElements[{index}] was unavailable immediately before " +
                    "permanent consumable preparation.";
                return false;
            }
            var source = native.AudioSource.GetValue(element);
            if (source is null || source.GetType() != native.AudioSourceType)
            {
                reason =
                    $"AudioElement.audioSource was unavailable at pool index {index} " +
                    "immediately before permanent consumable preparation.";
                return false;
            }
        }

        var reusable = 0;
        for (var offset = 0; offset < maximum; offset++)
        {
            var index = (currentIndex + offset) % maximum;
            var element = elements[index]!;
            if (!InvokeBool(native.AudioIsPlaying, element))
            {
                reusable++;
                continue;
            }
            if (!InvokeBool(native.AudioIsLooping, element)) reusable++;
        }

        // Permanent use can pin one processing sound. Keep one more allocator entry available for
        // completion/progression sounds instead of allowing this mutation to consume the last one.
        if (reusable >= 2)
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"SoundManager.audioElements had {reusable} idle or reusable non-looping entries; " +
            "permanent consumable preparation requires two so one remains reserved.";
        return false;
    }

    private static ItemState Capture(
        object item,
        AutoItemsNativeBindings native)
    {
        if (native.Usages.GetValue(item) is not ICollection usages)
            throw new InvalidOperationException(
                "ConsumableSO.consumableUsages was unavailable during mutation evidence capture.");
        return new ItemState(
            Invoke<int>(native.StrongestLevel, item),
            Invoke<int>(native.GetQuantity, item),
            Invoke<int>(native.GetQueued, item),
            native.CurrentPrepTime.GetValue(item) is BigDouble prep
                ? prep
                : throw new InvalidOperationException(
                    "ConsumableSO.currentPrepTime was unavailable during mutation evidence capture."),
            InvokeBool(native.IsRandomized, item),
            usages.Count,
            ReadSingleUsageLevel(usages, native),
            InvokeBool(native.IsTargeting, null));
    }

    private static int ReadSingleUsageLevel(
        ICollection usages,
        AutoItemsNativeBindings native)
    {
        if (usages.Count != 1) return 0;
        foreach (var usage in usages)
        {
            if (usage is null || usage.GetType() != native.UsageType)
                throw new InvalidOperationException(
                    "ConsumableSO.consumableUsages contained a non-ConsumableUsage value.");
            var scaling = native.UsageBaseScaling.GetValue(usage) ??
                throw new InvalidOperationException(
                    "ConsumableUsage.baseSi was unavailable during mutation evidence capture.");
            return Invoke<int>(native.ScalingLevel, scaling);
        }
        return 0;
    }

    private static string FormatMutationException(string stage, Exception exception)
    {
        var inner = UnwrapInvocation(exception);
        return $"exceptionStage={stage}; wrapperType=" +
            $"{exception.GetType().FullName ?? exception.GetType().Name}; " +
            $"innerExceptionType={inner.GetType().FullName ?? inner.GetType().Name}; " +
            $"innerExceptionMessage={Bound(inner.Message, MaximumExceptionMessageCharacters)}; " +
            $"innerExceptionStack={Bound(inner.StackTrace ?? "<unavailable>", MaximumExceptionStackCharacters)}";
    }

    private static Exception UnwrapInvocation(Exception exception)
    {
        var current = exception;
        while (current is TargetInvocationException { InnerException: not null } invocation)
            current = invocation.InnerException;
        return current;
    }

    private static string Bound(string value, int maximum) => value.Length <= maximum
        ? value
        : value.Substring(0, maximum) + "...[truncated]";

    private static string FormatItemState(ItemState state) =>
        $"level={state.Level},qty={state.Quantity},queued={state.Queued}," +
        $"prep={state.Preparation},usages={state.Usages},usageLevel={state.UsageLevel}," +
        $"randomized={state.Randomized},targeting={state.Targeting}";

    private static bool InvokeBool(MethodInfo method, object? target) =>
        Invoke<bool>(method, target);

    private static T Invoke<T>(MethodInfo method, object? target) =>
        method.Invoke(target, Array.Empty<object>()) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} did not return {typeof(T).Name}.");

    private readonly struct ItemState
    {
        internal ItemState(
            int level,
            int quantity,
            int queued,
            BigDouble preparation,
            bool randomized,
            int usages,
            int usageLevel,
            bool targeting)
        {
            Level = level;
            Quantity = quantity;
            Queued = queued;
            Preparation = preparation;
            Randomized = randomized;
            Usages = usages;
            UsageLevel = usageLevel;
            Targeting = targeting;
        }

        internal int Level { get; }
        internal int Quantity { get; }
        internal int Queued { get; }
        internal BigDouble Preparation { get; }
        internal bool Randomized { get; }
        internal int Usages { get; }
        internal int UsageLevel { get; }
        internal bool Targeting { get; }
    }
}
