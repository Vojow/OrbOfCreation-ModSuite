using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Publishes a wake epoch only when the authoritative plot-action list accepted
/// quantity for the concrete instance passed to AddInstance.
/// </summary>
[HarmonyPatch]
internal static class AutoAgromancyPlotActionPatch
{
    internal readonly struct QuantityBefore
    {
        internal QuantityBefore(bool authoritative, int quantity)
        {
            Authoritative = authoritative;
            Quantity = quantity;
        }

        internal bool Authoritative { get; }
        internal int Quantity { get; }
    }

    private static MethodBase? TargetMethod()
    {
        var list = ReflectionUtil.FindLoadedType(
            KnownEntities.ActivePlotNodeActions.ManagedTypeName);
        var instance = ReflectionUtil.FindLoadedType("PlotNodeActionInstance");
        return list is null || instance is null
            ? null
            : list.GetMethod(
                "AddInstance",
                ReflectionUtil.InstanceFlags,
                null,
                new[] { instance, typeof(int) },
                null);
    }

    private static void Prefix(
        object __instance,
        object actionInstance,
        out QuantityBefore __state) =>
        __state = Capture(__instance, actionInstance);

    private static void Postfix(
        object __instance,
        object actionInstance,
        in QuantityBefore __state)
    {
        PublishIfIncreased(__instance, actionInstance, in __state);
    }

    internal static bool PublishIfIncreased(
        object list,
        object actionInstance,
        in QuantityBefore before)
    {
        if (!before.Authoritative) return false;
        var after = Capture(list, actionInstance);
        if (!after.Authoritative || after.Quantity <= before.Quantity) return false;
        WorldHarvestActionTriggerSource.AdvancePlotAction();
        return true;
    }

    internal static QuantityBefore Capture(object list, object actionInstance)
    {
        if (!IsAuthoritative(list) || actionInstance is null)
            return default;
        try
        {
            var value = ReadValue(list);
            if (value is null) return default;
            var action = Invoke(actionInstance, "GetAction");
            var element = Invoke(actionInstance, "GetElement");
            var quantity = 0;
            foreach (var candidate in value)
            {
                if (candidate is null ||
                    !ReferenceEquals(Invoke(candidate, "GetAction"), action) ||
                    !ReferenceEquals(Invoke(candidate, "GetElement"), element))
                    continue;
                quantity = checked(quantity + ReadQuantity(candidate));
            }
            return new QuantityBefore(authoritative: true, quantity);
        }
        catch (Exception exception) when (
            exception is TargetInvocationException or TargetException or
            ArgumentException or InvalidOperationException or
            MemberAccessException or OverflowException)
        {
            return default;
        }
    }

    private static bool IsAuthoritative(object list)
    {
        if (list is null ||
            !string.Equals(
                list.GetType().FullName,
                KnownEntities.ActivePlotNodeActions.ManagedTypeName,
                StringComparison.Ordinal))
            return false;
        var getGuid = list.GetType().GetMethod(
            "GetGuid",
            ReflectionUtil.InstanceFlags,
            null,
            Type.EmptyTypes,
            null);
        return getGuid?.Invoke(list, Array.Empty<object>()) is Guid id &&
            id == KnownEntities.ActivePlotNodeActions.Uuid;
    }

    private static IEnumerable? ReadValue(object list)
    {
        for (var type = list.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("value", ReflectionUtil.InstanceFlags);
            if (field?.GetValue(list) is IEnumerable value) return value;
        }
        return null;
    }

    private static object? Invoke(object instance, string methodName) =>
        instance.GetType().GetMethod(
            methodName,
            ReflectionUtil.InstanceFlags,
            null,
            Type.EmptyTypes,
            null)?.Invoke(instance, Array.Empty<object>());

    private static int ReadQuantity(object instance)
    {
        var actual = Invoke(instance, "GetActualQuantity");
        return actual is int quantity ? quantity : 0;
    }
}
