using System;
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
    private const float UiRetryIntervalSeconds = 5f;
    private const float UiIntegrityIntervalSeconds = 5f;

    private ConfigEntry<bool>? _enabled;
    private ConfigEntry<bool>? _enableUiShell;
    private float _mainSceneElapsed;
    private float _uiRetrySeconds;
    private float _uiIntegritySeconds;
    private bool _uiFailureLogged;
    private bool _uiMaintenanceDue;
    private int _deferInstallUntilFrame;
    private ModConfigUiShell? _uiShell;
    private ConfigCatalogSnapshot? _catalog;
    private ModConfigCoordinatorWork? _uiWork;
    private long _lifecycleGeneration;

    private void Awake()
    {
        _enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            new ConfigDescription(
                "Enable Orb Mod Config.",
                null,
                new ModConfigMetadata(0, 0, hidden: true)));
        _enableUiShell = Config.Bind(
            "Interface",
            "EnableButtonShell",
            true,
            new ConfigDescription(
                "Insert the Mods top-bar button and in-game configuration editor.",
                null,
                new ModConfigMetadata(10, 0, hidden: true)));

        if (!_enabled.Value)
        {
            Logger.LogInfo("Orb Mod Config is disabled by configuration.");
            return;
        }

        _uiWork = new ModConfigCoordinatorWork(
            SuitePerformanceCoordinator.Shared,
            () => Time.frameCount);
        GameLifecycleMonitor.Shared.Transitioned += OnLifecycleTransition;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, SceneManager.GetActiveScene().name);
        _lifecycleGeneration = GameLifecycleMonitor.Shared.Current.Generation;
        ResetSceneState(SceneManager.GetActiveScene());
    }

    private void Update()
    {
        if (_enabled?.Value != true)
        {
            DeactivateUiWork(disposeShell: true);
            return;
        }

        if (SceneManager.GetActiveScene().name != "Main")
        {
            DeactivateUiWork(disposeShell: false);
            return;
        }

        _mainSceneElapsed += Time.unscaledDeltaTime;
        if (_mainSceneElapsed < UiInstallDelaySeconds)
        {
            _uiWork?.SetState(true, false);
            return;
        }

        if (_enableUiShell?.Value != true)
        {
            DeactivateUiWork(disposeShell: true);
            return;
        }

        if (_uiShell is not null && !_uiShell.IsAlive)
            _uiMaintenanceDue = true;
        else if (_uiShell is null)
        {
            if (Time.frameCount >= _deferInstallUntilFrame)
            {
                _uiRetrySeconds -= Math.Max(0.0f, Time.unscaledDeltaTime);
                if (_uiRetrySeconds <= 0.0f) _uiMaintenanceDue = true;
            }
        }
        else
        {
            _uiShell.Tick(Time.unscaledDeltaTime);
            if (AdvanceCadence(ref _uiIntegritySeconds, Time.unscaledDeltaTime, UiIntegrityIntervalSeconds))
                _uiMaintenanceDue = true;
        }

        _uiWork?.TryRun(true, _uiMaintenanceDue, RunUiMaintenance);
        _uiWork?.SetState(true, _uiMaintenanceDue);
    }

    private void RunUiMaintenance()
    {
        _uiMaintenanceDue = false;
        if (_uiShell is not null && !_uiShell.IsAlive)
        {
            _uiShell.Dispose();
            _uiShell = null;
            _uiRetrySeconds = 0f;
            _deferInstallUntilFrame = Time.frameCount + 1;
            return;
        }
        if (_uiShell is not null)
        {
            _uiShell.RefreshNavigation();
            _uiIntegritySeconds = UiIntegrityIntervalSeconds;
            return;
        }

        _uiRetrySeconds = UiRetryIntervalSeconds;
        var catalog = ModConfigCatalogSession.GetOrDiscover(
            ref _catalog,
            ConfigCatalog.DiscoverLoaded,
            LogCatalog);
        if (!ModConfigUiShell.TryCreate(
                Logger, catalog, out _uiShell, out var reason, MarkUiMaintenanceDue))
        {
            if (!_uiFailureLogged)
            {
                _uiFailureLogged = true;
                Logger.LogWarning("Mod Config UI is not ready; installation will retry: " + reason);
            }
            return;
        }
        _uiFailureLogged = false;
        _uiIntegritySeconds = UiIntegrityIntervalSeconds;
    }

    private void LogCatalog(ConfigCatalogSnapshot catalog)
    {
        Logger.LogInfo(
            $"Orb Mod Config loaded. UiShell={_enableUiShell?.Value == true}; " +
            $"DiscoveredMods={catalog.Mods.Count}, DiscoveredSettings={catalog.SettingCount}.");
        foreach (var mod in catalog.Mods)
        {
            Logger.LogInfo(
                $"Mod Config catalog: {mod.Name} {mod.Version} ({mod.Guid}); " +
                $"Sections={mod.Sections.Count}, Settings={mod.Sections.Sum(section => section.Settings.Count)}.");
        }
    }

    private void MarkUiMaintenanceDue() => _uiMaintenanceDue = true;

    private void DeactivateUiWork(bool disposeShell)
    {
        _uiWork?.SetState(false, false);
        _uiMaintenanceDue = false;
        if (!disposeShell || _uiShell is null) return;
        _uiShell.Dispose();
        _uiShell = null;
        _uiRetrySeconds = 0f;
        _uiFailureLogged = false;
    }

    private void OnDestroy()
    {
        _uiShell?.Dispose();
        _uiShell = null;
        _uiWork?.Dispose();
        _uiWork = null;
        GameLifecycleMonitor.Shared.Transitioned -= OnLifecycleTransition;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.SceneExited, previous.name);
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, next.name);
    }

    private void OnLifecycleTransition(GameLifecycleTransition transition)
    {
        if (transition.Current.Generation == _lifecycleGeneration) return;
        _lifecycleGeneration = transition.Current.Generation;
        if (transition.Current.LastTransition != GameLifecycleTransitionKind.SceneEntered) return;
        _uiShell?.Dispose();
        _uiShell = null;
        ResetSceneState(SceneManager.GetActiveScene());
    }

    private static void ObserveLifecycle(GameLifecycleTransitionKind kind, string sceneName)
    {
        GameLifecycleMonitor.Shared.TryObserve(
            new GameLifecycleObservation(
                kind,
                Time.frameCount,
                sceneName,
                PluginIds.ModConfigGuid),
            out _,
            out _);
    }

    private void ResetSceneState(Scene scene)
    {
        _mainSceneElapsed = 0f;
        _uiRetrySeconds = 0f;
        _uiIntegritySeconds = 0f;
        _uiFailureLogged = false;
        _uiMaintenanceDue = false;
        _deferInstallUntilFrame = 0;
        _uiWork?.SetState(scene.name == "Main", false);
    }

    internal static bool AdvanceCadence(ref float remainingSeconds, float elapsedSeconds, float intervalSeconds)
    {
        remainingSeconds -= Math.Max(0.0f, elapsedSeconds);
        if (remainingSeconds > 0.0f) return false;
        remainingSeconds = Math.Max(0.1f, intervalSeconds);
        return true;
    }
}
