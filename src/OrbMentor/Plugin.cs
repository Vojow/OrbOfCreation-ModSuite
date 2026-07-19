using System;
using BepInEx;
using HarmonyLib;
using OrbModding.Common;
using UnityEngine.SceneManagement;
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Linq;
using BepInEx.Logging;

namespace OrbMentor;

[BepInPlugin(PluginIds.MentorGuid, PluginIds.MentorName, PluginIds.MentorVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private const float UiRetryIntervalSeconds = 5.0f;
    private Harmony? _harmony;
    private MentorConfig? _config;
    private MentorRuntime? _runtime;
    private MentorGameplayInvalidationBridge? _invalidationBridge;
    private MentorToggleButton? _button;
    private float _uiRetry;
    private bool _wasActive;
    private long _lifecycleGeneration;
    private GameLifecycleLease _lifecycleLease;
    internal static Plugin? Instance { get; private set; }
    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        _config = MentorConfig.Bind(Config);
        _runtime = new MentorRuntime(
            _config,
            Logger,
            SuitePerformanceCoordinator.Shared,
            () => Time.frameCount,
            featureStatusRegistry: FeatureStatusRegistry.Shared);
        _invalidationBridge = new MentorGameplayInvalidationBridge(GameplayInvalidationBus.Shared);
        _wasActive = _config.Active;
        var audit = GameAssemblyAudit.Check(Paths.GameRootPath);
        if (!audit.MatchesExpected) Logger.LogWarning("Game assemblies differ from the audited baseline; Mentor will fail closed if its native contract is unavailable.");
        var target = AccessTools.Method("SpellRecipeSO:GainMasteryExp");
        if (target is null) { _runtime.BlockPermanent("native GainMasteryExp hook unavailable"); return; }
        _harmony = new Harmony(PluginIds.MentorGuid);
        try { _harmony.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterMasteryGain))); }
        catch (Exception ex)
        {
            _runtime.BlockPermanent($"native GainMasteryExp hook failed: {ex.GetBaseException().Message}");
            return;
        }
        PatchDomainRequired("AlchemyRecipeSO:GainMasteryXp", nameof(AfterAlchemyMasteryGain), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("EquipmentSO:IncrementActive", nameof(BeforeArtifactTick), MentorDomain.Artifacts, postfix: false);
        PatchDomainRequired("EquipmentSO:IncrementActive", nameof(FinalizeArtifactTick), MentorDomain.Artifacts, postfix: false, finalizer: true);
        PatchDomainRequired("ExperienceContainer:GainExperience", nameof(BeforeContainerGain), MentorDomain.Artifacts, postfix: false);
        PatchRequired("SpellRecipeSO:Discover", nameof(AfterSpellProgression));
        PatchRequired("SpellRecipeSO:PurchaseLevel", nameof(AfterSpellProgression));
        PatchDomainRequired("AlchemyRecipeSO:Discover", nameof(AfterAlchemyProgression), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("AlchemyRecipeSO:ApplyMastery", nameof(AfterAlchemyProgression), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("EquipmentSO:Discover", nameof(AfterArtifactProgression), MentorDomain.Artifacts, postfix: true);
        PatchDomainRequired("EquipmentSO:Create", nameof(AfterArtifactProgression), MentorDomain.Artifacts, postfix: true);
        PatchDomainRequired("EquipmentSO:GainMasteryLevels", nameof(AfterArtifactProgression), MentorDomain.Artifacts, postfix: true);
        PatchRequired("SpellRecipeSO:ResetData", nameof(AfterNativeProgressionReset));
        PatchDomainRequired("AlchemyRecipeSO:ResetData", nameof(AfterAlchemyNativeReset), MentorDomain.Alchemy, postfix: true);
        PatchDomainRequired("EquipmentSO:ResetData", nameof(AfterArtifactNativeReset), MentorDomain.Artifacts, postfix: true);
        PatchOptional("SaveStateManager:ImplementLoadedJson", nameof(BeforeSaveLoad), postfix: false);
        PatchOptional("SaveStateManager:ImplementLoadedJson", nameof(AfterSaveLoaded), postfix: true);
        PatchOptional("GameManager:InitGame", nameof(AfterGameInitialized), postfix: true);
        PatchOptional("GameManager:ResetGameState", nameof(BeforeRuntimeReset), postfix: false);
        PatchOptional("PersistentResetManager:PersistentResetLogic", nameof(BeforePersistentReset), postfix: false);
        PatchOptional("SpellManager:AddSpell", nameof(AfterSpellLoadoutChanged), postfix: true);
        PatchOptional("SpellManager:RemoveSpell", nameof(AfterSpellLoadoutChanged), postfix: true);
        PatchOptional("SpellManager:MoveSpell", nameof(AfterSpellLoadoutChanged), postfix: true);
        GameLifecycleMonitor.Shared.Transitioned += OnLifecycleTransition;
        SceneManager.activeSceneChanged += OnSceneChanged;
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, SceneManager.GetActiveScene().name);
        _lifecycleGeneration = GameLifecycleMonitor.Shared.Current.Generation;
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();
        Logger.LogInfo($"Orb Mentor loaded. Mode={_config.Mode.Value}, Sources={_config.SpellSourcePolicy.Value}, Economy={_config.EconomyMode.Value}, Share={_config.SharePercent.Value:0.##}%.");
    }

    private void Update()
    {
        if (_config is null || _runtime is null) return;
        if (SceneManager.GetActiveScene().name == "Main" && _config.ToggleShortcut.Value.IsDown())
        {
            _config.Mode.Value = _config.Mode.Value == MentorOperationMode.Active ? MentorOperationMode.Disabled : MentorOperationMode.Active;
            _runtime.Cancel(MentorDropReason.Disabled);
            _runtime.RefreshFeatureStatus();
            Logger.LogInfo($"Orb Mentor is now {_config.Mode.Value}.");
        }
        var active = _config.Active;
        if (_wasActive && !active) _runtime.Cancel(MentorDropReason.Disabled);
        _wasActive = active;
        if (SceneManager.GetActiveScene().name != "Main") { _button?.Dispose(); _button = null; return; }
        if (_button is not null && !_button.IsAlive) { _button.Dispose(); _button = null; }
        if (_button is not null) { _button.Render(); return; }
        _uiRetry -= Time.unscaledDeltaTime;
        if (_uiRetry <= 0) { _uiRetry = UiRetryIntervalSeconds; MentorToggleButton.TryCreate(_config, _runtime, out _button); }
    }

    private void LateUpdate()
    {
        if (_config?.Active == true && _runtime?.IsBlocked != true)
            _invalidationBridge?.Pump(Time.frameCount);
        if (IsGameplayScene()) _runtime?.LateTick();
    }
    private static void AfterMasteryGain(SpellRecipeSO __instance, BigDouble exp)
    {
        if (IsGameplayScene()) Instance?._runtime?.Observe(__instance, exp);
    }
    private static void AfterAlchemyMasteryGain(object __instance, BigDouble exp)
    {
        if (IsGameplayScene()) Instance?._runtime?.ObserveAlchemy(__instance, exp);
    }
    private static void BeforeArtifactTick(object __instance)
    {
        if (IsGameplayScene()) Instance?._runtime?.BeginArtifactTick(__instance);
    }
    private static Exception? FinalizeArtifactTick(Exception? __exception) { Instance?._runtime?.EndArtifactTick(__exception is null); return __exception; }
    private static void BeforeContainerGain(object __instance, BigDouble __0)
    {
        if (IsGameplayScene()) Instance?._runtime?.ObserveExperienceContainer(__instance, __0);
    }
    private static void BeforeSaveLoad(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.SaveLoadStarted, SceneManager.GetActiveScene().name, __instance);
    private static void AfterSaveLoaded(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.SaveLoaded, SceneManager.GetActiveScene().name, __instance);
    private static void AfterGameInitialized(object __instance)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.RegistryRebuilt, SceneManager.GetActiveScene().name, __instance);
        ObserveLifecycle(GameLifecycleTransitionKind.RuntimeReady, SceneManager.GetActiveScene().name, __instance);
    }
    private static void BeforePersistentReset(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.NewGamePlusStarted, SceneManager.GetActiveScene().name, __instance);
    private static void BeforeRuntimeReset() =>
        ObserveLifecycle(GameLifecycleTransitionKind.ResetStarted, SceneManager.GetActiveScene().name);
    private static void AfterSpellLoadoutChanged()
    {
        var plugin = Instance;
        plugin?._runtime?.NotifyEquippedLoadoutChanged();
        plugin?._invalidationBridge?.PublishSpellLoadout(Time.frameCount);
    }
    private static void AfterSpellProgression(object __instance) =>
        PublishProgression(MentorDomain.Spells, __instance);
    private static void AfterAlchemyProgression(object __instance) =>
        PublishProgression(MentorDomain.Alchemy, __instance);
    private static void AfterArtifactProgression(object __instance) =>
        PublishProgression(MentorDomain.Artifacts, __instance);
    private static void AfterNativeProgressionReset()
    {
        var plugin = Instance;
        plugin?._runtime?.RequestLifecycleReset();
        plugin?._invalidationBridge?.PublishProgression(MentorDomain.Spells, Time.frameCount, null);
    }
    private static void AfterAlchemyNativeReset()
    {
        var plugin = Instance;
        plugin?._runtime?.RequestDomainReset(MentorDomain.Alchemy);
        plugin?._invalidationBridge?.PublishProgression(MentorDomain.Alchemy, Time.frameCount, null);
    }
    private static void AfterArtifactNativeReset()
    {
        var plugin = Instance;
        plugin?._runtime?.RequestDomainReset(MentorDomain.Artifacts);
        plugin?._invalidationBridge?.PublishProgression(MentorDomain.Artifacts, Time.frameCount, null);
    }

    private static void PublishProgression(MentorDomain domain, object changedSource)
    {
        var plugin = Instance;
        plugin?._runtime?.MarkRelationshipDirty(domain, changedSource);
        var entityId = plugin?._runtime?.TryGetStableProgressionEntityId(domain, changedSource, out var stableId) == true
            ? stableId
            : null;
        plugin?._invalidationBridge?.PublishProgression(domain, Time.frameCount, entityId);
    }
    private static bool IsGameplayScene() =>
        Instance is { } plugin &&
        SceneManager.GetActiveScene().name == "Main" &&
        GameLifecycleMonitor.Shared.Current.IsGameplayReady &&
        GameLifecycleMonitor.Shared.IsCurrent(plugin._lifecycleLease);
    private void PatchOptional(string targetName, string patchName, bool postfix)
    {
        var target = AccessTools.Method(targetName);
        if (target is null) { Logger.LogWarning($"Orb Mentor optional domain hook unavailable: {targetName}."); return; }
        var patch = new HarmonyMethod(typeof(Plugin), patchName);
        try
        {
            if (postfix) _harmony!.Patch(target, postfix: patch); else _harmony!.Patch(target, prefix: patch);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Orb Mentor optional lifecycle hook failed: {targetName}: {ex.GetBaseException().Message}");
        }
    }
    private void PatchRequired(string targetName, string patchName)
    {
        var target = AccessTools.Method(targetName);
        if (target is null)
        {
            _runtime?.BlockPermanent($"required lifecycle hook unavailable: {targetName}");
            return;
        }
        try { _harmony!.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), patchName)); }
        catch (Exception ex)
        {
            _runtime?.BlockPermanent($"required lifecycle hook failed: {targetName}: {ex.GetBaseException().Message}");
        }
    }
    private void PatchDomainRequired(
        string targetName,
        string patchName,
        MentorDomain domain,
        bool postfix,
        bool finalizer = false)
    {
        var target = AccessTools.Method(targetName);
        if (target is null)
        {
            _runtime?.QuarantineDomain(domain, $"required {domain} hook unavailable: {targetName}");
            return;
        }
        var patch = new HarmonyMethod(typeof(Plugin), patchName);
        try
        {
            if (finalizer) _harmony!.Patch(target, finalizer: patch);
            else if (postfix) _harmony!.Patch(target, postfix: patch);
            else _harmony!.Patch(target, prefix: patch);
        }
        catch (Exception ex)
        {
            _runtime?.QuarantineDomain(domain, $"required {domain} hook failed: {targetName}: {ex.GetBaseException().Message}");
        }
    }
    private void OnSceneChanged(Scene previous, Scene next)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.SceneExited, previous.name);
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, next.name);
    }

    private void OnLifecycleTransition(GameLifecycleTransition transition)
    {
        if (transition.Current.Generation == _lifecycleGeneration) return;
        _lifecycleGeneration = transition.Current.Generation;
        _runtime?.ResetLifecycle();
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();
    }

    private static void ObserveLifecycle(
        GameLifecycleTransitionKind kind,
        string sceneName,
        object? nativeIdentity = null)
    {
        GameLifecycleMonitor.Shared.TryObserve(
            new GameLifecycleObservation(
                kind,
                Time.frameCount,
                sceneName,
                PluginIds.MentorGuid,
                nativeIdentity),
            out _,
            out _);
    }

    private void OnDestroy()
    {
        GameLifecycleMonitor.Shared.Transitioned -= OnLifecycleTransition;
        SceneManager.activeSceneChanged -= OnSceneChanged;
        _button?.Dispose(); _button = null; _runtime?.Dispose(); _harmony?.UnpatchSelf(); _harmony = null; _runtime = null; _invalidationBridge = null; Instance = null;
    }

    internal static void ShowNotice(string message, RectTransform? anchor)
    {
        try
        {
            var nodeType = Type.GetType("TooltipNode, Assembly-CSharp", false);
            var popupType = Type.GetType("UIPopupText, Assembly-CSharp", false);
            if (nodeType is null || popupType is null || anchor is null) return;
            var node = Activator.CreateInstance(nodeType, message, Color.white);
            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(nodeType);
            var list = (IList)Activator.CreateInstance(listType)!; list.Add(node);
            var method = popupType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == "CreateOn" && m.GetParameters().Length == 3);
            method?.Invoke(null, new object[] { list, anchor, new Vector2(0, 32) });
        }
        catch { }
    }
}
