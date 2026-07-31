using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class QuickControlColumnTests
{
    [Fact]
    public void GameObjectTransformMatchesTheInstalledUnityConstructorContract()
    {
        var ordinary = new GameObject("Ordinary");
        var ui = new GameObject("Ui", typeof(RectTransform));

        Assert.IsType<Transform>(ordinary.transform);
        Assert.IsType<RectTransform>(ui.transform);
    }

    [Fact]
    public void RectTransformMismatchNamesTheMemberAndExpectedVersusActualTypes()
    {
        var ordinary = new GameObject("Ordinary");

        Assert.False(
            QuickControlColumn.TryRequireRectTransform(
                ordinary,
                "UnityEngine.GameObject('Ordinary').transform",
                out var rect,
                out var reason));

        Assert.Null(rect);
        Assert.Contains("UnityEngine.GameObject('Ordinary').transform", reason);
        Assert.Contains("expected UnityEngine.RectTransform", reason);
        Assert.Contains("actual UnityEngine.Transform", reason);
    }

    [Theory]
    [InlineData("Auto Buy")]
    [InlineData("Auto Cast")]
    [InlineData("Auto Harvest")]
    [InlineData("Mentor")]
    public void TooltipableFeatureIconsResolveThroughExactNativeReturnTypes(string pageLabel)
    {
        Assert.True(
            NativeFeatureIconResolver.TryResolve(
                pageLabel,
                capturedRail: null,
                out var icon,
                out var reason),
            reason);
        Assert.NotNull(icon);
    }

    [Fact]
    public void DrawerBuildsEveryRegisteredAutomationFeatureWithoutAnEquippedSpell()
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
                        .Append(QuickControlColumn.DrawerControlId)
                        .OrderBy(value => value, StringComparer.Ordinal),
                    liveColumn.ControlIds.OrderBy(value => value, StringComparer.Ordinal));
                Assert.Equal(
                    context.Registry.Features.Select(feature => feature.FeatureId),
                    liveColumn.DrawerControlIds);
                Assert.Empty(liveColumn.Failures);
                var columnRoot = Assert.IsType<RectTransform>(
                    context.Native.Anchor.GetChild(0));
                Assert.Equal(QuickControlColumn.ObjectName, columnRoot.name);
                AssertAllRectTransforms(columnRoot);
            }
        }
        finally
        {
            global::SpellManager.instance = previousSpellManager;
        }
    }

    [Fact]
    public void ClosedFootprintHasExactlyTwoLiveControlsAndDisclosureTogglesTheRightwardDrawer()
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
            var root = context.Native.Anchor.GetChild(0);
            var drawer = Assert.IsType<RectTransform>(FindDescendant(
                root,
                QuickControlColumn.DrawerObjectName));
            Assert.False(drawer.gameObject.activeSelf);
            Assert.True(drawer.anchoredPosition.x > 0f);
            Assert.Equal(2, CountLiveButtons(root));

            Assert.True(liveColumn.TryGetButton(
                QuickControlColumn.DrawerControlId,
                out var disclosure));
            disclosure.onClick.Invoke();

            Assert.True(liveColumn.IsDrawerOpen);
            Assert.True(drawer.gameObject.activeSelf);
            Assert.Equal(2 + context.Registry.Features.Count, CountLiveButtons(root));
            Assert.False(DirectChild(disclosure.transform, "ClosedGlyph").gameObject.activeSelf);
            Assert.True(DirectChild(disclosure.transform, "OpenGlyph").gameObject.activeSelf);

            disclosure.onClick.Invoke();

            Assert.False(liveColumn.IsDrawerOpen);
            Assert.False(drawer.gameObject.activeSelf);
            Assert.Equal(2, CountLiveButtons(root));
        }
    }

    [Fact]
    public void FaultedFeatureRaisesColorAndStructuralAttentionOnTheClosedDrawer()
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
                QuickControlColumn.DrawerControlId,
                out var disclosure));
            Assert.True(liveColumn.TryGetDrawerPresentation(out var healthy));
            Assert.False(healthy.IsOpen);
            Assert.False(healthy.HasAttention);
            Assert.False(DirectChild(
                disclosure.transform,
                "AttentionMarker").gameObject.activeSelf);

            context.Statuses.AutoItems.Observe(
                configuredEnabled: true,
                FeatureStatusState.Faulted,
                FeatureStatusReasonCode.RuntimeFailure,
                "Auto Items failed.");
            liveColumn.Render();

            Assert.True(liveColumn.TryGetDrawerPresentation(out var attention));
            Assert.False(attention.IsOpen);
            Assert.True(attention.HasAttention);
            Assert.Equal(
                ConfiguredIntentFrameTreatment.InactiveRecessed,
                attention.FrameTreatment);
            Assert.Equal(ConfiguredIntentIconButtonVisual.UnhealthyColor, attention.Color);
            Assert.True(DirectChild(
                disclosure.transform,
                "AttentionMarker").gameObject.activeSelf);
            Assert.True(DirectChild(disclosure.transform, "ClosedGlyph").gameObject.activeSelf);
            Assert.False(DirectChild(disclosure.transform, "OpenGlyph").gameObject.activeSelf);
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
            Assert.DoesNotContain(
                AutomataFeatureStatuses.AutoItemsFeatureId,
                liveColumn.DrawerControlIds);
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

            button.onClick.Invoke();

            Assert.False(context.Configuration.EmergencyDisable.Value);
            Assert.Equal(new[] { true, false }, context.EmergencyChanges);
            Assert.True(liveColumn.TryGetPresentation(
                QuickControlColumn.EmergencyStopId,
                out var cleared));
            Assert.Equal(
                ConfiguredIntentFrameTreatment.InactiveRecessed,
                cleared.FrameTreatment);
            Assert.Equal("READY / STOP ALL", cleared.TooltipLabel);
        }
    }

    [Fact]
    public void AnchorResolutionRequiresTheExactAuditedHelpButtonsStructure()
    {
        var canvasObject = new GameObject("Canvas", typeof(RectTransform));
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

        var missing = new GameObject("WrongCanvas", typeof(RectTransform));
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

    [Fact]
    public void AnchorResolutionNamesCanvasDeclaredTypeMismatch()
    {
        var contentObject = new GameObject("ContentArea");
        var contentArea = contentObject.AddComponent<WrongCanvasContentArea>();
        contentArea.canvas = contentObject.transform;

        Assert.False(
            QuickControlNativeAdapter.TryResolveAnchor(
                new Component[] { contentArea },
                out var anchor,
                out var reason));

        Assert.Null(anchor);
        Assert.Contains("WrongCanvasContentArea.canvas declared type check failed", reason);
        Assert.Contains("expected UnityEngine.RectTransform", reason);
        Assert.Contains("actual UnityEngine.Transform", reason);
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
        for (var index = 0; index < root.childCount; index++)
        {
            var candidate = FindDescendant(root.GetChild(index), name);
            if (candidate is not null) return candidate;
        }
        return null!;
    }

    private static Transform DirectChild(Transform parent, string name)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        }
        return null!;
    }

    private static int CountLiveButtons(Transform root, bool ancestorsActive = true)
    {
        var active = ancestorsActive && root.gameObject.activeSelf;
        var count = active && root.gameObject.GetComponent<Button>() is not null ? 1 : 0;
        for (var index = 0; index < root.childCount; index++)
            count += CountLiveButtons(root.GetChild(index), active);
        return count;
    }

    private static void AssertAllRectTransforms(Transform root)
    {
        Assert.IsType<RectTransform>(root);
        for (var index = 0; index < root.childCount; index++)
            AssertAllRectTransforms(root.GetChild(index));
    }

    private sealed class FakeContentArea : Behaviour
    {
        public RectTransform? canvas;
    }

    private sealed class WrongCanvasContentArea : Behaviour
    {
        public Transform? canvas;
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
                registry: new FeatureStatusRegistry(),
                configurationGeneration: Store.CurrentGeneration);
            Registry = AutomationFeatureControlRegistry.Create(
                Store,
                Statuses,
                new SpellLevelCapabilityState(),
                mentor);
            EmergencyStop = new EmergencyStopControl(
                Store,
                stopped => EmergencyChanges.Add(stopped));
            var anchor = (RectTransform)new GameObject(
                "HelpButtons",
                typeof(RectTransform)).transform;
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
