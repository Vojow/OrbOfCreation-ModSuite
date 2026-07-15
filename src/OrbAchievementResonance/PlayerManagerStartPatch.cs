using System;
using HarmonyLib;

namespace OrbAchievementResonance;

internal static class PlayerManagerStartPatch
{
    public static bool TryInstall(Harmony harmony)
    {
        var applyEffects = AccessTools.Method("NumberVariable:ApplyEffects");
        var managerStart = AccessTools.Method("Player:ManagerStart");
        if (applyEffects is null || managerStart is null)
        {
            Plugin.Log?.LogWarning("Could not find Player.ManagerStart and NumberVariable.ApplyEffects; Achievement Resonance injection is inactive.");
            return false;
        }

        try
        {
            harmony.Patch(
                applyEffects,
                prefix: new HarmonyMethod(typeof(PlayerManagerStartPatch), nameof(ApplyEffectsPrefix)));
            harmony.Patch(
                managerStart,
                prefix: new HarmonyMethod(typeof(PlayerManagerStartPatch), nameof(ManagerStartPrefix)));
            return true;
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            Plugin.Log?.LogError($"Could not patch Achievement Resonance lifecycle hooks; injection is inactive. {ex}");
            return false;
        }
    }

    private static void ManagerStartPrefix(object __instance)
    {
        try
        {
            Plugin.Runtime?.BeforePlayerManagerStart(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Achievement Resonance ManagerStart prefix failed: {ex}");
        }
    }

    private static void ApplyEffectsPrefix(object __instance, int bonusLevels)
    {
        try
        {
            Plugin.Runtime?.BeforeNumberVariableApplyEffects(__instance, bonusLevels);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Achievement Resonance ApplyEffects prefix failed: {ex}");
        }
    }
}
