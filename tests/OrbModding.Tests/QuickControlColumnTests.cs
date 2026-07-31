using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class QuickControlColumnTests
{
    [Fact]
    public void ColumnBuildsEveryRegisteredAutomationFeaturePlusEmergencyStopWithoutAnEquippedSpell()
    {
        using var context = new Context();
        var previousSpellManager = global::SpellManager.instance;
        global::SpellManager.instance = new global::SpellManager();
        try
        {
            Assert.Empty(global::SpellManager.instance.activeSpells.value);
            Assert.True(
                QuickControlColumn.TryCreate(
                    context.Registry,
                    context.EmergencyStop,
                    context.Native,
                    allowFeatureControls: true,
                    out var column,
                    out var reason,
                    context.ResolveIcon),
                reason);
            using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
            {
                Assert.Equal(
                    context.Registry.Features
                        .Select(feature => feature.FeatureId)
                        .Append(QuickControlColumn.EmergencyStopId)
                        .OrderBy(value => value, StringComparer.Ordinal),
                    liveColumn.ControlIds.OrderBy(value => value, StringComparer.Ordinal));
                Assert.Empty(liveColumn.Failures);
            }
        }
        finally
        {
            global::SpellManager.instance = previousSpellManager;
        }
    }

    [Fact]
    public void MissingAuditedSpriteCreatesNoLiveControlAndNamesTheFeature()
    {
        using var context = new Context();
        bool Resolve(
            AutomationFeatureControlRegistration registration,
            out Sprite? icon,
            out string reason)
        {
            if (registration.FeatureId == AutomataFeatureStatuses.AutoItemsFeatureId)
            {
                icon = null;
                reason = "ScreenWorld capture missing";
                return false;
            }
            return context.ResolveIcon(registration, out icon, out reason);
        }

        Assert.True(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                context.Native,
                allowFeatureControls: true,
                out var column,
                out var reason,
                Resolve),
            reason);
        using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
        {
            Assert.False(liveColumn.TryGetButton(
                AutomataFeatureStatuses.AutoItemsFeatureId,
                out _));
            Assert.Contains(
                AutomataFeatureStatuses.AutoItemsFeatureId,
                liveColumn.Failures.Keys);
            Assert.Contains("Auto Items", reason);
            Assert.Contains("ScreenWorld capture missing", reason);
        }
    }

    [Fact]
    public void MissingAnchorCreatesNoColumnAndNamesTheContract()
    {
        using var context = new Context();
        var missingAnchor = new QuickControlNativePrimitives(
            null!,
            context.Native.StateVisuals);

        Assert.False(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                missingAnchor,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon));
        Assert.Null(column);
        Assert.Contains("HelpButtons anchor", reason);
    }

    [Fact]
    public void UnconstructibleStateVisualCreatesNoColumn()
    {
        using var context = new Context();
        var missingActiveFrame = new QuickControlNativePrimitives(
            context.Native.Anchor,
            new NativeButtonStateVisualPrimitives(new Sprite(), null!));

        Assert.False(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                missingActiveFrame,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon));
        Assert.Null(column);
        Assert.Contains("inactive/active state frame pair", reason);
    }

    [Fact]
    public void EmergencyStopButtonMutatesSafetyEmergencyDisableImmediately()
    {
        using var context = new Context();
        Assert.True(
            QuickControlColumn.TryCreate(
                context.Registry,
                context.EmergencyStop,
                context.Native,
                allowFeatureControls: true,
                out var column,
                out var reason,
                context.ResolveIcon),
            reason);
        using (var liveColumn = Assert.IsType<QuickControlColumn>(column))
        {
            Assert.True(liveColumn.TryGetButton(
                QuickControlColumn.EmergencyStopId,
                out var button));

            button.onClick.Invoke();

            Assert.True(context.Configuration.EmergencyDisable.Value);
            Assert.Equal(new[] { true }, context.EmergencyChanges);
            Assert.True(liveColumn.TryGetPresentation(
                QuickControlColumn.EmergencyStopId,
                out var presentation));
            Assert.Equal(
                ConfiguredIntentFrameTreatment.ActiveRaised,
                presentation.FrameTreatment);
            Assert.Equal("STOPPED", presentation.TooltipLabel);
            var tooltip = button.gameObject.GetComponent<HoverTooltip>()?.tooltipItem;
            Assert.NotNull(tooltip);
            Assert.Equal("STOPPED", tooltip!.GetDisplayType());
            Assert.Equal(presentation.Color, tooltip.GetColor());
        }
    }

    [Fact]
    public void AnchorResolutionRequiresTheExactAuditedHelpButtonsStructure()
    {
        var canvasObject = new GameObject("Canvas");
        var contentObject = Child(canvasObject, "ContentArea");
        var contentArea = contentObject.AddComponent<FakeContentArea>();
        contentArea.canvas = (RectTransform)canvasObject.transform;
        var helpButtons = Child(canvasObject, "HelpButtons");
        Child(helpButtons, "SettingsButton");
        Child(helpButtons, "PlayerStatsButton");

        Assert.True(
            QuickControlNativeAdapter.TryResolveAnchor(
                new Component[] { contentArea },
                out var anchor,
                out var reason),
            reason);
        Assert.Same(helpButtons.transform, anchor);

        var missing = new GameObject("WrongCanvas");
        var wrongContent = Child(missing, "ContentArea").AddComponent<FakeContentArea>();
        wrongContent.canvas = (RectTransform)missing.transform;
        Assert.False(
            QuickControlNativeAdapter.TryResolveAnchor(
                new Component[] { wrongContent },
                out var absent,
                out var missingReason));
        Assert.Null(absent);
        Assert.Contains(QuickControlNativeAdapter.AnchorPath, missingReason);
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private sealed class FakeContentArea : Behaviour
    {
        public RectTransform? canvas;
    }

    private sealed class Context : IDisposable
    {
        private readonly Dictionary<string, Sprite> _icons = new(StringComparer.Ordinal);

        internal Context()
        {
            var file = new ConfigFile();
            Configuration = BepInExAutomataConfiguration.Bind(file);
            var mentor = MentorConfig.Bind(file);
            Configuration.AttachMentor(mentor);
            Store = new AutomataConfigurationStore(Configuration, (_, _) => { });
            Statuses = new AutomataFeatureStatuses(
                Store.Current,
                lifecycleGeneration: 1,
                configurationGeneration: Store.CurrentGeneration);
            Registry = AutomationFeatureControlRegistry.Create(
                Store,
                Statuses,
                new SpellLevelCapabilityState(),
                mentor);
            EmergencyStop = new EmergencyStopControl(
                Store,
                () => Array.Empty<string>(),
                stopped => EmergencyChanges.Add(stopped));
            var anchor = (RectTransform)new GameObject("HelpButtons").transform;
            Native = new QuickControlNativePrimitives(
                anchor,
                new NativeButtonStateVisualPrimitives(new Sprite(), new Sprite()));
        }

        internal BepInExAutomataConfiguration Configuration { get; }
        internal AutomataConfigurationStore Store { get; }
        internal AutomataFeatureStatuses Statuses { get; }
        internal AutomationFeatureControlRegistry Registry { get; }
        internal EmergencyStopControl EmergencyStop { get; }
        internal List<bool> EmergencyChanges { get; } = new();
        internal QuickControlNativePrimitives Native { get; }

        internal bool ResolveIcon(
            AutomationFeatureControlRegistration registration,
            out Sprite? icon,
            out string reason)
        {
            if (registration.PageLabel == "Auto Cast")
            {
                return NativeFeatureIconResolver.TryResolve(
                    registration.PageLabel,
                    capturedRail: null,
                    out icon,
                    out reason);
            }
            if (!_icons.TryGetValue(registration.FeatureId, out var resolved))
            {
                resolved = new Sprite();
                _icons.Add(registration.FeatureId, resolved);
            }
            icon = resolved;
            reason = string.Empty;
            return true;
        }

        public void Dispose() => Statuses.Dispose();
    }
}
