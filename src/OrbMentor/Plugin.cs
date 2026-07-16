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
    private MentorToggleButton? _button;
    private float _uiRetry;
    private bool _wasActive;
    internal static Plugin? Instance { get; private set; }
    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        _config = MentorConfig.Bind(Config);
        _runtime = new MentorRuntime(_config, Logger);
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
        PatchOptional("SaveStateManager:ImplementLoadedJson", nameof(AfterLifecycleReset), postfix: true);
        PatchOptional("Player:ManagerStart", nameof(AfterLifecycleReset), postfix: true);
        SceneManager.activeSceneChanged += OnSceneChanged;
        Logger.LogInfo($"Orb Mentor loaded. Mode={_config.Mode.Value}, Economy={_config.EconomyMode.Value}, Share={_config.SharePercent.Value:0.##}%.");
    }

    private void Update()
    {
        if (_config is null || _runtime is null) return;
        if (SceneManager.GetActiveScene().name == "Main" && _config.ToggleShortcut.Value.IsDown())
        {
            _config.Mode.Value = _config.Mode.Value == MentorOperationMode.Active ? MentorOperationMode.Disabled : MentorOperationMode.Active;
            _runtime.Cancel(MentorDropReason.Disabled);
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
        if (SceneManager.GetActiveScene().name == "Main") _runtime?.LateTick();
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
    private static void AfterLifecycleReset()
    {
        Instance?._runtime?.RequestLifecycleReset();
    }
    private static void AfterSpellProgression(object __instance) =>
        Instance?._runtime?.MarkRelationshipDirty(MentorDomain.Spells, __instance);
    private static void AfterAlchemyProgression(object __instance) =>
        Instance?._runtime?.MarkRelationshipDirty(MentorDomain.Alchemy, __instance);
    private static void AfterArtifactProgression(object __instance) =>
        Instance?._runtime?.MarkRelationshipDirty(MentorDomain.Artifacts, __instance);
    private static void AfterNativeProgressionReset() => Instance?._runtime?.RequestLifecycleReset();
    private static void AfterAlchemyNativeReset() => Instance?._runtime?.RequestDomainReset(MentorDomain.Alchemy);
    private static void AfterArtifactNativeReset() => Instance?._runtime?.RequestDomainReset(MentorDomain.Artifacts);
    private static bool IsGameplayScene() => SceneManager.GetActiveScene().name == "Main";
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
    private void OnSceneChanged(Scene previous, Scene next) => _runtime?.ResetLifecycle();
    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        _button?.Dispose(); _button = null; _runtime?.Cancel(MentorDropReason.LifecycleReset); _harmony?.UnpatchSelf(); _harmony = null; _runtime = null; Instance = null;
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
