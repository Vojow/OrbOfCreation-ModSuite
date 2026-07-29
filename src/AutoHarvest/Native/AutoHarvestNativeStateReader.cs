using System;
using System.Collections;
using System.Reflection;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
#endif

namespace OrbAutomata;

internal sealed partial class AutoHarvestNativeStateReader : IAutoHarvestSubmissionStatePort
{
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;

    internal AutoHarvestNativeStateReader(AutomataProfileOperations profileOperations) =>
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
#endif

    /// <summary>
    /// Resolves the live instance the action boundary submits into.
    /// </summary>
    /// <remarks>
    /// This is all that is left of the boundary's re-read. Every fact the submission decision rests
    /// on was derived from the world snapshot and rides on the action; what no snapshot can carry is
    /// the object itself, and prototype resolution is the one live read the north star keeps.
    /// </remarks>
    public object? ReadPrototype(in ResolvedAutoHarvestPair resolved)
    {
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
