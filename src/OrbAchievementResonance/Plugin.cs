using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAchievementResonance;

[BepInPlugin(PluginIds.AchievementResonanceGuid, PluginIds.AchievementResonanceName, PluginIds.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    private Harmony? _harmony;
    private ResonanceConfig? _config;
    private ResonanceRuntime? _runtime;

    internal static ManualLogSource Log { get; private set; } = null!;

    internal static ResonanceRuntime? Runtime { get; private set; }

    private void Awake()
    {
        Log = Logger;
        _config = new ResonanceConfig(Config);
        _runtime = new ResonanceRuntime(_config, Log);
        Runtime = _runtime;

        LogAssemblyStatus();

        if (!_config.Enabled.Value)
        {
            Log.LogInfo("Achievement Resonance is disabled by configuration.");
            return;
        }

        if (_config.LogCatalogOnStartup.Value)
        {
            _runtime.LogTargetCatalog();
        }

        _harmony = new Harmony(PluginIds.AchievementResonanceGuid);
        if (!PlayerManagerStartPatch.TryInstall(_harmony))
        {
            Log.LogWarning("Achievement Resonance loaded without its lifecycle hooks.");
            return;
        }
        Log.LogInfo(
            "Achievement Resonance loaded. " +
            $"ApplyNativeEffectBlocks={_config.ApplyNativeEffectBlocks.Value}; " +
            "native Achievement Strength injection and capped modifier refresh hooks are ready.");
    }

    private void OnDestroy()
    {
        if (_config?.CleanupOwnedBlocksOnDestroy.Value == true)
        {
            _runtime?.CleanupOwnedBlocks();
        }

        _harmony?.UnpatchSelf();
        _harmony = null;
        Runtime = null;
        _runtime = null;
    }

    private void LogAssemblyStatus()
    {
        if (_config?.WarnOnAssemblyMismatch.Value != true)
        {
            return;
        }

        var audit = GameAssemblyAudit.Check(Paths.GameRootPath);
        if (audit.MatchesExpected)
        {
            Log.LogInfo("Game assemblies match the audited baseline.");
            return;
        }

        Log.LogWarning("Game assemblies differ from the audited baseline. Re-run the runtime probe before trusting modifier targets.");
    }
}
