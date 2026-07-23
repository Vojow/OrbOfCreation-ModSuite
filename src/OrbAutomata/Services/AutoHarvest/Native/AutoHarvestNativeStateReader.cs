using System;
using System.Collections;
using System.Reflection;
using static OrbAutomata.AutoHarvestReflectionAccess;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
#endif

namespace OrbAutomata;

internal sealed partial class AutoHarvestNativeStateReader :
    IAutoHarvestStatePort,
    IAutoHarvestCaptureStatePort,
    IAutoHarvestSubmissionStatePort
{
#if SERVICE_CYCLE_PROFILE
    private readonly AutoHarvestProfileOperations _profileOperations;

    internal AutoHarvestNativeStateReader(AutoHarvestProfileOperations profileOperations) =>
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
#endif

    public void ReadFacts(
        in ResolvedAutoHarvestPair resolved,
        in AutoHarvestSubmissionState activeState,
        out AutoHarvestPairFacts facts,
        out object? prototype)
    {
        var contract = resolved.Contract;
        var binding = resolved.Target;
        var availableActions = RequireList(
            GetValue(contract.PlotAvailableActions, binding.Plot),
            "plot available actions");
        var actionAvailable = ContainsExactlyOneReference(
            availableActions,
            contract.Types.Action,
            binding.Action);
        prototype = FindPrototype(resolved, out var prototypeKnown);
        var visible = InvokeBool(contract.PlotIsVisible, binding.Plot);
        var prerequisiteState = prototypeKnown && prototype is not null
            ? Evidence(InvokeBool(contract.InstanceIsVisible, prototype))
            : AutoHarvestEvidenceState.Unknown;
        var readinessState = AutoHarvestEvidenceState.Unknown;
        if (prototypeKnown && prototype is not null)
        {
            var ready = InvokeInt(contract.PlotGetRemainingQuantity, binding.Plot) > 0 &&
                InvokeBool(contract.InstanceHasEnough, prototype) &&
                InvokeInt(contract.InstanceGetMaximumRemaining, prototype) > 0 &&
                InvokeInt(contract.ActionGetElementCost, binding.Action, binding.Plot) == 1;
            readinessState = Evidence(ready);
        }

        var identity =
            IdentityMatches(binding.Plot, binding.PlotUuid, contract.PlotStableId) &&
            IdentityMatches(binding.Action, binding.ActionUuid, contract.ActionStableId)
                ? AutoHarvestEvidenceState.Verified
                : AutoHarvestEvidenceState.Unknown;
        facts = new AutoHarvestPairFacts(
            identity,
            Evidence(visible),
            Evidence(actionAvailable),
            prerequisiteState,
            readinessState,
            binding.ActionSafety,
            activeState.IsValid
                ? Evidence(activeState.SupportedCollectCount == 0)
                : AutoHarvestEvidenceState.Unknown,
            ProjectActionSlotAvailability(activeState));
    }

    internal static AutoHarvestEvidenceState ProjectActionSlotAvailability(
        in AutoHarvestSubmissionState state)
    {
        if (!state.IsValid) return AutoHarvestEvidenceState.Unknown;
        return state.NativeHasEmptyEntry && state.EmptyEntryCount >= 1
            ? AutoHarvestEvidenceState.Verified
            : AutoHarvestEvidenceState.Rejected;
    }

    private object? FindPrototype(
        in ResolvedAutoHarvestPair resolved,
        out bool known)
    {
        known = false;
        var contract = resolved.Contract;
        var binding = resolved.Target;
        if (Invoke(contract.PlotGetActionInstances, binding.Plot, Array.Empty<object>()) is not IList instances)
            return null;
        object? match = null;
        foreach (var instance in instances)
        {
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (instance is null || instance.GetType() != contract.Types.Instance) return null;
            var plot = Invoke(contract.InstanceGetElement, instance, Array.Empty<object>());
            var action = Invoke(contract.InstanceGetAction, instance, Array.Empty<object>());
            var observed = ClassifyPair(resolved, plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory) return null;
            var expected = binding.Pair == AutoHarvestPair.FruitTree
                ? AutoHarvestObservedPair.FruitTree
                : AutoHarvestObservedPair.TreasureTree;
            if (observed != expected) continue;
            if (match is not null) return null;
            match = instance;
        }
        known = true;
        return match;
    }

    private AutoHarvestObservedPair ClassifyPair(
        in ResolvedAutoHarvestPair resolved,
        object? plot,
        object? action)
    {
        if (plot is null || action is null) return AutoHarvestObservedPair.Contradictory;
        if (plot.GetType() != resolved.Contract.Types.Plot ||
            action.GetType() != resolved.Contract.Types.Action)
            return AutoHarvestObservedPair.Contradictory;
        var exactFruit = resolved.Fruit is not null &&
            ReferenceEquals(plot, resolved.Fruit.Plot) &&
            ReferenceEquals(action, resolved.Fruit.Action);
        var exactTreasure = resolved.Treasure is not null &&
            ReferenceEquals(plot, resolved.Treasure.Plot) &&
            ReferenceEquals(action, resolved.Treasure.Action);
        var supportedActionReference =
            resolved.Fruit is not null && ReferenceEquals(action, resolved.Fruit.Action) ||
            resolved.Treasure is not null && ReferenceEquals(action, resolved.Treasure.Action);
        return AutoHarvestIdentityPolicy.Classify(
            TryRead(resolved.Contract.PlotStableId, plot, out var plotUuid) ? plotUuid : default,
            TryRead(resolved.Contract.ActionStableId, action, out var actionUuid) ? actionUuid : default,
            exactFruit,
            exactTreasure,
            supportedActionReference);
    }

    private static AutoHarvestEvidenceState Evidence(bool value) =>
        value ? AutoHarvestEvidenceState.Verified : AutoHarvestEvidenceState.Rejected;

    private bool ContainsExactlyOneReference(
        IList values,
        Type expectedType,
        object expected)
    {
        var count = 0;
        foreach (var value in values)
        {
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (value is null || value.GetType() != expectedType) return false;
            if (ReferenceEquals(value, expected)) count++;
        }
        return count == 1;
    }

    private bool IdentityMatches(
        object value,
        string expected,
        AutoHarvestStableIdAccessor accessor) =>
        TryRead(accessor, value, out var actual) &&
        Guid.TryParse(expected, out var wanted) &&
        actual == wanted;

#if SERVICE_CYCLE_PROFILE
    private object? GetValue(FieldInfo field, object owner) =>
        AutoHarvestReflectionAccess.GetValue(field, owner, _profileOperations);

    private object? Invoke(MethodInfo method, object owner, object[] arguments) =>
        AutoHarvestReflectionAccess.Invoke(method, owner, arguments, _profileOperations);

    private bool InvokeBool(MethodInfo method, object owner) =>
        AutoHarvestReflectionAccess.InvokeBool(method, owner, _profileOperations);

    private int InvokeInt(MethodInfo method, object owner, params object[] arguments) =>
        AutoHarvestReflectionAccess.InvokeInt(method, owner, _profileOperations, arguments);

    private bool TryRead(
        AutoHarvestStableIdAccessor accessor,
        object value,
        out Guid identity) => accessor.TryRead(value, out identity, _profileOperations);
#else
    private static object? GetValue(FieldInfo field, object owner) => field.GetValue(owner);

    private static object? Invoke(MethodInfo method, object owner, object[] arguments) =>
        method.Invoke(owner, arguments);

    private static bool TryRead(
        AutoHarvestStableIdAccessor accessor,
        object value,
        out Guid identity) => accessor.TryRead(value, out identity);
#endif
}
