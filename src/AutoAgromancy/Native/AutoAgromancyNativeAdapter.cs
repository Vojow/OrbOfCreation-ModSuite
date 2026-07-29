using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoAgromancyExactMutationDisposition
{
    Committed = 1,
    Rejected = 2,
    ContractUnavailable = 3,
    AttemptedUnverified = 4,
}

internal readonly struct AutoAgromancyExactMutationResult
{
    internal AutoAgromancyExactMutationResult(
        AutoAgromancyExactMutationDisposition disposition,
        int previousLevel,
        int observedLevel,
        string reason)
    {
        Disposition = disposition;
        PreviousLevel = previousLevel;
        ObservedLevel = observedLevel;
        Reason = reason ?? string.Empty;
    }

    internal AutoAgromancyExactMutationDisposition Disposition { get; }
    internal int PreviousLevel { get; }
    internal int ObservedLevel { get; }
    internal string Reason { get; }
}

/// <summary>
/// Owns only the exact main-thread Druidry level mutation. Prospective cost
/// reading and level selection belong to world collection and the worker.
/// </summary>
internal sealed class AutoAgromancyNativeAdapter : IAutoAgromancyExactNativeMutator
{
    internal const string ActiveHarvestActionsId =
        "e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32";

    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly TypedRegistryResolver _registryResolver;
    private readonly Contract? _contract;
    private readonly string _contractFailure;

    internal AutoAgromancyNativeAdapter(
        Func<bool> tryCaptureMutationPermit,
        TypedRegistryResolver? registryResolver = null)
    {
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
        Contract.TryCreate(out _contract, out var failure);
        _contractFailure = _contract is null
            ? "Auto Agromancy exact mutation contract is unavailable: " + failure
            : string.Empty;
    }

    internal bool ContractAvailable => _contract is not null;
    internal string ContractFailure => _contractFailure;

    public AutoAgromancyExactMutationResult ApplyExactTarget(
        Guid actionId,
        Guid elementId,
        int expectedCurrentLevel,
        int targetLevel)
    {
        var contract = _contract;
        if (contract is null)
            return Result(
                AutoAgromancyExactMutationDisposition.ContractUnavailable,
                expectedCurrentLevel,
                expectedCurrentLevel,
                _contractFailure);
        if (actionId == Guid.Empty || elementId == Guid.Empty ||
            expectedCurrentLevel < 0 || targetLevel < 0)
            return Result(
                AutoAgromancyExactMutationDisposition.Rejected,
                expectedCurrentLevel,
                expectedCurrentLevel,
                "the exact mutation request is invalid");

        try
        {
            if (!TryResolveActiveList(contract, out var list, out var resolutionFailure))
                return Result(
                    AutoAgromancyExactMutationDisposition.Rejected,
                    expectedCurrentLevel,
                    expectedCurrentLevel,
                    resolutionFailure);
            if (contract.ListValues.GetValue(list) is not IList values)
                return Result(
                    AutoAgromancyExactMutationDisposition.ContractUnavailable,
                    expectedCurrentLevel,
                    expectedCurrentLevel,
                    "the active Druidry entries are unavailable");

            if (!TryFindUniquePair(
                    contract,
                    values,
                    actionId,
                    elementId,
                    out var selected,
                    out var duplicated))
                return Result(
                    AutoAgromancyExactMutationDisposition.Rejected,
                    expectedCurrentLevel,
                    expectedCurrentLevel,
                    duplicated
                        ? "the active Druidry pair is duplicated"
                        : "the active Druidry pair disappeared");

            var current = ReadInt(contract.Instances, selected);
            var maximum = InvokeInt(contract.GetMaximumInstances, selected);
            if (current != expectedCurrentLevel || maximum <= 0 || targetLevel > maximum)
                return Result(
                    AutoAgromancyExactMutationDisposition.Rejected,
                    current,
                    current,
                    "the current or maximum Druidry level changed");
            if (contract.IsVisible.Invoke(selected, Array.Empty<object>()) is not true)
                return Result(
                    AutoAgromancyExactMutationDisposition.Rejected,
                    current,
                    current,
                    "the active Druidry pair is no longer visible");
            if (!_tryCaptureMutationPermit())
                return Result(
                    AutoAgromancyExactMutationDisposition.Rejected,
                    current,
                    current,
                    "Druidry level-adjustment ownership is unavailable");

            contract.ChangeInstance.Invoke(
                selected,
                new object[] { targetLevel - current });

            var found = TryFindUniquePair(
                contract,
                values,
                actionId,
                elementId,
                out var applied,
                out _);
            var observed = found ? ReadInt(contract.Instances, applied) : 0;
            return observed == targetLevel
                ? Result(
                    AutoAgromancyExactMutationDisposition.Committed,
                    current,
                    observed,
                    "the exact native Druidry level was verified")
                : Result(
                    AutoAgromancyExactMutationDisposition.AttemptedUnverified,
                    current,
                    observed,
                    "the native Druidry level did not match the exact target");
        }
        catch (Exception exception) when (IsExpectedNativeFailure(exception))
        {
            return Result(
                AutoAgromancyExactMutationDisposition.AttemptedUnverified,
                expectedCurrentLevel,
                -1,
                "the exact native Druidry mutation threw: " +
                exception.GetBaseException().Message);
        }
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

    private static bool TryFindUniquePair(
        Contract contract,
        IList values,
        Guid actionId,
        Guid elementId,
        out object selected,
        out bool duplicated)
    {
        selected = null!;
        duplicated = false;
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = values[index];
            if (candidate is null ||
                !contract.InstanceType.IsInstanceOfType(candidate) ||
                !HasSamePair(contract, candidate, actionId, elementId))
                continue;
            if (selected is not null)
            {
                duplicated = true;
                selected = null!;
                return false;
            }
            selected = candidate;
        }
        return selected is not null;
    }

    private static bool HasSamePair(
        Contract contract,
        object instance,
        Guid actionId,
        Guid elementId)
    {
        var action = contract.GetAction.Invoke(instance, Array.Empty<object>());
        var element = contract.GetElement.Invoke(instance, Array.Empty<object>());
        return action is not null &&
            element is not null &&
            contract.ActionType.IsInstanceOfType(action) &&
            contract.ElementType.IsInstanceOfType(element) &&
            contract.ActionGetGuid.Invoke(action, Array.Empty<object>()) is Guid actualAction &&
            contract.ElementGetGuid.Invoke(element, Array.Empty<object>()) is Guid actualElement &&
            actualAction == actionId &&
            actualElement == elementId;
    }

    private static int ReadInt(FieldInfo field, object instance) =>
        field.GetValue(instance) is int result
            ? result
            : throw new InvalidOperationException(field.Name + " did not return Int32.");

    private static int InvokeInt(MethodInfo method, object instance) =>
        method.Invoke(instance, Array.Empty<object>()) is int result
            ? result
            : throw new InvalidOperationException(method.Name + " did not return Int32.");

    private static AutoAgromancyExactMutationResult Result(
        AutoAgromancyExactMutationDisposition disposition,
        int previousLevel,
        int observedLevel,
        string reason) =>
        new(disposition, previousLevel, observedLevel, reason);

    private static bool IsExpectedNativeFailure(Exception exception) =>
        exception is TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        MissingMethodException or
        MemberAccessException or
        OverflowException;

    private sealed class Contract
    {
        private Contract(
            Type instanceType,
            Type listType,
            Type actionType,
            Type elementType,
            FieldInfo listValues,
            FieldInfo instances,
            MethodInfo getAction,
            MethodInfo getElement,
            MethodInfo actionGetGuid,
            MethodInfo elementGetGuid,
            MethodInfo isVisible,
            MethodInfo getMaximumInstances,
            MethodInfo changeInstance)
        {
            InstanceType = instanceType;
            ListType = listType;
            ActionType = actionType;
            ElementType = elementType;
            ListValues = listValues;
            Instances = instances;
            GetAction = getAction;
            GetElement = getElement;
            ActionGetGuid = actionGetGuid;
            ElementGetGuid = elementGetGuid;
            IsVisible = isVisible;
            GetMaximumInstances = getMaximumInstances;
            ChangeInstance = changeInstance;
        }

        internal Type InstanceType { get; }
        internal Type ListType { get; }
        internal Type ActionType { get; }
        internal Type ElementType { get; }
        internal FieldInfo ListValues { get; }
        internal FieldInfo Instances { get; }
        internal MethodInfo GetAction { get; }
        internal MethodInfo GetElement { get; }
        internal MethodInfo ActionGetGuid { get; }
        internal MethodInfo ElementGetGuid { get; }
        internal MethodInfo IsVisible { get; }
        internal MethodInfo GetMaximumInstances { get; }
        internal MethodInfo ChangeInstance { get; }

        internal static bool TryCreate(out Contract? contract, out string reason)
        {
            contract = null;
            reason = string.Empty;
            try
            {
                var instance = RequireType("HarvestActionInstance");
                var list = RequireType("HarvestActionInstanceListVariable");
                var action = RequireType("HarvestActionSO");
                var element = RequireType("HarvestElementSO");
                contract = new Contract(
                    instance,
                    list,
                    action,
                    element,
                    RequireField(
                        list,
                        "value",
                        typeof(List<>).MakeGenericType(instance)),
                    RequireField(instance, "instances", typeof(int)),
                    RequireMethod(instance, "GetAction", action),
                    RequireMethod(instance, "GetElement", element),
                    RequireMethod(action, "GetGuid", typeof(Guid)),
                    RequireMethod(element, "GetGuid", typeof(Guid)),
                    RequireMethod(instance, "IsVisible", typeof(bool)),
                    RequireMethod(instance, "GetMaximumInstances", typeof(int)),
                    RequireMethod(
                        instance,
                        "ChangeInstance",
                        typeof(void),
                        typeof(int)));
                return true;
            }
            catch (InvalidOperationException exception)
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
            Type fieldType)
        {
            for (var type = owner; type is not null; type = type.BaseType)
            {
                var field = type.GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field is not null &&
                    !field.IsStatic &&
                    field.FieldType == fieldType)
                    return field;
            }
            throw new InvalidOperationException(
                owner.Name + "." + name + " field contract changed.");
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
                throw new InvalidOperationException(
                    owner.Name + "." + name + " method contract changed.");
            return method;
        }
    }
}
