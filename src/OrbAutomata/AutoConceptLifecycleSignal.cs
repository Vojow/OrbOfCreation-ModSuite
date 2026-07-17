using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OrbAutomata;

internal static class AutoConceptLifecycleSignal
{
    public static event Action? Changed;
    public static void RaiseChanged() => Changed?.Invoke();
}

[HarmonyPatch]
internal static class AutoConceptActiveListPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("AlchemyInstanceListVariable");
        if (type is null) yield break;
        foreach (var name in new[] { "AddAlchemyInstances", "RemoveAlchemyInstances", "RebuildCounts", "SetupMaxSlotsValue" })
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name == name) yield return method;
        }
    }

    private static void Postfix() => AutoConceptLifecycleSignal.RaiseChanged();
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
            if (method is not null) yield return method;
        }
    }

    private static void Postfix() => AutoConceptLifecycleSignal.RaiseChanged();
}
