using System;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using UnityEngine.SceneManagement;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// Auto Buy's two completion patch classes died with the rest of the signal patches, because Auto
/// Buy now reads finished structures and upgrades off the world snapshot. The one thing those
/// patches still did for somebody else — telling unmigrated Spell Leveling that a build or purchase
/// finished, so the spells it may have unlocked get looked at now rather than at the next idle
/// interval — moved into the plugin's optional-hook set, and that is what these pin.
/// </summary>
public sealed class PluginNativeCompletionHookTests : IDisposable
{
    public PluginNativeCompletionHookTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        SpellRecipeSO.All.Clear();
        SpellManager.instance = new SpellManager();
    }

    [Fact]
    public void CompletionHooksNameBothStructureAndUpgradeCompletion()
    {
        Assert.Equal(
            new[] { "StructureSO:CompleteAction", "UpgradeSO:CompleteAction" },
            global::OrbModding.Plugin.NativeCompletionHookTargets);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ANativeCompletionBringsTheNextSpellLevelingPassForwardWhilePlaying()
    {
        InstallLevelAllUpgrade();
        var first = AddReadySpell("00000000-0000-0000-0000-0000000000a1", mastery: 1);
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 16);
        long frame = 1;
        using var controller = new AutoSpellLevelController(
            ActiveConfig(),
            new ReflectionSpellLevelRuntime(),
            new ManualLogSource(),
            coordinator,
            () => frame);

        controller.Tick(0.1f);
        Assert.Equal(2, first.masteryLevel);

        // A second spell becomes ready, but the pass that would take it has just parked its timer.
        var second = AddReadySpell("00000000-0000-0000-0000-0000000000a2", mastery: 1);
        frame++;
        controller.Tick(0.0f);
        Assert.Equal(1, second.masteryLevel);

        RequireGameplayReady();
        InstallPlugin(controller);

        // Outside gameplay the completion is not ours to act on.
        SceneManager.ActiveScene = new Scene("MainMenu");
        InvokeCompletionHook();
        frame++;
        controller.Tick(0.0f);
        Assert.Equal(1, second.masteryLevel);

        // Inside it, the completion is exactly the news Spell Leveling was waiting for.
        SceneManager.ActiveScene = new Scene("Main");
        InvokeCompletionHook();
        frame++;
        controller.Tick(0.0f);
        Assert.Equal(2, second.masteryLevel);
    }

    public void Dispose()
    {
        InstanceProperty.SetValue(null, null);
        SceneManager.ActiveScene = new Scene("Main");
        IdScriptableObject.RuntimeLookup.Clear();
        SpellRecipeSO.All.Clear();
        SpellManager.instance = null;
    }

    private static PropertyInfo InstanceProperty =>
        typeof(global::OrbModding.Plugin).GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
        throw new MissingMemberException(nameof(global::OrbModding.Plugin), "Instance");

    /// <summary>
    /// The hook reads the suite-wide lifecycle monitor, which is a process-wide singleton with no
    /// reset, so the lease is captured only once the monitor is already playing.
    /// </summary>
    private static void RequireGameplayReady()
    {
        var monitor = GameLifecycleMonitor.Shared;
        if (monitor.Current.IsGameplayReady) return;
        monitor.TryObserve(
            new GameLifecycleObservation(
                GameLifecycleTransitionKind.RuntimeReady,
                monitor.Current.LastFrame + 1,
                "Main",
                "native completion hook pin"),
            out _,
            out var reason);
        Assert.True(monitor.Current.IsGameplayReady, reason);
    }

    private static void InstallPlugin(AutoSpellLevelController controller)
    {
        var plugin = new global::OrbModding.Plugin();
        SetField(plugin, "_autoSpellLevelController", controller);
        SetField(plugin, "_lifecycleLease", GameLifecycleMonitor.Shared.CaptureLease());
        InstanceProperty.SetValue(null, plugin);
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    private static void InvokeCompletionHook()
    {
        var method = typeof(global::OrbModding.Plugin).GetMethod(
            "AfterNativeCompletion",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(
                nameof(global::OrbModding.Plugin),
                "AfterNativeCompletion");
        method.Invoke(null, null);
    }

    private static BepInExAutomataConfiguration ActiveConfig()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoLevelSpells.Value = true;
        config.EnableOperationalLogging.Value = false;
        return config;
    }

    private static void InstallLevelAllUpgrade()
    {
        IdScriptableObject.RuntimeLookup.Add(
            new Guid(ReflectionSpellLevelRuntime.UnlockLevelAllSpellsUuid),
            new UpgradeSO
            {
                uuid = ReflectionSpellLevelRuntime.UnlockLevelAllSpellsUuid,
                level = 0,
            });
    }

    private static SpellRecipeSO AddReadySpell(string uuid, int mastery)
    {
        var recipe = new SpellRecipeSO
        {
            uuid = uuid,
            masteryLevel = mastery,
            discovered = true,
            readyToLevel = true,
        };
        recipe.levelingPrerequisites.available = true;
        SpellManager.instance!.availableSpellRecipes.value.Add(recipe);
        return recipe;
    }

    private sealed class ZeroClock : IPerformanceClock
    {
        public long GetTimestamp() => 0;

        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => 0;
    }
}
