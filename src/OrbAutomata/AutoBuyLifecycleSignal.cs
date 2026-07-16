using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OrbAutomata;

internal static class AutoBuyLifecycleSignal
{
    public static event Action? Invalidated;

    public static event Action? StructureQueueChanged;

    public static event Action? UpgradeQueueChanged;

    public static event Action? NativeCompletion;

    public static void Raise()
    {
        Invalidated?.Invoke();
    }

    public static void RaiseStructureQueueChanged()
    {
        StructureQueueChanged?.Invoke();
    }

    public static void RaiseUpgradeQueueChanged()
    {
        UpgradeQueueChanged?.Invoke();
    }

    public static void RaiseNativeCompletion()
    {
        NativeCompletion?.Invoke();
    }
}

[HarmonyPatch]
internal static class AutoBuyStructureQueuePatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.TypeByName("StructureSO")?.GetMethod(
            "QueueBuild",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(int) },
            null);
    }

    private static void Postfix()
    {
        AutoBuyLifecycleSignal.RaiseStructureQueueChanged();
    }
}

[HarmonyPatch]
internal static class AutoBuyUpgradeQueuePatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.TypeByName("UpgradeSO")?.GetMethod(
            "Purchase",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
    }

    private static void Postfix()
    {
        AutoBuyLifecycleSignal.RaiseUpgradeQueueChanged();
    }
}

[HarmonyPatch]
internal static class AutoBuyNativeCompletionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var typeName in new[] { "StructureSO", "UpgradeSO" })
        {
            var method = AccessTools.TypeByName(typeName)?.GetMethod(
                "CompleteAction",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (method is not null)
            {
                yield return method;
            }
        }
    }

    private static void Postfix()
    {
        AutoBuyLifecycleSignal.RaiseNativeCompletion();
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
