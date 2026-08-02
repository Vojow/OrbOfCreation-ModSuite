using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-scoped re-drive of the native Discovery Tree offer pipeline. All policy, identity,
/// affordability, and evidence reads complete before a mutation permit is captured. Verification
/// gates target identity and the requested native outcome; accounting observations remain evidence.
/// </summary>
internal sealed class DiscoveryTreeOfferGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private DiscoveryTreeOfferNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal DiscoveryTreeOfferGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal DiscoveryTreeOfferSubmission Submit(in DiscoveryTreeOfferAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.WrongThread,
                $"Discovery Tree offers are bound to Unity thread {_mainThreadId}, not thread {Environment.CurrentManagedThreadId}.");
        if (_bindings is not { } native)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped Discovery Tree offer binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try
        {
            currentEpoch = _readLifecycleEpoch();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != currentEpoch)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.LifecycleReplaced,
                $"Action lifecycle {action.LifecycleEpoch} is stale; the live lifecycle is {currentEpoch}.");

        try
        {
            if (!TryResolveTree(native, action.TreeId, out var tree, out var reason))
                return DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferPreflight.IdentityUnavailable, reason);
            if (!native.IsVisible(tree))
                return DiscoveryTreeOfferSubmission.Reject(
                    DiscoveryTreeOfferPreflight.TreeUnavailable,
                    $"DiscoveryTreeSO.IsVisible() refused tree {EntityIdentityFormatter.Format(action.TreeId)}.");

            return action.Kind switch
            {
                DiscoveryTreeOfferActionKind.Initiate => SubmitInitiate(in action, native, tree),
                DiscoveryTreeOfferActionKind.Select => SubmitSelect(in action, native, tree),
                DiscoveryTreeOfferActionKind.Confirm => SubmitConfirm(in action, native, tree),
                DiscoveryTreeOfferActionKind.Reroll => SubmitReroll(in action, native, tree),
                _ => DiscoveryTreeOfferSubmission.Reject(
                    DiscoveryTreeOfferPreflight.ContractUnavailable,
                    $"Unknown Discovery Tree offer action kind {(int)action.Kind}."),
            };
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                "Discovery Tree offer preflight failed before mutation: " + ex.GetBaseException().Message);
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private DiscoveryTreeOfferSubmission SubmitInitiate(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsIdle(tree))
            return WrongMode(action.Kind, "Idle");
        if (!native.HasRemainingDiscoveries(tree) && !native.HasImmediateRequired(tree))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.NoDiscoveries,
                "The native tree reports neither a remaining main-pool discovery nor an immediate required discovery.");

        var cost = native.GetNextCost(tree);
        if (cost is null || cost.GetType() != native.CostType)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                "DiscoveryTreeSO.GetNextItemCost() returned a non-ResourceCostList value.");
        if (!native.HasEnough(cost))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.Unaffordable,
                $"GetNextItemCost().HasEnough() refused tree {EntityIdentityFormatter.Format(action.TreeId)}.");

        var before = CaptureState(native, tree, Guid.Empty);
        var costs = CaptureCosts(native, cost);
        if (!TryCapturePermit(out var reason))
            return DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);

        var stage = DiscoveryTreeOfferNativeStage.Payment;
        var nativeCalls = 0;
        var paymentInvoked = false;
        try
        {
            paymentInvoked = true;
            nativeCalls = 1;
            native.PerformCost(cost);
            stage = DiscoveryTreeOfferNativeStage.Initiate;
            nativeCalls = 2;
            native.Initiate(tree);
            stage = DiscoveryTreeOfferNativeStage.Verification;
            var after = CaptureState(native, tree, Guid.Empty);
            var receipt = BuildReceipt(native, cost, in before, in after, costs, paymentInvoked,
                offersPendingNativeIncrement: true, postcondition: false);
            var verified = native.IsCrafting(tree);
            receipt = WithPostcondition(in receipt, verified);
            return verified
                ? Verified(stage, nativeCalls, in receipt,
                    "Verified the requested transition to Crafting; payment, rerolls, counters, flags, timer, and pending offers are receipt evidence.")
                : Fault(in action, DiscoveryTreeOfferPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls, in receipt,
                    $"Initiate expected Crafting mode for tree {EntityIdentityFormatter.Format(action.TreeId)}, observed {after.Mode}.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var receipt = CaptureReceiptBestEffort(native, tree, Guid.Empty, cost, in before, costs,
                paymentInvoked, offersPending: true);
            if (receipt.EvidenceAvailable && IsCraftingBestEffort(native, tree))
            {
                receipt = WithPostcondition(in receipt, true);
                return Verified(DiscoveryTreeOfferNativeStage.Verification, nativeCalls, in receipt,
                    "The native call threw after the requested Crafting transition landed; the exception and all accounting observations remain receipt evidence.");
            }
            return Fault(in action, DiscoveryTreeOfferPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, nativeCalls, in receipt,
                "Native initiate threw before the requested Crafting transition was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private DiscoveryTreeOfferSubmission SubmitSelect(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree)) return WrongMode(action.Kind, "Choice");
        if (!TryResolveOfferedItem(native, tree, action.OfferId, out var item, out var reason, out var rejection))
            return DiscoveryTreeOfferSubmission.Reject(rejection, reason);
        var before = CaptureState(native, tree, action.OfferId);
        if (!TryCapturePermit(out reason))
            return DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);
        var offerId = action.OfferId;

        return ExecuteSingle(in action, native, tree, before,
            DiscoveryTreeOfferNativeStage.Select,
            () => native.Select(tree, offerId),
            after => after.SelectedChoice == offerId,
            "Verified that the requested offered UUID became the native selection; all other observations are evidence.");
    }

    private DiscoveryTreeOfferSubmission SubmitConfirm(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree)) return WrongMode(action.Kind, "Choice");
        if (!TryResolveOfferedItem(native, tree, action.OfferId, out var item, out var reason, out var rejection))
            return DiscoveryTreeOfferSubmission.Reject(rejection, reason);
        var selected = ReadGuid(native, native.ReadSelected(tree));
        if (selected != action.OfferId)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.OfferUnavailable,
                $"Confirm target {EntityIdentityFormatter.Format(action.OfferId)} is not the native selected offer {EntityIdentityFormatter.Format(selected)}.");
        var before = CaptureState(native, tree, action.OfferId);
        if (!TryCapturePermit(out reason))
            return DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);

        return ExecuteSingle(in action, native, tree, before,
            DiscoveryTreeOfferNativeStage.Confirm,
            () => native.Confirm(tree),
            after => after.TargetResolved && after.TargetDiscovered,
            "Verified that the requested UUID became discovered; mode, cleanup, counts, and rerolls are evidence.");
    }

    private DiscoveryTreeOfferSubmission SubmitReroll(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree)) return WrongMode(action.Kind, "Choice");
        if (native.HasImmediateRequired(tree))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.RerollUnavailable,
                "The immediate-required discovery path does not expose reroll in the native UI.");
        var before = CaptureState(native, tree, Guid.Empty);
        if (before.Rerolls <= 0 || before.CurrentChoices.Length == 0)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.RerollUnavailable,
                $"Reroll requires Choice mode, at least one offer, and rerollsLeft > 0; observed offers={before.CurrentChoices.Length}, rerolls={before.Rerolls}.");
        if (!TryCapturePermit(out var reason))
            return DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);

        var stage = DiscoveryTreeOfferNativeStage.Reroll;
        var nativeCalls = 0;
        try
        {
            nativeCalls = 1;
            native.Reroll(tree);
            // The native data method deliberately leaves the old selected UUID in place while new
            // offers are crafting. Clearing it is cleanup evidence, not the reroll outcome gate.
            stage = DiscoveryTreeOfferNativeStage.ClearSelection;
            nativeCalls = 2;
            try
            {
                native.Select(tree, Guid.Empty);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                reason = "Selection cleanup threw after reroll: " + ex.GetBaseException().Message;
            }
            stage = DiscoveryTreeOfferNativeStage.Verification;
            var after = CaptureState(native, tree, Guid.Empty);
            var verified = native.IsCrafting(tree);
            var receipt = new DiscoveryTreeOfferMutationReceipt(
                true, false, false, verified, true, in before, in after,
                Array.Empty<DiscoveryTreeCostReceipt>());
            return verified
                ? Verified(stage, nativeCalls, in receipt,
                    "Verified the requested reroll transition to Crafting; debit, exclusions, selection cleanup, flags, timer, and counts are evidence." +
                    (reason.Length == 0 ? string.Empty : " " + reason))
                : Fault(in action, DiscoveryTreeOfferPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed, nativeCalls, in receipt,
                    $"Reroll expected Crafting mode for tree {EntityIdentityFormatter.Format(action.TreeId)}, observed {after.Mode}.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var receipt = CaptureReceiptBestEffort(native, tree, Guid.Empty, null, in before,
                Array.Empty<CostBefore>(), paymentInvoked: false, offersPending: true);
            if (receipt.EvidenceAvailable && IsCraftingBestEffort(native, tree))
            {
                receipt = WithPostcondition(in receipt, true);
                return Verified(DiscoveryTreeOfferNativeStage.Verification, nativeCalls, in receipt,
                    "The native call threw after the requested Crafting transition landed; the exception and accounting observations remain receipt evidence.");
            }
            return Fault(in action, DiscoveryTreeOfferPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, nativeCalls, in receipt,
                "Native reroll threw before the requested Crafting transition was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private DiscoveryTreeOfferSubmission ExecuteSingle(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree,
        in DiscoveryTreeOfferState before,
        DiscoveryTreeOfferNativeStage stage,
        Action execute,
        Func<DiscoveryTreeOfferState, bool> verify,
        string success)
    {
        try
        {
            execute();
            var after = CaptureState(native, tree, action.OfferId);
            var matched = verify(after);
            var receipt = new DiscoveryTreeOfferMutationReceipt(
                true, false, false, matched, false, in before, in after,
                Array.Empty<DiscoveryTreeCostReceipt>());
            return matched
                ? Verified(DiscoveryTreeOfferNativeStage.Verification, 1, in receipt, success)
                : Fault(in action, DiscoveryTreeOfferPreflight.VerificationFailed,
                    DiscoveryTreeOfferNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
                    1, in receipt, $"{action.Kind} postconditions did not match the audited native transition.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var receipt = CaptureReceiptBestEffort(native, tree, action.OfferId, null, in before,
                Array.Empty<CostBefore>(), paymentInvoked: false, offersPending: false);
            if (receipt.EvidenceAvailable && verify(receipt.After))
            {
                receipt = WithPostcondition(in receipt, true);
                return Verified(DiscoveryTreeOfferNativeStage.Verification, 1, in receipt,
                    $"The native {action.Kind} call threw after the requested outcome landed; the exception and accounting observations remain receipt evidence.");
            }
            return Fault(in action, DiscoveryTreeOfferPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, 1, in receipt,
                $"Native {action.Kind} threw before the requested outcome was observable: {ex.GetBaseException().Message}");
        }
    }

    private static DiscoveryTreeOfferSubmission Verified(
        DiscoveryTreeOfferNativeStage stage,
        int nativeCalls,
        in DiscoveryTreeOfferMutationReceipt receipt,
        string reason) =>
        new(DiscoveryTreeOfferPreflight.Proceeded, stage, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(nativeCalls, 1, 1), in receipt, reason);

    private static DiscoveryTreeOfferSubmission Fault(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferPreflight preflight,
        DiscoveryTreeOfferNativeStage stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        in DiscoveryTreeOfferMutationReceipt receipt,
        string reason)
    {
        var exactReason = $"Discovery Tree offer {stage} failed on tree " +
            $"{EntityIdentityFormatter.Format(action.TreeId)}: {reason}";
        return new DiscoveryTreeOfferSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(nativeCalls, 1, 0), in receipt, exactReason);
    }

    private static DiscoveryTreeOfferSubmission WrongMode(
        DiscoveryTreeOfferActionKind kind,
        string expected) =>
        DiscoveryTreeOfferSubmission.Reject(
            DiscoveryTreeOfferPreflight.WrongMode,
            $"{kind} requires native {expected} mode.");

    private static bool TryResolveTree(
        DiscoveryTreeOfferNativeBindings native,
        Guid treeId,
        out object tree,
        out string reason)
    {
        tree = null!;
        var matches = 0;
        foreach (var value in native.ReadTrees())
        {
            if (value is null || value.GetType() != native.TreeType) continue;
            if (native.ReadTreeIdentity(value) != treeId) continue;
            tree = value;
            matches++;
        }
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? $"No exact DiscoveryTreeSO with identity {EntityIdentityFormatter.Format(treeId)} exists in the live registry."
            : $"DiscoveryTreeSO identity {EntityIdentityFormatter.Format(treeId)} is ambiguous across {matches} exact live instances.";
        return false;
    }

    private static bool TryResolveOfferedItem(
        DiscoveryTreeOfferNativeBindings native,
        object tree,
        Guid offerId,
        out object item,
        out string reason,
        out DiscoveryTreeOfferPreflight rejection)
    {
        item = null!;
        if (!Contains(native, native.ReadCurrentChoices(tree), offerId))
        {
            reason = $"Identity {EntityIdentityFormatter.Format(offerId)} is not in the tree's current native offer set.";
            rejection = DiscoveryTreeOfferPreflight.OfferUnavailable;
            return false;
        }
        var resolved = native.GetItem(tree, offerId);
        if (resolved is null || !native.ItemType.IsInstanceOfType(resolved) ||
            native.ReadItemIdentity(resolved) != offerId)
        {
            reason = $"Current offer {EntityIdentityFormatter.Format(offerId)} did not resolve to one exact IDiscoverable identity.";
            rejection = DiscoveryTreeOfferPreflight.IdentityUnavailable;
            return false;
        }
        if (native.IsItemDiscovered(resolved))
        {
            reason = $"Current offer {EntityIdentityFormatter.Format(offerId)} is already discovered.";
            rejection = DiscoveryTreeOfferPreflight.AlreadyDiscovered;
            return false;
        }
        item = resolved;
        reason = string.Empty;
        rejection = DiscoveryTreeOfferPreflight.Proceeded;
        return true;
    }

    private static DiscoveryTreeOfferState CaptureState(
        DiscoveryTreeOfferNativeBindings native,
        object tree,
        Guid targetId)
    {
        var target = targetId == Guid.Empty ? null : native.GetItem(tree, targetId);
        var targetResolved = target is not null && native.ItemType.IsInstanceOfType(target) &&
            native.ReadItemIdentity(target) == targetId;
        return new DiscoveryTreeOfferState(
            native.ReadMode(tree),
            native.ReadActionTime(tree),
            native.ReadRerolls(tree),
            native.GetMaxRerolls(tree),
            native.ReadUsedRerolls(tree),
            ReadGuids(native, native.ReadCurrentChoices(tree)),
            ReadGuids(native, native.ReadNextExclusions(tree)),
            ReadGuid(native, native.ReadSelected(tree)),
            native.ReadTotalDiscovered(tree),
            native.ReadPoolDiscovered(tree),
            targetResolved,
            targetResolved && native.IsItemDiscovered(target!),
            targetResolved && native.IsItemRequired(target!));
    }

    private static Guid[] ReadGuids(DiscoveryTreeOfferNativeBindings native, IList values)
    {
        var result = new Guid[values.Count];
        for (var index = 0; index < values.Count; index++)
            result[index] = ReadGuid(native, values[index]);
        return result;
    }

    private static Guid ReadGuid(DiscoveryTreeOfferNativeBindings native, object? value)
    {
        if (value is null) throw new InvalidOperationException("A GuidContainer was null.");
        return native.ReadGuid(value);
    }

    private static bool Contains(DiscoveryTreeOfferNativeBindings native, IList values, Guid id)
    {
        for (var index = 0; index < values.Count; index++)
            if (ReadGuid(native, values[index]) == id) return true;
        return false;
    }

    private static CostBefore[] CaptureCosts(DiscoveryTreeOfferNativeBindings native, object cost)
    {
        var entries = native.GetCostEntries(cost);
        var aggregate = new Dictionary<Guid, CostBefore>();
        for (var index = 0; index < entries.Count; index++)
        {
            var tuple = entries[index];
            if (tuple is null || tuple.GetType() != native.TupleType)
                throw new InvalidOperationException("ResourceCostList.GetEntries contained a non-ResourceTuple value.");
            var resource = native.ReadTupleResource(tuple);
            if (resource is null || resource.GetType() != native.ResourceType)
                throw new InvalidOperationException("ResourceTuple.resource changed native type.");
            var id = native.ReadResourceIdentity(resource);
            var expected = native.ReadTupleValue(tuple);
            var quantity = native.ReadResourceQuantity(resource);
            if (aggregate.TryGetValue(id, out var prior))
            {
                if (prior.Quantity.CompareTo(quantity) != 0)
                    throw new InvalidOperationException($"Resource {EntityIdentityFormatter.Format(id)} quantity changed during cost capture.");
                aggregate[id] = new CostBefore(id, prior.Expected + expected, quantity, resource);
            }
            else
            {
                aggregate.Add(id, new CostBefore(id, expected, quantity, resource));
            }
        }
        var result = new CostBefore[aggregate.Count];
        var offset = 0;
        foreach (var value in aggregate.Values) result[offset++] = value;
        Array.Sort(result, static (left, right) => left.ResourceId.CompareTo(right.ResourceId));
        return result;
    }

    private static DiscoveryTreeOfferMutationReceipt BuildReceipt(
        DiscoveryTreeOfferNativeBindings native,
        object cost,
        in DiscoveryTreeOfferState before,
        in DiscoveryTreeOfferState after,
        CostBefore[] costs,
        bool paymentInvoked,
        bool offersPendingNativeIncrement,
        bool postcondition)
    {
        var receipts = new DiscoveryTreeCostReceipt[costs.Length];
        var charged = false;
        for (var index = 0; index < costs.Length; index++)
        {
            var current = native.ReadResourceQuantity(costs[index].Resource);
            receipts[index] = new DiscoveryTreeCostReceipt(
                costs[index].ResourceId, costs[index].Expected, costs[index].Quantity, current);
            var delta = costs[index].Quantity - current;
            if (delta.CompareTo(BigDouble.Zero) > 0) charged = true;
        }
        return new DiscoveryTreeOfferMutationReceipt(
            true, paymentInvoked, charged, postcondition, offersPendingNativeIncrement,
            in before, in after, receipts);
    }

    private static DiscoveryTreeOfferMutationReceipt WithPostcondition(
        in DiscoveryTreeOfferMutationReceipt receipt,
        bool matched)
    {
        var before = receipt.Before;
        var after = receipt.After;
        return new DiscoveryTreeOfferMutationReceipt(
            receipt.EvidenceAvailable, receipt.PaymentInvoked, receipt.ResourcesCharged,
            matched, receipt.OffersPendingNativeIncrement,
            in before, in after, receipt.Costs);
    }

    private static DiscoveryTreeOfferMutationReceipt CaptureReceiptBestEffort(
        DiscoveryTreeOfferNativeBindings native,
        object tree,
        Guid targetId,
        object? cost,
        in DiscoveryTreeOfferState before,
        CostBefore[] costs,
        bool paymentInvoked,
        bool offersPending)
    {
        try
        {
            var after = CaptureState(native, tree, targetId);
            if (cost is not null)
                return BuildReceipt(native, cost, in before, in after, costs, paymentInvoked,
                    offersPending, postcondition: false);
            return new DiscoveryTreeOfferMutationReceipt(
                true, paymentInvoked, false, false, offersPending,
                in before, in after, Array.Empty<DiscoveryTreeCostReceipt>());
        }
        catch (Exception) when (paymentInvoked || costs.Length == 0)
        {
            return new DiscoveryTreeOfferMutationReceipt(
                false, paymentInvoked, false, false, offersPending,
                in before, in before, Array.Empty<DiscoveryTreeCostReceipt>());
        }
    }

    private bool TryCapturePermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readOwnershipFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "The suite no longer owns DiscoveryTreeOfferLifecycle.";
            return false;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            reason = "The Discovery Tree mutation permit could not be captured: " + ex.GetBaseException().Message;
            return false;
        }
    }

    private void BindLifecycle()
    {
        if (DiscoveryTreeOfferNativeBindings.TryCreate(
                out var bindings, out var reason, _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsCraftingBestEffort(DiscoveryTreeOfferNativeBindings native, object tree)
    {
        try
        {
            return native.IsCrafting(tree);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    private static bool IsExpected(Exception exception) => exception is not
        StackOverflowException and not
        OutOfMemoryException and not
        AccessViolationException;

    private readonly struct CostBefore
    {
        internal CostBefore(Guid resourceId, BigDouble expected, BigDouble quantity, object resource)
        {
            ResourceId = resourceId;
            Expected = expected;
            Quantity = quantity;
            Resource = resource;
        }

        internal Guid ResourceId { get; }
        internal BigDouble Expected { get; }
        internal BigDouble Quantity { get; }
        internal object Resource { get; }
    }
}
