using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OrbAutomata;

internal static class AutoBuyLifecycleSignal
{
    public static event Action? Invalidated;

    public static void Raise()
    {
        Invalidated?.Invoke();
    }
}

[HarmonyPatch]
internal static class AutoBuyLifecyclePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var player = AccessTools.TypeByName("Player");
        var managerStart = player?.GetMethod(
            "ManagerStart",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (managerStart is not null)
        {
            yield return managerStart;
        }

        var saveStateManager = AccessTools.TypeByName("SaveStateManager");
        var implementLoadedJson = saveStateManager is null
            ? null
            : saveStateManager.GetMethod(
                "ImplementLoadedJson",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
        if (implementLoadedJson is not null)
        {
            yield return implementLoadedJson;
        }
    }

    private static void Postfix()
    {
        AutoBuyLifecycleSignal.Raise();
    }
}
