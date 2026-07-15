using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbModConfig;

[BepInPlugin(PluginIds.ModConfigGuid, PluginIds.ModConfigName, PluginIds.ModConfigVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private const float UiInstallDelaySeconds = 2f;

    private ConfigEntry<bool>? _enabled;
    private ConfigEntry<bool>? _enableUiShell;
    private float _mainSceneElapsed;
    private bool _shellInstallAttempted;
    private ModConfigUiShell? _uiShell;
    private ConfigCatalogSnapshot? _catalog;

    private void Awake()
    {
        _enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            new ConfigDescription(
                "Enable Orb Mod Config.",
                null,
                new ModConfigMetadata(0, 0)));
        _enableUiShell = Config.Bind(
            "Interface",
            "EnableButtonShell",
            true,
            new ConfigDescription(
                "Insert the Mods top-bar button and in-game configuration editor.",
                null,
                new ModConfigMetadata(10, 0)));

        if (!_enabled.Value)
        {
            Logger.LogInfo("Orb Mod Config is disabled by configuration.");
            return;
        }

        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ResetSceneState(SceneManager.GetActiveScene());
    }

    private void Start()
    {
        if (_enabled?.Value != true)
        {
            _uiShell?.Dispose();
            _uiShell = null;
            return;
        }

        // Start runs after BepInEx has constructed the other plugin components, so
        // the catalog is not dependent on this plugin's chainloader order.
        _catalog = ConfigCatalog.DiscoverLoaded();
        Logger.LogInfo(
            $"Orb Mod Config loaded. UiShell={_enableUiShell?.Value == true}; " +
            $"DiscoveredMods={_catalog.Mods.Count}, DiscoveredSettings={_catalog.SettingCount}.");
        foreach (var mod in _catalog.Mods)
        {
            Logger.LogInfo(
                $"Mod Config catalog: {mod.Name} {mod.Version} ({mod.Guid}); " +
                $"Sections={mod.Sections.Count}, Settings={mod.Sections.Sum(section => section.Settings.Count)}.");
        }
    }

    private void Update()
    {
        if (_enabled?.Value != true)
        {
            return;
        }

        if (SceneManager.GetActiveScene().name != "Main")
        {
            return;
        }

        _mainSceneElapsed += Time.unscaledDeltaTime;
        if (_mainSceneElapsed < UiInstallDelaySeconds)
        {
            return;
        }

        if (_enableUiShell?.Value == true)
        {
            if (_uiShell is null && !_shellInstallAttempted)
            {
                _shellInstallAttempted = true;
                _catalog ??= ConfigCatalog.DiscoverLoaded();
                if (!ModConfigUiShell.TryCreate(Logger, _catalog, out _uiShell, out var reason))
                {
                    Logger.LogWarning("Mod Config UI shell was not installed: " + reason);
                }
            }
        }
        else if (_uiShell is not null)
        {
            _uiShell.Dispose();
            _uiShell = null;
            _shellInstallAttempted = false;
        }
    }

    private void OnDestroy()
    {
        _uiShell?.Dispose();
        _uiShell = null;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        _uiShell?.Dispose();
        _uiShell = null;
        ResetSceneState(next);
    }

    private void ResetSceneState(Scene scene)
    {
        _mainSceneElapsed = 0f;
        _shellInstallAttempted = scene.name != "Main";
    }
}
