using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoAgromancyBalanceDisposition
{
    Applied = 1,
    Rejected = 2,
    ContractUnavailable = 3,
    MutationUnverified = 4,
    NotApplicable = 5,
}

internal readonly struct AutoAgromancyBalanceResult
{
    internal AutoAgromancyBalanceResult(
        AutoAgromancyBalanceDisposition disposition,
        string actionId,
        string elementId,
        int previousLevel,
        int targetLevel,
        int maximumLevel,
        string limitingResource,
        BigAmount limitingRate,
        string reason)
    {
        Disposition = disposition;
        ActionId = actionId ?? string.Empty;
        ElementId = elementId ?? string.Empty;
        PreviousLevel = previousLevel;
        TargetLevel = targetLevel;
        MaximumLevel = maximumLevel;
        LimitingResource = limitingResource ?? string.Empty;
        LimitingRate = limitingRate;
        Reason = reason ?? string.Empty;
    }

    internal AutoAgromancyBalanceDisposition Disposition { get; }
    internal string ActionId { get; }
    internal string ElementId { get; }
    internal int PreviousLevel { get; }
    internal int TargetLevel { get; }
    internal int MaximumLevel { get; }
    internal string LimitingResource { get; }
    internal BigAmount LimitingRate { get; }
    internal string Reason { get; }
}

internal readonly struct AutoAgromancyActiveLevel
{
    internal AutoAgromancyActiveLevel(
        string actionId,
        string elementId,
        int level,
        object selected)
    {
        ActionId = actionId;
        ElementId = elementId;
        Level = level;
        Selected = selected;
    }

    internal string ActionId { get; }
    internal string ElementId { get; }
    internal int Level { get; }
    internal object Selected { get; }
}

internal sealed class AutoAgromancyNativeAdapter
{
    internal const string ActiveHarvestActionsId =
        "e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32";
    private static readonly TimeSpan CaptureBudget = TimeSpan.FromMilliseconds(25);

    private readonly Func<long> _readLifecycle;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly TypedRegistryResolver _registryResolver;
    private readonly Contract? _contract;
    private readonly string _contractFailure;

    internal AutoAgromancyNativeAdapter(
        Func<long> readLifecycle,
        Func<bool> tryCaptureMutationPermit,
        TypedRegistryResolver? registryResolver = null)
    {
        _readLifecycle = readLifecycle ?? throw new ArgumentNullException(nameof(readLifecycle));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
        Contract.TryCreate(out _contract, out var balanceFailure);
        if (_contract is null)
            _contractFailure = "Auto Agromancy native contract is unavailable: " + balanceFailure;
        else
            _contractFailure = string.Empty;
    }

    internal bool ContractAvailable => _contract is not null;
    internal string ContractFailure => _contractFailure;

    internal AutoAgromancyBalanceResult Balance(object uiList, object selected)
    {
        if (_contract is null)
            return Failure(AutoAgromancyBalanceDisposition.ContractUnavailable, _contractFailure);
        if (uiList is null || selected is null)
            return Failure(AutoAgromancyBalanceDisposition.NotApplicable, "the clicked native selection is missing");
        if (!IsAuditedAddSide(uiList))
        {
            return Failure(
                AutoAgromancyBalanceDisposition.NotApplicable,
                "the click is not on the audited Agromancy add list");
        }
        if (!TryGetActionList(_contract, uiList, out var actionList))
        {
            return Failure(
                AutoAgromancyBalanceDisposition.NotApplicable,
                "the audited Agromancy action list is unavailable");
        }
        return BalanceSelection(actionList, selected, uiList);
    }

    internal AutoAgromancyBalanceResult BalanceActiveSelection(
        object selected,
        int previousLevel)
    {
        if (_contract is null)
            return Failure(AutoAgromancyBalanceDisposition.ContractUnavailable, _contractFailure);
        if (selected is null)
            return Failure(AutoAgromancyBalanceDisposition.NotApplicable, "the active Agromancy selection is missing");
        if (!TryResolveActiveList(_contract, out var actionList, out var resolutionFailure))
        {
            return Failure(
                AutoAgromancyBalanceDisposition.NotApplicable,
                resolutionFailure);
        }
        var active = _contract.FindInstance.Invoke(actionList, new[] { selected });
        if (!ReferenceEquals(active, selected))
        {
            return Failure(
                AutoAgromancyBalanceDisposition.NotApplicable,
                "the selected instance is not in the audited ActiveHarvestActions list");
        }
        var result = BalanceSelection(actionList, selected, uiList: null);
        if (result.Disposition == AutoAgromancyBalanceDisposition.Rejected &&
            !TryRestoreObservedLevel(_contract, actionList, selected, previousLevel))
        {
            return Failure(
                AutoAgromancyBalanceDisposition.MutationUnverified,
                "the rejected player level change could not be restored");
        }
        return result;
    }

    internal bool TryCaptureActiveLevels(
        out IReadOnlyList<AutoAgromancyActiveLevel> active,
        out string reason)
    {
        active = Array.Empty<AutoAgromancyActiveLevel>();
        var contract = _contract;
        if (contract is null)
        {
            reason = _contractFailure;
            return false;
        }
        if (!TryResolveActiveList(contract, out var actionList, out reason))
            return false;
        if (contract.ListValuesField.GetValue(actionList) is not IList values)
            return Fail("the active Agromancy action entries are unavailable", out reason);

        var captured = new List<AutoAgromancyActiveLevel>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null || !contract.InstanceType.IsInstanceOfType(candidate))
                continue;
            var level = ReadInt(contract.InstancesField, candidate);
            if (level <= 0) continue;
            var action = contract.GetAction.Invoke(candidate, Array.Empty<object>());
            var element = contract.GetElement.Invoke(candidate, Array.Empty<object>());
            if (action is null ||
                element is null ||
                !TryStableId(action, out var actionId) ||
                !TryStableId(element, out var elementId))
                return Fail("an active Agromancy pair identity is unavailable", out reason);
            captured.Add(new AutoAgromancyActiveLevel(
                actionId,
                elementId,
                level,
                candidate));
        }
        active = captured;
        reason = string.Empty;
        return true;
    }

    internal IReadOnlyList<AutoAgromancyBalanceResult> BalanceActive()
    {
        if (_contract is null)
        {
            return new[]
            {
                Failure(
                    AutoAgromancyBalanceDisposition.ContractUnavailable,
                    _contractFailure),
            };
        }

        var resolution = _registryResolver.Resolve(
            Guid.Parse(ActiveHarvestActionsId),
            _contract.ListType);
        if (!resolution.IsResolved || !_registryResolver.IsCurrent(resolution))
        {
            return new[]
            {
                Failure(
                    AutoAgromancyBalanceDisposition.Rejected,
                    "the active Agromancy action list is unavailable: " +
                    resolution.Reason),
            };
        }

        var actionList = resolution.Value!;
        if (_contract.ListValuesField.GetValue(actionList) is not IList values)
        {
            return new[]
            {
                Failure(
                    AutoAgromancyBalanceDisposition.ContractUnavailable,
                    "the active Agromancy action entries are unavailable"),
            };
        }

        var selected = new List<object>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null || !_contract.InstanceType.IsInstanceOfType(candidate))
                continue;
            if (ReadInt(_contract.InstancesField, candidate) > 0)
                selected.Add(candidate);
        }

        var results = new List<AutoAgromancyBalanceResult>(selected.Count);
        for (var index = 0; index < selected.Count; index++)
        {
            var result = BalanceSelection(actionList, selected[index], uiList: null);
            results.Add(result);
            if (result.Disposition is
                AutoAgromancyBalanceDisposition.MutationUnverified or
                AutoAgromancyBalanceDisposition.ContractUnavailable)
                break;
        }
        return results;
    }

    private AutoAgromancyBalanceResult BalanceSelection(
        object actionList,
        object selected,
        object? uiList)
    {
        var admitted = default(Capture);
        var mutationStarted = false;
        try
        {
            var lifecycle = _readLifecycle();
            if (lifecycle <= 0)
                return Failure(AutoAgromancyBalanceDisposition.Rejected, "gameplay lifecycle is not ready");
            if (!TryCapture(actionList, selected, out var first, out var firstFailure))
                return Failure(AutoAgromancyBalanceDisposition.Rejected, firstFailure);

            var plan = AutoAgromancyLevelPlanner.Plan(
                first.MaximumLevel,
                first.Resources,
                first.Levels);
            if (!plan.HasTarget)
                return FromPlan(first, plan);

            if (!TryCapture(actionList, selected, out var current, out var currentFailure))
                return Failure(AutoAgromancyBalanceDisposition.Rejected, currentFailure, first);
            var currentPlan = AutoAgromancyLevelPlanner.Plan(
                current.MaximumLevel,
                current.Resources,
                current.Levels);
            if (!currentPlan.HasTarget)
                return FromPlan(current, currentPlan);
            if (_readLifecycle() != lifecycle || !Equivalent(first, current) ||
                plan.TargetLevel != currentPlan.TargetLevel)
            {
                return Failure(
                    AutoAgromancyBalanceDisposition.Rejected,
                    "native state changed during Auto Agromancy admission",
                    current);
            }
            if (!_tryCaptureMutationPermit())
            {
                return Failure(
                    AutoAgromancyBalanceDisposition.Rejected,
                    "Agromancy action ownership is unavailable",
                    current);
            }

            admitted = current;
            mutationStarted = true;
            if (!TryApply(current, currentPlan.TargetLevel, out var mutationFailure))
            {
                TryRollback(current);
                return Failure(
                    AutoAgromancyBalanceDisposition.MutationUnverified,
                    mutationFailure,
                    current,
                    currentPlan.TargetLevel);
            }

            if (!TryVerifyObservedRates(current, out var feedbackFailure))
            {
                TryRollback(current);
                return Failure(
                    AutoAgromancyBalanceDisposition.MutationUnverified,
                    feedbackFailure,
                    current,
                    currentPlan.TargetLevel);
            }

            if (uiList is not null)
                TryPresentSuccess(uiList, selected, current.Action);
            return Success(current, currentPlan);
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            if (mutationStarted)
            {
                TryRollback(admitted);
                return Failure(
                    AutoAgromancyBalanceDisposition.MutationUnverified,
                    "native Auto Agromancy mutation could not be verified: " +
                    exception.GetBaseException().Message,
                    admitted);
            }
            return Failure(
                AutoAgromancyBalanceDisposition.ContractUnavailable,
                "native Auto Agromancy access failed: " +
                exception.GetBaseException().Message);
        }
    }

    internal bool IsAuditedAddSide(object uiList)
    {
        var contract = _contract;
        if (contract is null || uiList is null) return false;
        if (!TryGetActionList(contract, uiList, out var actionList)) return false;
        return
            TryStableId(actionList, out var listId) &&
            string.Equals(listId, ActiveHarvestActionsId, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveActiveList(
        Contract contract,
        out object actionList,
        out string reason)
    {
        actionList = null!;
        var resolution = _registryResolver.Resolve(
            Guid.Parse(ActiveHarvestActionsId),
            contract.ListType);
        if (!resolution.IsResolved || !_registryResolver.IsCurrent(resolution))
        {
            reason =
                "the active Agromancy action list is unavailable: " +
                resolution.Reason;
            return false;
        }
        actionList = resolution.Value!;
        reason = string.Empty;
        return true;
    }

    private static bool TryRestoreObservedLevel(
        Contract contract,
        object actionList,
        object selected,
        int previousLevel)
    {
        try
        {
            var active = contract.FindInstance.Invoke(actionList, new[] { selected });
            if (!ReferenceEquals(active, selected)) return previousLevel == 0;
            var current = ReadInt(contract.InstancesField, selected);
            if (previousLevel < 0 || previousLevel >= current) return false;
            if (previousLevel == 0)
            {
                contract.RemoveInstance.Invoke(
                    actionList,
                    new object[] { selected, current });
                return contract.FindInstance.Invoke(actionList, new[] { selected }) is null;
            }
            contract.ChangeInstance.Invoke(
                selected,
                new object[] { previousLevel - current });
            return
                ReferenceEquals(
                    contract.FindInstance.Invoke(actionList, new[] { selected }),
                    selected) &&
                ReadInt(contract.InstancesField, selected) == previousLevel;
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            return false;
        }
    }

    private static bool TryGetActionList(
        Contract contract,
        object uiContext,
        out object actionList)
    {
        actionList = null!;
        object? candidate;
        if (contract.UiListType.IsInstanceOfType(uiContext))
            candidate = contract.ActionListField.GetValue(uiContext);
        else if (contract.UiRowType.IsInstanceOfType(uiContext))
            candidate = contract.RowActionListField.GetValue(uiContext);
        else
            return false;
        if (candidate is null || !contract.ListType.IsInstanceOfType(candidate))
            return false;
        actionList = candidate;
        return true;
    }

    private bool TryCapture(
        object actionList,
        object selected,
        out Capture capture,
        out string reason)
    {
        capture = default;
        var contract = _contract!;
        var timer = Stopwatch.StartNew();
        if (!contract.InstanceType.IsInstanceOfType(selected))
            return Fail("the selected action has an unexpected native type", out reason);
        if (!contract.ListType.IsInstanceOfType(actionList))
            return Fail("the active Agromancy action list is unavailable", out reason);
        if (!TryStableId(actionList, out var listId) ||
            !string.Equals(listId, ActiveHarvestActionsId, StringComparison.OrdinalIgnoreCase))
            return Fail("the clicked list is not the audited ActiveHarvestActions list", out reason);

        var action = contract.GetAction.Invoke(selected, Array.Empty<object>());
        var element = contract.GetElement.Invoke(selected, Array.Empty<object>());
        if (action is null || element is null ||
            !contract.ActionType.IsInstanceOfType(action) ||
            !contract.ElementType.IsInstanceOfType(element) ||
            !TryStableId(action, out var actionId) ||
            !TryStableId(element, out var elementId))
            return Fail("the selected action or element identity is unavailable", out reason);
        if (contract.IsVisible.Invoke(selected, Array.Empty<object>()) is not true)
            return Fail("the selected Agromancy action is not visible", out reason);

        var maximum = InvokeInt(contract.GetMaximumInstances, selected);
        if (maximum <= 0 || maximum > AutoAgromancyLevelPlanner.MaximumExactLevels)
            return Fail($"maximum native level {maximum} is outside the exact-search bound", out reason);

        var existing = contract.FindInstance.Invoke(actionList, new[] { selected });
        if (existing is not null && !contract.InstanceType.IsInstanceOfType(existing))
            return Fail("the active action list returned an unexpected instance type", out reason);
        var previousLevel = existing is null ? 0 : ReadInt(contract.InstancesField, existing);
        if (previousLevel < 0 || previousLevel > maximum)
            return Fail("the current native action level is invalid", out reason);
        if (existing is null && contract.HasEmptySpot.Invoke(actionList, Array.Empty<object>()) is not true)
            return Fail("no Agromancy action slot is available", out reason);

        var actionReference = contract.GetActionRef.Invoke(selected, Array.Empty<object>());
        if (actionReference is null ||
            !contract.ActionReferenceType.IsInstanceOfType(actionReference))
            return Fail("the selected action reference is unavailable", out reason);
        if (!TryReadBaseCosts(
                contract,
                actionReference,
                element,
                out var baseCosts,
                out reason))
            return false;

        var nativeLevels = new List<NativeLevel>(maximum);
        var resources = new List<NativeResource>();
        var resourceIndices = new Dictionary<object, int>(ReferenceComparer.Instance);
        for (var level = 1; level <= maximum; level++)
        {
            if (timer.Elapsed > CaptureBudget)
                return Fail("exact native cost capture exceeded its click-path time bound", out reason);
            var scaling = contract.GetScalingInfoAt.Invoke(selected, new object[] { level });
            var drainMod = scaling is null
                ? null
                : contract.GetDrainCostMod.Invoke(scaling, Array.Empty<object>());
            var percent = drainMod is null
                ? null
                : contract.AsPercent.Invoke(drainMod, Array.Empty<object>());
            if (percent is null || !TryReadFiniteBigAmount(contract, percent, out var multiplier) ||
                multiplier.IsNegative)
                return Fail("native action scaling is invalid", out reason);

            var drains = new List<NativeDrain>(baseCosts.Count);
            foreach (var baseCost in baseCosts)
            {
                if (!TryMultiply(contract, baseCost.Amount, percent, out var rawDrain) ||
                    contract.GetTrueSpend.Invoke(baseCost.Resource, new[] { rawDrain }) is not { } trueDrain ||
                    !TryReadFiniteBigAmount(contract, trueDrain, out var amount) ||
                    amount.IsNegative)
                    return Fail("native resource quality conversion failed", out reason);
                var resourceIndex = AddResource(
                    baseCost.Resource,
                    resources,
                    resourceIndices,
                    out var resourceFailure);
                if (resourceIndex < 0)
                    return Fail(resourceFailure, out reason);
                AddDrain(drains, resourceIndex, amount);
            }
            nativeLevels.Add(new NativeLevel(level, drains));
        }

        var currentContributions = new Dictionary<object, BigAmount>(ReferenceComparer.Instance);
        if (existing is not null && previousLevel > 0)
        {
            var resourceDrain = contract.ResourceDrainField.GetValue(existing);
            var currentCosts = resourceDrain is null
                ? null
                : contract.GetCurrentDrain.Invoke(resourceDrain, Array.Empty<object>());
            if (!TryReadCostEntries(contract, currentCosts, out var entries, out reason))
                return false;
            foreach (var entry in entries)
            {
                var trueSpend = contract.GetTrueSpend.Invoke(entry.Resource, new[] { entry.Amount });
                if (trueSpend is null ||
                    !TryReadFiniteBigAmount(contract, trueSpend, out var contribution) ||
                    contribution.IsNegative)
                    return Fail("the current selected drain is invalid", out reason);
                currentContributions.TryGetValue(entry.Resource, out var accumulated);
                currentContributions[entry.Resource] = accumulated.Add(contribution);
            }
        }

        var resourceSnapshots = new List<AutoAgromancyResourceSnapshot>(resources.Count);
        for (var index = 0; index < resources.Count; index++)
        {
            var resource = resources[index];
            var live = contract.GetTrueRate.Invoke(resource.Native, Array.Empty<object>());
            if (live is null || !TryReadFiniteBigAmount(contract, live, out var liveRate))
                return Fail("a consumed resource true rate is unavailable", out reason);
            currentContributions.TryGetValue(resource.Native, out var currentContribution);
            resourceSnapshots.Add(new AutoAgromancyResourceSnapshot(
                resource.Id,
                resource.Name,
                liveRate.Add(currentContribution)));
        }

        var levels = new List<AutoAgromancyLevelCost>(nativeLevels.Count);
        foreach (var nativeLevel in nativeLevels)
        {
            var drains = new AutoAgromancyDrainEntry[nativeLevel.Drains.Count];
            for (var index = 0; index < drains.Length; index++)
            {
                drains[index] = new AutoAgromancyDrainEntry(
                    nativeLevel.Drains[index].ResourceIndex,
                    nativeLevel.Drains[index].Amount);
            }
            levels.Add(new AutoAgromancyLevelCost(nativeLevel.Level, drains));
        }

        capture = new Capture(
            actionList,
            selected,
            existing,
            action,
            actionId,
            element,
            elementId,
            previousLevel,
            maximum,
            resources,
            resourceSnapshots,
            levels);
        reason = string.Empty;
        return true;
    }

    private static bool TryReadBaseCosts(
        Contract contract,
        object actionReference,
        object element,
        out List<NativeCost> costs,
        out string reason)
    {
        costs = new List<NativeCost>();
        var actionCost = contract.ActionCostField.GetValue(actionReference);
        if (!TryReadCostEntries(contract, actionCost, out var entries, out reason))
            return false;
        costs.AddRange(entries);

        var elementCostValue = contract.ElementCostField.GetValue(actionReference);
        if (elementCostValue is null)
            return Fail("the element-internal-resource cost is unavailable", out reason);
        double elementCost;
        try
        {
            elementCost = Convert.ToDouble(elementCostValue, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is InvalidCastException or FormatException or OverflowException)
        {
            return Fail("the element-internal-resource cost is invalid", out reason);
        }
        if (double.IsNaN(elementCost) || double.IsInfinity(elementCost) || elementCost < 0)
            return Fail("the element-internal-resource cost is invalid", out reason);
        if (elementCost > 0)
        {
            var resource = contract.GetInternalResource.Invoke(element, Array.Empty<object>());
            if (resource is null || !contract.ResourceType.IsInstanceOfType(resource) ||
                !TryCreateBigDouble(contract, elementCost, out var nativeCost))
                return Fail("the element internal resource is unavailable", out reason);
            costs.Add(new NativeCost(resource, nativeCost));
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryReadCostEntries(
        Contract contract,
        object? costList,
        out List<NativeCost> costs,
        out string reason)
    {
        costs = new List<NativeCost>();
        if (costList is null ||
            contract.GetEntries.Invoke(costList, Array.Empty<object>()) is not IList entries)
            return Fail("the native cost vector is unavailable", out reason);
        for (var index = 0; index < entries.Count; index++)
        {
            var tuple = entries[index];
            if (tuple is null || !contract.ResourceTupleType.IsInstanceOfType(tuple))
                return Fail("the native cost vector contains an invalid tuple", out reason);
            var resource = contract.ResourceField.GetValue(tuple);
            var amount = contract.GetValue.Invoke(tuple, Array.Empty<object>());
            if (resource is null || amount is null ||
                !contract.ResourceType.IsInstanceOfType(resource) ||
                !TryReadFiniteBigAmount(contract, amount, out var parsed) ||
                parsed.IsNegative)
                return Fail("the native cost vector contains an invalid resource or amount", out reason);
            costs.Add(new NativeCost(resource, amount));
        }
        reason = string.Empty;
        return true;
    }

    private static int AddResource(
        object native,
        List<NativeResource> resources,
        Dictionary<object, int> indices,
        out string reason)
    {
        if (indices.TryGetValue(native, out var existing))
        {
            reason = string.Empty;
            return existing;
        }
        if (!TryStableId(native, out var id))
        {
            reason = "a consumed resource stable identity is unavailable";
            return -1;
        }
        var index = resources.Count;
        indices.Add(native, index);
        resources.Add(new NativeResource(
            native,
            id,
            ReflectionUtil.ReadDisplayName(native) ?? id));
        reason = string.Empty;
        return index;
    }

    private static void AddDrain(
        List<NativeDrain> drains,
        int resourceIndex,
        BigAmount amount)
    {
        for (var index = 0; index < drains.Count; index++)
        {
            if (drains[index].ResourceIndex != resourceIndex) continue;
            drains[index] = new NativeDrain(
                resourceIndex,
                drains[index].Amount.Add(amount));
            return;
        }
        drains.Add(new NativeDrain(resourceIndex, amount));
    }

    private bool TryApply(Capture capture, int targetLevel, out string reason)
    {
        var contract = _contract!;
        if (capture.Existing is null)
        {
            contract.AddInstance.Invoke(
                capture.ActionList,
                new object[] { capture.Selected, targetLevel });
        }
        else
        {
            contract.ChangeInstance.Invoke(
                capture.Existing,
                new object[] { targetLevel - capture.PreviousLevel });
        }
        var applied = contract.FindInstance.Invoke(
            capture.ActionList,
            new[] { capture.Selected });
        if (applied is null ||
            !contract.InstanceType.IsInstanceOfType(applied) ||
            ReadInt(contract.InstancesField, applied) != targetLevel ||
            !HasSamePair(contract, applied, capture.ActionId, capture.ElementId))
            return Fail("native mutation did not produce the exact requested level", out reason);
        reason = string.Empty;
        return true;
    }

    private bool TryVerifyObservedRates(Capture capture, out string reason)
    {
        var contract = _contract!;
        for (var index = 0; index < capture.NativeResources.Count; index++)
        {
            var resource = capture.NativeResources[index];
            var rate = contract.GetTrueRate.Invoke(resource.Native, Array.Empty<object>());
            if (rate is null || !TryReadFiniteBigAmount(contract, rate, out var parsed))
                return Fail("post-engagement resource rate is unavailable; the change was rolled back", out reason);
            if (parsed.IsNegative)
                return Fail($"{resource.Name} became negative after action effects; the change was rolled back", out reason);
        }
        reason = string.Empty;
        return true;
    }

    private void TryRollback(Capture capture)
    {
        try
        {
            var contract = _contract!;
            var applied = contract.FindInstance.Invoke(
                capture.ActionList,
                new[] { capture.Selected });
            if (applied is null) return;
            var current = ReadInt(contract.InstancesField, applied);
            if (capture.PreviousLevel > 0)
                contract.ChangeInstance.Invoke(
                    applied,
                    new object[] { capture.PreviousLevel - current });
            else
                contract.RemoveInstance.Invoke(
                    capture.ActionList,
                    new object[] { applied, current });
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            // The caller reports an unverified mutation. Never retry an ambiguous rollback.
        }
    }

    private static bool Equivalent(Capture left, Capture right)
    {
        if (!ReferenceEquals(left.ActionList, right.ActionList) ||
            !ReferenceEquals(left.Action, right.Action) ||
            !ReferenceEquals(left.Element, right.Element) ||
            !string.Equals(left.ActionId, right.ActionId, StringComparison.Ordinal) ||
            !string.Equals(left.ElementId, right.ElementId, StringComparison.Ordinal) ||
            left.PreviousLevel != right.PreviousLevel ||
            left.MaximumLevel != right.MaximumLevel ||
            left.Resources.Count != right.Resources.Count ||
            left.Levels.Count != right.Levels.Count)
            return false;
        for (var index = 0; index < left.Resources.Count; index++)
        {
            var a = left.Resources[index];
            var b = right.Resources[index];
            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal) ||
                !Equal(a.BaselineWithoutSelected, b.BaselineWithoutSelected))
                return false;
        }
        for (var levelIndex = 0; levelIndex < left.Levels.Count; levelIndex++)
        {
            var a = left.Levels[levelIndex];
            var b = right.Levels[levelIndex];
            if (a.Level != b.Level || a.Drains.Count != b.Drains.Count) return false;
            for (var drainIndex = 0; drainIndex < a.Drains.Count; drainIndex++)
            {
                if (a.Drains[drainIndex].ResourceIndex != b.Drains[drainIndex].ResourceIndex ||
                    !Equal(a.Drains[drainIndex].Drain, b.Drains[drainIndex].Drain))
                    return false;
            }
        }
        return true;
    }

    private static AutoAgromancyBalanceResult FromPlan(
        Capture capture,
        in AutoAgromancyPlan plan)
    {
        var resource = plan.LimitingResourceIndex >= 0 &&
            plan.LimitingResourceIndex < capture.Resources.Count
                ? capture.Resources[plan.LimitingResourceIndex].Name
                : string.Empty;
        return new AutoAgromancyBalanceResult(
            AutoAgromancyBalanceDisposition.Rejected,
            capture.ActionId,
            capture.ElementId,
            capture.PreviousLevel,
            0,
            capture.MaximumLevel,
            resource,
            plan.LimitingProjectedRate,
            plan.Reason);
    }

    private static AutoAgromancyBalanceResult Success(
        Capture capture,
        in AutoAgromancyPlan plan)
    {
        var resource = plan.LimitingResourceIndex >= 0 &&
            plan.LimitingResourceIndex < capture.Resources.Count
                ? capture.Resources[plan.LimitingResourceIndex].Name
                : string.Empty;
        return new AutoAgromancyBalanceResult(
            AutoAgromancyBalanceDisposition.Applied,
            capture.ActionId,
            capture.ElementId,
            capture.PreviousLevel,
            plan.TargetLevel,
            capture.MaximumLevel,
            resource,
            plan.LimitingProjectedRate,
            plan.Reason);
    }

    private static AutoAgromancyBalanceResult Failure(
        AutoAgromancyBalanceDisposition disposition,
        string reason,
        Capture capture = default,
        int targetLevel = 0) =>
        new(
            disposition,
            capture.ActionId,
            capture.ElementId,
            capture.PreviousLevel,
            targetLevel,
            capture.MaximumLevel,
            string.Empty,
            default,
            reason);

    private static bool HasSamePair(
        Contract contract,
        object instance,
        string actionId,
        string elementId)
    {
        var action = contract.GetAction.Invoke(instance, Array.Empty<object>());
        var element = contract.GetElement.Invoke(instance, Array.Empty<object>());
        return action is not null &&
            element is not null &&
            TryStableId(action, out var actualAction) &&
            TryStableId(element, out var actualElement) &&
            string.Equals(actualAction, actionId, StringComparison.Ordinal) &&
            string.Equals(actualElement, elementId, StringComparison.Ordinal);
    }

    private static bool TryMultiply(
        Contract contract,
        object left,
        object right,
        out object result)
    {
        result = null!;
        if (!contract.BigDoubleType.IsInstanceOfType(left) ||
            !contract.BigDoubleType.IsInstanceOfType(right))
            return false;
        result = contract.Multiply.Invoke(null, new[] { left, right })!;
        return result is not null;
    }

    private static bool TryCreateBigDouble(
        Contract contract,
        double value,
        out object result)
    {
        result = contract.FromDouble.Invoke(null, new object[] { value })!;
        return result is not null && contract.BigDoubleType.IsInstanceOfType(result);
    }

    private static bool TryReadFiniteBigAmount(
        Contract contract,
        object value,
        out BigAmount amount)
    {
        amount = default;
        if (!contract.BigDoubleType.IsInstanceOfType(value) ||
            contract.BigDoubleMantissaField.GetValue(value) is not double mantissa ||
            double.IsNaN(mantissa) ||
            double.IsInfinity(mantissa) ||
            contract.BigDoubleExponentField.GetValue(value) is not long exponent)
            return false;
        amount = new BigAmount(mantissa, exponent);
        return true;
    }

    private static int ReadInt(FieldInfo field, object instance)
    {
        var value = field.GetValue(instance);
        return value is int result
            ? result
            : throw new InvalidOperationException(field.Name + " did not return Int32.");
    }

    private static int InvokeInt(MethodInfo method, object instance)
    {
        var value = method.Invoke(instance, Array.Empty<object>());
        return value is int result
            ? result
            : throw new InvalidOperationException(method.Name + " did not return Int32.");
    }

    private static bool Equal(BigAmount left, BigAmount right) =>
        left.Mantissa.Equals(right.Mantissa) && left.Exponent == right.Exponent;

    private static bool TryStableId(object value, out string id)
    {
        id = ReflectionUtil.ReadStableId(value) ?? string.Empty;
        if (!Guid.TryParse(id, out var parsed)) return false;
        id = parsed.ToString();
        return true;
    }

    private static void TryPresentSuccess(object uiList, object selected, object action)
    {
        try
        {
            var sound = ReflectionUtil.ReadMember(action, "equipSound");
            if (sound is not null) ReflectionUtil.InvokeNoArgs(sound, "Play");
            if (uiList.GetType().Name == "UIHarvestAction")
            {
                ReflectionUtil.InvokeNoArgs(uiList, "Flash");
            }
            else
            {
                var rendered = InvokeCompatible(uiList, "GetRenderedItem", selected);
                if (rendered is not null) ReflectionUtil.InvokeNoArgs(rendered, "Flash");
            }
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            // Presentation is non-authoritative after the verified native mutation.
        }
    }

    private static object? InvokeCompatible(object instance, string name, object argument)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Instance |
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                var parameters = method.GetParameters();
                if (method.Name == name &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType.IsInstanceOfType(argument))
                    return method.Invoke(instance, new[] { argument });
            }
        }
        return null;
    }

    private static bool IsExpectedNativeFailure(Exception exception) =>
        exception is TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        MissingMethodException or
        MemberAccessException or
        OverflowException;

    private static bool Fail(string reason, out string output)
    {
        output = reason;
        return false;
    }

    private readonly struct NativeCost
    {
        internal NativeCost(object resource, object amount)
        {
            Resource = resource;
            Amount = amount;
        }
        internal object Resource { get; }
        internal object Amount { get; }
    }

    private readonly struct NativeDrain
    {
        internal NativeDrain(int resourceIndex, BigAmount amount)
        {
            ResourceIndex = resourceIndex;
            Amount = amount;
        }
        internal int ResourceIndex { get; }
        internal BigAmount Amount { get; }
    }

    private readonly struct NativeLevel
    {
        internal NativeLevel(int level, List<NativeDrain> drains)
        {
            Level = level;
            Drains = drains;
        }
        internal int Level { get; }
        internal List<NativeDrain> Drains { get; }
    }

    private readonly struct NativeResource
    {
        internal NativeResource(object native, string id, string name)
        {
            Native = native;
            Id = id;
            Name = name;
        }
        internal object Native { get; }
        internal string Id { get; }
        internal string Name { get; }
    }

    private readonly struct Capture
    {
        internal Capture(
            object actionList,
            object selected,
            object? existing,
            object action,
            string actionId,
            object element,
            string elementId,
            int previousLevel,
            int maximumLevel,
            List<NativeResource> nativeResources,
            List<AutoAgromancyResourceSnapshot> resources,
            List<AutoAgromancyLevelCost> levels)
        {
            ActionList = actionList;
            Selected = selected;
            Existing = existing;
            Action = action;
            ActionId = actionId;
            Element = element;
            ElementId = elementId;
            PreviousLevel = previousLevel;
            MaximumLevel = maximumLevel;
            NativeResources = nativeResources;
            Resources = resources;
            Levels = levels;
        }
        internal object ActionList { get; }
        internal object Selected { get; }
        internal object? Existing { get; }
        internal object Action { get; }
        internal string ActionId { get; }
        internal object Element { get; }
        internal string ElementId { get; }
        internal int PreviousLevel { get; }
        internal int MaximumLevel { get; }
        internal List<NativeResource> NativeResources { get; }
        internal List<AutoAgromancyResourceSnapshot> Resources { get; }
        internal List<AutoAgromancyLevelCost> Levels { get; }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    private sealed class Contract
    {
        private Contract(
            Type uiListType,
            Type uiRowType,
            Type instanceType,
            Type listType,
            Type actionType,
            Type elementType,
            Type actionReferenceType,
            Type resourceType,
            Type resourceTupleType,
            Type bigDoubleType,
            FieldInfo bigDoubleMantissaField,
            FieldInfo bigDoubleExponentField,
            FieldInfo actionListField,
            FieldInfo rowActionListField,
            FieldInfo listValuesField,
            FieldInfo instancesField,
            FieldInfo resourceDrainField,
            FieldInfo actionCostField,
            FieldInfo elementCostField,
            FieldInfo resourceField,
            MethodInfo getAction,
            MethodInfo getElement,
            MethodInfo getActionRef,
            MethodInfo isVisible,
            MethodInfo getMaximumInstances,
            MethodInfo getScalingInfoAt,
            MethodInfo findInstance,
            MethodInfo hasEmptySpot,
            MethodInfo addInstance,
            MethodInfo removeInstance,
            MethodInfo changeInstance,
            MethodInfo getInternalResource,
            MethodInfo getEntries,
            MethodInfo getValue,
            MethodInfo getDrainCostMod,
            MethodInfo asPercent,
            MethodInfo getTrueSpend,
            MethodInfo getTrueRate,
            MethodInfo getCurrentDrain,
            MethodInfo multiply,
            MethodInfo fromDouble)
        {
            UiListType = uiListType;
            UiRowType = uiRowType;
            InstanceType = instanceType;
            ListType = listType;
            ActionType = actionType;
            ElementType = elementType;
            ActionReferenceType = actionReferenceType;
            ResourceType = resourceType;
            ResourceTupleType = resourceTupleType;
            BigDoubleType = bigDoubleType;
            BigDoubleMantissaField = bigDoubleMantissaField;
            BigDoubleExponentField = bigDoubleExponentField;
            ActionListField = actionListField;
            RowActionListField = rowActionListField;
            ListValuesField = listValuesField;
            InstancesField = instancesField;
            ResourceDrainField = resourceDrainField;
            ActionCostField = actionCostField;
            ElementCostField = elementCostField;
            ResourceField = resourceField;
            GetAction = getAction;
            GetElement = getElement;
            GetActionRef = getActionRef;
            IsVisible = isVisible;
            GetMaximumInstances = getMaximumInstances;
            GetScalingInfoAt = getScalingInfoAt;
            FindInstance = findInstance;
            HasEmptySpot = hasEmptySpot;
            AddInstance = addInstance;
            RemoveInstance = removeInstance;
            ChangeInstance = changeInstance;
            GetInternalResource = getInternalResource;
            GetEntries = getEntries;
            GetValue = getValue;
            GetDrainCostMod = getDrainCostMod;
            AsPercent = asPercent;
            GetTrueSpend = getTrueSpend;
            GetTrueRate = getTrueRate;
            GetCurrentDrain = getCurrentDrain;
            Multiply = multiply;
            FromDouble = fromDouble;
        }

        internal Type UiListType { get; }
        internal Type UiRowType { get; }
        internal Type InstanceType { get; }
        internal Type ListType { get; }
        internal Type ActionType { get; }
        internal Type ElementType { get; }
        internal Type ActionReferenceType { get; }
        internal Type ResourceType { get; }
        internal Type ResourceTupleType { get; }
        internal Type BigDoubleType { get; }
        internal FieldInfo BigDoubleMantissaField { get; }
        internal FieldInfo BigDoubleExponentField { get; }
        internal FieldInfo ActionListField { get; }
        internal FieldInfo RowActionListField { get; }
        internal FieldInfo ListValuesField { get; }
        internal FieldInfo InstancesField { get; }
        internal FieldInfo ResourceDrainField { get; }
        internal FieldInfo ActionCostField { get; }
        internal FieldInfo ElementCostField { get; }
        internal FieldInfo ResourceField { get; }
        internal MethodInfo GetAction { get; }
        internal MethodInfo GetElement { get; }
        internal MethodInfo GetActionRef { get; }
        internal MethodInfo IsVisible { get; }
        internal MethodInfo GetMaximumInstances { get; }
        internal MethodInfo GetScalingInfoAt { get; }
        internal MethodInfo FindInstance { get; }
        internal MethodInfo HasEmptySpot { get; }
        internal MethodInfo AddInstance { get; }
        internal MethodInfo RemoveInstance { get; }
        internal MethodInfo ChangeInstance { get; }
        internal MethodInfo GetInternalResource { get; }
        internal MethodInfo GetEntries { get; }
        internal MethodInfo GetValue { get; }
        internal MethodInfo GetDrainCostMod { get; }
        internal MethodInfo AsPercent { get; }
        internal MethodInfo GetTrueSpend { get; }
        internal MethodInfo GetTrueRate { get; }
        internal MethodInfo GetCurrentDrain { get; }
        internal MethodInfo Multiply { get; }
        internal MethodInfo FromDouble { get; }

        internal static bool TryCreate(out Contract? contract, out string reason)
        {
            contract = null;
            reason = string.Empty;
            try
            {
                var uiList = RequireType("UIHarvestActionList");
                var uiRow = RequireType("UIHarvestAction");
                var instance = RequireType("HarvestActionInstance");
                var list = RequireType("HarvestActionInstanceListVariable");
                var action = RequireType("HarvestActionSO");
                var element = RequireType("HarvestElementSO");
                var actionReference = RequireType("HarvestElementSO+HarvestActionReference");
                var resource = RequireType("ResourceSO");
                var tuple = RequireType("ResourceTuple");
                var resourceDrain = RequireType("ResourceDrain");
                var scalingInfo = RequireType("ScalingInfo");
                var bigDouble = RequireType("BigDouble");
                contract = new Contract(
                    uiList,
                    uiRow,
                    instance,
                    list,
                    action,
                    element,
                    actionReference,
                    resource,
                    tuple,
                    bigDouble,
                    RequireField(bigDouble, "mantissa", typeof(double), false),
                    RequireField(bigDouble, "exponent", typeof(long), false),
                    RequireField(uiList, "actionListVariable", list, false),
                    RequireField(uiRow, "actionListVariable", list, false),
                    RequireField(
                        list,
                        "value",
                        typeof(List<>).MakeGenericType(instance),
                        false),
                    RequireField(instance, "instances", typeof(int), false),
                    RequireField(instance, "resourceDrain", resourceDrain, false),
                    RequireField(actionReference, "actionCost", RequireType("ResourceCostList"), false),
                    RequireField(actionReference, "elementCost", typeof(double), false),
                    RequireField(tuple, "resource", resource, false),
                    RequireMethod(instance, "GetAction", action),
                    RequireMethod(instance, "GetElement", element),
                    RequireMethod(instance, "GetActionRef", actionReference),
                    RequireMethod(instance, "IsVisible", typeof(bool)),
                    RequireMethod(instance, "GetMaximumInstances", typeof(int)),
                    RequireMethod(instance, "GetScalingInfo", scalingInfo, typeof(int)),
                    RequireMethod(list, "FindInstance", instance, instance),
                    RequireMethodInHierarchy(list, "HasEmptySpot", typeof(bool)),
                    RequireMethod(list, "AddInstance", typeof(void), instance, typeof(int)),
                    RequireMethod(list, "RemoveInstance", typeof(void), instance, typeof(int)),
                    RequireMethod(instance, "ChangeInstance", typeof(void), typeof(int)),
                    RequireMethod(element, "GetInternalResource", resource),
                    RequireMethod(RequireType("ResourceCostList"), "GetEntries",
                        typeof(List<>).MakeGenericType(tuple)),
                    RequireMethod(tuple, "GetValue", bigDouble),
                    RequireMethod(scalingInfo, "GetDrainCostMod", bigDouble),
                    RequireMethod(bigDouble, "AsPercent", bigDouble),
                    RequireMethod(resource, "GetTrueSpend", bigDouble, bigDouble),
                    RequireMethod(resource, "GetTrueRate", bigDouble),
                    RequireMethod(resourceDrain, "GetCurrentDrain", RequireType("ResourceCostList")),
                    RequireStaticMethod(bigDouble, "op_Multiply", bigDouble, bigDouble, bigDouble),
                    RequireStaticMethod(bigDouble, "op_Implicit", bigDouble, typeof(double)));
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException)
            {
                reason = exception.Message;
                return false;
            }
        }

        private static Type RequireType(string name) =>
            ReflectionUtil.FindLoadedType(name) ??
            throw new InvalidOperationException(name + " type was not found.");

        private static FieldInfo RequireField(
            Type owner,
            string name,
            Type fieldType,
            bool isStatic)
        {
            for (var type = owner; type is not null; type = type.BaseType)
            {
                var field = type.GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field is not null &&
                    field.FieldType == fieldType &&
                    field.IsStatic == isStatic)
                    return field;
            }
            throw new InvalidOperationException(owner.Name + "." + name + " field contract changed.");
        }

        private static MethodInfo RequireMethod(
            Type owner,
            string name,
            Type returnType,
            params Type[] parameters)
        {
            var method = owner.GetMethod(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                parameters,
                null);
            if (method is null || method.IsStatic || method.ReturnType != returnType)
                throw new InvalidOperationException(owner.Name + "." + name + " method contract changed.");
            return method;
        }

        private static MethodInfo RequireMethodInHierarchy(
            Type owner,
            string name,
            Type returnType,
            params Type[] parameters) =>
            RequireMethod(owner, name, returnType, parameters);

        private static MethodInfo RequireStaticMethod(
            Type owner,
            string name,
            Type returnType,
            params Type[] parameters)
        {
            var method = owner.GetMethod(
                name,
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                parameters,
                null);
            if (method is null || !method.IsStatic || method.ReturnType != returnType)
                throw new InvalidOperationException(owner.Name + "." + name + " operator contract changed.");
            return method;
        }
    }
}
