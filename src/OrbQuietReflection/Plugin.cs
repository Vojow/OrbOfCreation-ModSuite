using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using OrbModding.Common;

namespace OrbQuietReflection;

[BepInPlugin(
    PluginIds.QuietReflectionGuid,
    PluginIds.QuietReflectionName,
    PluginIds.QuietReflectionVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private Harmony? _harmony;
    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<bool> _suppressReflectiveNotifications = null!;
    private bool _loggedContractFailure;

    private void Awake()
    {
        _enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "Enable Quiet Reflection.");
        _suppressReflectiveNotifications = Config.Bind(
            "Notifications",
            "SuppressReflectiveSplashNotifications",
            true,
            "Hide popup entries from Reflective learning passives. Their effects and cooldowns are unchanged.");

        _enabled.SettingChanged += OnConfigChanged;
        _suppressReflectiveNotifications.SettingChanged += OnConfigChanged;
        ReflectiveQuietPatch.ContractFailure = OnContractFailure;
        RefreshState();

        if (!TryInstallPatch())
        {
            ReflectiveQuietPatch.SuppressionEnabled = false;
            return;
        }

        Logger.LogInfo(
            "Quiet Reflection loaded. Reflective passive splash notifications are " +
            (ReflectiveQuietPatch.SuppressionEnabled ? "suppressed." : "enabled by configuration."));
    }

    private void OnDestroy()
    {
        _enabled.SettingChanged -= OnConfigChanged;
        _suppressReflectiveNotifications.SettingChanged -= OnConfigChanged;
        ReflectiveQuietPatch.SuppressionEnabled = false;
        ReflectiveQuietPatch.ContractFailure = null;
        _harmony?.UnpatchSelf();
        _harmony = null;
    }

    private void OnConfigChanged(object sender, EventArgs args)
    {
        RefreshState();
    }

    private void RefreshState()
    {
        ReflectiveQuietPatch.SuppressionEnabled =
            _enabled.Value && _suppressReflectiveNotifications.Value;
    }

    private bool TryInstallPatch()
    {
        var target = AccessTools.Method("PassiveAbilitySO:IsQuiet");
        if (!HasExpectedContract(target))
        {
            Logger.LogWarning(
                "PassiveAbilitySO.IsQuiet() does not match the audited contract; " +
                "no notification behavior was changed.");
            return false;
        }

        try
        {
            _harmony = new Harmony(PluginIds.QuietReflectionGuid);
            _harmony.Patch(
                target!,
                postfix: new HarmonyMethod(typeof(ReflectiveQuietPatch), nameof(ReflectiveQuietPatch.Postfix)));
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                $"Could not install the Reflective notification filter; no behavior was changed. " +
                $"{ex.GetType().Name}: {ex.Message}");
            _harmony?.UnpatchSelf();
            _harmony = null;
            return false;
        }
    }

    private static bool HasExpectedContract(MethodInfo? target)
    {
        return target is not null &&
               !target.IsStatic &&
               target.ReturnType == typeof(bool) &&
               target.GetParameters().Length == 0;
    }

    private void OnContractFailure(Exception ex)
    {
        if (_loggedContractFailure)
        {
            return;
        }

        _loggedContractFailure = true;
        Logger.LogWarning(
            $"A Reflective passive type could not be validated; leaving its notifications unchanged. " +
            $"{ex.GetType().Name}: {ex.Message}");
    }
}
