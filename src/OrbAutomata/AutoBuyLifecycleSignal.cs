using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

internal static class AutoBuyLifecycleSignal
{
    [ThreadStatic]
    private static object? _automatedMutationIdentity;

    [ThreadStatic]
    private static int _automatedMutationDepth;

    public static event Action<GameLifecycleTransitionKind, object?>? Invalidated;

    public static event Action<object>? StructureQueueChanged;

    public static event Action<object>? UpgradeQueueChanged;

    public static event Action<object, AutoBuyCandidateKind>? NativeCompletion;

    public static void Raise(GameLifecycleTransitionKind kind, object? nativeIdentity)
    {
        Invalidated?.Invoke(kind, nativeIdentity);
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

    public static void RaiseNativeCompletion(object nativeIdentity, AutoBuyCandidateKind completedKind)
    {
        NativeCompletion?.Invoke(nativeIdentity, completedKind);
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
internal static class AutoBuyStructureCompletionPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.TypeByName("StructureSO")?.GetMethod(
            "CompleteAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
    }

    private static void Postfix(object __instance)
    {
        AutoBuyLifecycleSignal.RaiseNativeCompletion(__instance, AutoBuyCandidateKind.Structure);
    }
}

[HarmonyPatch]
internal static class AutoBuyUpgradeCompletionPatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.TypeByName("UpgradeSO")?.GetMethod(
            "CompleteAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
    }

    private static void Postfix(object __instance)
    {
        AutoBuyLifecycleSignal.RaiseNativeCompletion(__instance, AutoBuyCandidateKind.Upgrade);
    }
}

[HarmonyPatch]
internal static class AutoBuyLifecyclePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var gameManager = AccessTools.TypeByName("GameManager");
        var initGame = gameManager?.GetMethod(
            "InitGame",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (initGame is not null)
        {
            yield return initGame;
        }

        var resetGameState = gameManager?.GetMethod(
            "ResetGameState",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (resetGameState is not null)
        {
            yield return resetGameState;
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

        var resetManager = AccessTools.TypeByName("PersistentResetManager");
        var persistentReset = resetManager?.GetMethod(
            "PersistentResetLogic",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (persistentReset is not null)
        {
            yield return persistentReset;
        }
    }

    private static void Prefix(MethodBase __originalMethod, object __instance)
    {
        if (__originalMethod.DeclaringType?.Name == "SaveStateManager")
        {
            AutoBuyLifecycleSignal.Raise(GameLifecycleTransitionKind.SaveLoadStarted, __instance);
        }
        else if (__originalMethod.DeclaringType?.Name == "PersistentResetManager")
        {
            AutoBuyLifecycleSignal.Raise(GameLifecycleTransitionKind.NewGamePlusStarted, __instance);
        }
        else if (__originalMethod.DeclaringType?.Name == "GameManager" &&
                 __originalMethod.Name == "ResetGameState")
        {
            AutoBuyLifecycleSignal.Raise(GameLifecycleTransitionKind.ResetStarted, null);
        }
    }

    private static void Postfix(MethodBase __originalMethod, object __instance)
    {
        if (__originalMethod.DeclaringType?.Name == "SaveStateManager")
        {
            AutoBuyLifecycleSignal.Raise(GameLifecycleTransitionKind.SaveLoaded, __instance);
        }
        else if (__originalMethod.DeclaringType?.Name == "GameManager" &&
                 __originalMethod.Name == "InitGame")
        {
            AutoBuyLifecycleSignal.Raise(GameLifecycleTransitionKind.RegistryRebuilt, __instance);
            AutoBuyLifecycleSignal.Raise(GameLifecycleTransitionKind.RuntimeReady, __instance);
        }
    }
}
