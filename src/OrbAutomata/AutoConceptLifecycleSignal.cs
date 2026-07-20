using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OrbAutomata;

internal static class AutoConceptLifecycleSignal
{
    public static event Action<object?>? InventoryChanged;

    public static event Action<object>? ProgressionChanged;

    public static void RaiseInventoryChanged(object? nativeRecipe) =>
        InventoryChanged?.Invoke(nativeRecipe);

    public static void RaiseProgressionChanged(object nativeRecipe) =>
        ProgressionChanged?.Invoke(nativeRecipe);
}

[HarmonyPatch]
internal static class AutoConceptActiveListPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("AlchemyInstanceListVariable");
        var recipeType = AccessTools.TypeByName("AlchemyRecipeSO");
        if (type is null || recipeType is null) yield break;
        foreach (var name in new[] { "AddAlchemyInstances", "RemoveAlchemyInstances" })
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { recipeType, typeof(int) },
                null);
            if (method is not null && method.ReturnType == typeof(void)) yield return method;
        }
    }

    private static void Postfix(object __0) =>
        AutoConceptLifecycleSignal.RaiseInventoryChanged(__0);
}

[HarmonyPatch]
internal static class AutoConceptActiveListBroadPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("AlchemyInstanceListVariable");
        if (type is null) yield break;
        foreach (var name in new[] { "RebuildCounts", "SetupMaxSlotsValue" })
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (method is not null && method.ReturnType == typeof(void)) yield return method;
        }
    }

    private static void Postfix() =>
        AutoConceptLifecycleSignal.RaiseInventoryChanged(null);
}

[HarmonyPatch]
internal static class AutoConceptProgressionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("AlchemyRecipeSO");
        if (type is null) yield break;
        foreach (var name in new[] { "Discover", "ApplyMastery" })
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (method is not null && method.ReturnType == typeof(void)) yield return method;
        }
    }

    private static void Postfix(object __instance) =>
        AutoConceptLifecycleSignal.RaiseProgressionChanged(__instance);
}
