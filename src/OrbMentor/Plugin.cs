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
        if (target is null) { Logger.LogError("Orb Mentor blocked: native GainMasteryExp hook unavailable."); return; }
        _harmony = new Harmony(PluginIds.MentorGuid);
        _harmony.Patch(target, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterMasteryGain)));
        PatchOptional("AlchemyRecipeSO:GainMasteryXp", nameof(AfterAlchemyMasteryGain), postfix: true);
        PatchOptional("EquipmentSO:IncrementActive", nameof(BeforeArtifactTick), postfix: false);
        PatchFinalizer("EquipmentSO:IncrementActive", nameof(FinalizeArtifactTick));
        PatchOptional("ExperienceContainer:GainExperience", nameof(BeforeContainerGain), postfix: false);
        SceneManager.activeSceneChanged += OnSceneChanged;
        Logger.LogInfo($"Orb Mentor loaded. Mode={_config.Mode.Value}, Economy={_config.EconomyMode.Value}, Share={_config.SharePercent.Value:0.##}%.");
    }

    private void Update()
    {
        if (_config is null || _runtime is null) return;
        if (SceneManager.GetActiveScene().name == "Main" && _config.ToggleShortcut.Value.IsDown())
        {
            _config.Mode.Value = _config.Mode.Value == MentorOperationMode.Active ? MentorOperationMode.Disabled : MentorOperationMode.Active;
            _runtime.Cancel();
            Logger.LogInfo($"Orb Mentor is now {_config.Mode.Value}.");
        }
        var active = _config.Active;
        if (_wasActive && !active) _runtime.Cancel();
        _wasActive = active;
        if (SceneManager.GetActiveScene().name != "Main") { _button?.Dispose(); _button = null; return; }
        if (_button is not null && !_button.IsAlive) { _button.Dispose(); _button = null; }
        if (_button is not null) { _button.Render(); return; }
        _uiRetry -= Time.unscaledDeltaTime;
        if (_uiRetry <= 0) { _uiRetry = UiRetryIntervalSeconds; MentorToggleButton.TryCreate(_config, _runtime, out _button); }
    }

    private void LateUpdate() => _runtime?.LateTick();
    private static void AfterMasteryGain(SpellRecipeSO __instance, BigDouble exp) => Instance?._runtime?.Observe(__instance, exp);
    private static void AfterAlchemyMasteryGain(object __instance, BigDouble exp) => Instance?._runtime?.ObserveAlchemy(__instance, exp);
    private static void BeforeArtifactTick(object __instance) => Instance?._runtime?.BeginArtifactTick(__instance);
    private static Exception? FinalizeArtifactTick(Exception? __exception) { Instance?._runtime?.EndArtifactTick(); return __exception; }
    private static void BeforeContainerGain(object __instance, BigDouble __0) => Instance?._runtime?.ObserveExperienceContainer(__instance, __0);
    private void PatchOptional(string targetName, string patchName, bool postfix)
    {
        var target = AccessTools.Method(targetName);
        if (target is null) { Logger.LogWarning($"Orb Mentor optional domain hook unavailable: {targetName}."); return; }
        var patch = new HarmonyMethod(typeof(Plugin), patchName);
        if (postfix) _harmony!.Patch(target, postfix: patch); else _harmony!.Patch(target, prefix: patch);
    }
    private void PatchFinalizer(string targetName, string patchName)
    {
        var target = AccessTools.Method(targetName);
        if (target is null) { Logger.LogWarning($"Orb Mentor optional domain hook unavailable: {targetName}."); return; }
        _harmony!.Patch(target, finalizer: new HarmonyMethod(typeof(Plugin), patchName));
    }
    private void OnSceneChanged(Scene previous, Scene next) { _runtime?.Cancel(); _runtime?.ClearBlock(); }
    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        _button?.Dispose(); _button = null; _runtime?.Cancel(); _harmony?.UnpatchSelf(); _harmony = null; _runtime = null; Instance = null;
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
