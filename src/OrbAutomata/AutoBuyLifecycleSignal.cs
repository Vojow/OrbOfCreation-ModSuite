using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OrbAutomata;

internal static class AutoBuyLifecycleSignal
{
    [ThreadStatic]
    private static object? _automatedMutationIdentity;

    [ThreadStatic]
    private static int _automatedMutationDepth;

    public static event Action? Invalidated;

    public static event Action<object>? StructureQueueChanged;

    public static event Action<object>? UpgradeQueueChanged;

    public static event Action? NativeCompletion;

    public static void Raise()
    {
        Invalidated?.Invoke();
    }

    public static AutomatedMutationScope EnterAutomatedMutation(object nativeIdentity)
    {
        return new AutomatedMutationScope(nativeIdentity);
    }

    public static void RaiseStructureQueueChanged(object nativeIdentity)
    {
        if (!IsAutomatedMutation(nativeIdentity))
        {
            StructureQueueChanged?.Invoke(nativeIdentity);
        }
    }

    public static void RaiseUpgradeQueueChanged(object nativeIdentity)
    {
        if (!IsAutomatedMutation(nativeIdentity))
        {
            UpgradeQueueChanged?.Invoke(nativeIdentity);
        }
    }

    public static void RaiseNativeCompletion()
    {
        NativeCompletion?.Invoke();
    }

    private static bool IsAutomatedMutation(object nativeIdentity)
    {
        return _automatedMutationDepth > 0 &&
               ReferenceEquals(_automatedMutationIdentity, nativeIdentity);
    }

    internal readonly struct AutomatedMutationScope : IDisposable
    {
        private readonly object? _previousIdentity;
        private readonly int _previousDepth;

        public AutomatedMutationScope(object nativeIdentity)
        {
            _previousIdentity = _automatedMutationIdentity;
            _previousDepth = _automatedMutationDepth;
            _automatedMutationIdentity = nativeIdentity;
            _automatedMutationDepth = _previousDepth + 1;
        }

        public void Dispose()
        {
            _automatedMutationIdentity = _previousIdentity;
            _automatedMutationDepth = _previousDepth;
        }
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

    private static void Postfix(object __instance)
    {
        AutoBuyLifecycleSignal.RaiseStructureQueueChanged(__instance);
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

    private static void Postfix(object __instance)
    {
        AutoBuyLifecycleSignal.RaiseUpgradeQueueChanged(__instance);
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
