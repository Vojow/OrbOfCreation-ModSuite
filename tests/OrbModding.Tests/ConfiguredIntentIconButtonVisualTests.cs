using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class ConfiguredIntentIconButtonVisualTests
{
    [Theory]
    [InlineData(
        false,
        FeatureStatusState.ConfigurationDisabled,
        FeatureStatusReasonCode.ConfigurationDisabled,
        0,
        0,
        "OFF")]
    [InlineData(
        true,
        FeatureStatusState.Operational,
        FeatureStatusReasonCode.None,
        1,
        1,
        "ON / OPERATIONAL")]
    [InlineData(
        true,
        FeatureStatusState.Faulted,
        FeatureStatusReasonCode.RuntimeFailure,
        2,
        1,
        "ON / FAULTED")]
    [InlineData(
        true,
        FeatureStatusState.TemporarilyBlocked,
        FeatureStatusReasonCode.EmergencyDisabled,
        3,
        1,
        "ON / STOPPED")]
    public void FeatureStatusMapsToFrameColorAndTooltip(
        bool configured,
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        int expectedStateValue,
        int expectedFrameValue,
        string expectedTooltip)
    {
        var expectedState = (ConfiguredIntentIconState)expectedStateValue;
        var expectedFrame = (ConfiguredIntentFrameTreatment)expectedFrameValue;
        var presentation = ConfiguredIntentIconButtonVisual.FromFeatureStatus(
            Status(configured, state, reason));

        Assert.Equal(expectedState, presentation.State);
        Assert.Equal(expectedFrame, presentation.FrameTreatment);
        Assert.Equal(expectedTooltip, presentation.TooltipLabel);
        Assert.Equal(
            expectedState switch
            {
                ConfiguredIntentIconState.On =>
                    ConfiguredIntentIconButtonVisual.OnColor,
                ConfiguredIntentIconState.Unhealthy =>
                    ConfiguredIntentIconButtonVisual.UnhealthyColor,
                ConfiguredIntentIconState.Stopped =>
                    ConfiguredIntentIconButtonVisual.StoppedColor,
                _ => ConfiguredIntentIconButtonVisual.OffColor,
            },
            presentation.Color);
    }

    [Fact]
    public void ConfiguredOnAndOffUseDifferentNativeFrameStructures()
    {
        var root = new GameObject("suite-control");
        var frame = root.AddComponent<Image>();
        var button = root.AddComponent<Button>();
        var glyph = new GameObject("glyph").AddComponent<Image>();
        glyph.transform.SetParent(root.transform, false);
        var inactive = new Sprite();
        var active = new Sprite();

        Assert.True(
            ConfiguredIntentIconButtonVisual.TryCreate(
                root,
                button,
                new[] { glyph },
                new NativeButtonStateVisualPrimitives(inactive, active),
                out var visual,
                out var reason),
            reason);

        visual!.Render(ConfiguredIntentIconButtonVisual.FromFeatureStatus(
            Status(false, FeatureStatusState.ConfigurationDisabled)));
        Assert.Same(inactive, frame.sprite);
        visual.Render(ConfiguredIntentIconButtonVisual.FromFeatureStatus(
            Status(true, FeatureStatusState.Operational)));
        Assert.Same(active, frame.sprite);
        Assert.NotSame(inactive, active);
    }

    [Fact]
    public void MissingStateFramePairCannotConstructALiveVisual()
    {
        var root = new GameObject("suite-control");
        root.AddComponent<Image>();
        var button = root.AddComponent<Button>();
        var glyph = new GameObject("glyph").AddComponent<Image>();

        Assert.False(
            ConfiguredIntentIconButtonVisual.TryCreate(
                root,
                button,
                new[] { glyph },
                new NativeButtonStateVisualPrimitives(new Sprite(), null!),
                out var visual,
                out var reason));
        Assert.Null(visual);
        Assert.Contains("inactive/active state frame pair", reason);
    }

    [Fact]
    public void ClaimedControlLeavesNoHoverOrPressPixelTarget()
    {
        var root = new GameObject("suite-control");
        var frame = root.AddComponent<Image>();
        var button = root.AddComponent<Button>();
        var effects = root.AddComponent<FakeImageEffects>();
        button.targetGraphic = frame;
        var suiteColor = ConfiguredIntentIconButtonVisual.OnColor;
        frame.color = suiteColor;

        ConfiguredIntentButtonVisualOwnership.Claim(button, typeof(FakeImageEffects));
        button.interactable = false;
        button.interactable = true;

        Assert.Null(button.targetGraphic);
        Assert.False(effects.enabled);
        Assert.Equal(suiteColor, frame.color);
    }

    [Fact]
    public void AutoHarvestQuickControlPublishesOneCommittedStoreGenerationPerClick()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var publications = 0;
        var store = new AutomataConfigurationStore(config, (_, _) => publications++);
        var control = new AutoHarvestToggleControl(
            store,
            () => Status(
                store.Current.AutoHarvest.Mode == AutoHarvestOperationMode.Active,
                FeatureStatusState.Operational));

        Assert.False(control.IsOn);
        control.Toggle();

        Assert.True(control.IsOn);
        Assert.Equal(AutoHarvestOperationMode.Active, config.AutoHarvestMode.Value);
        Assert.Equal(1, publications);
        Assert.Equal(2UL, store.CurrentGeneration.Value);
    }

    [Fact]
    public void AutoItemsAndAutoScribePublishWholeFeatureModesThroughTheCommittedStore()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var publications = 0;
        var store = new AutomataConfigurationStore(config, (_, _) => publications++);
        var items = new AutoItemsToggleControl(
            store,
            () => Status(
                store.Current.AutoItems.Mode == AutoItemsOperationMode.Active,
                FeatureStatusState.Operational));
        var scribe = new AutoScribeToggleControl(
            store,
            () => Status(
                store.Current.AutoScribe.Mode == AutoScribeOperationMode.Active,
                FeatureStatusState.Operational));

        items.Toggle();
        scribe.Toggle();

        Assert.True(items.IsOn);
        Assert.True(scribe.IsOn);
        Assert.Equal(AutoItemsOperationMode.Active, config.AutoItemsMode.Value);
        Assert.Equal(AutoScribeOperationMode.Active, config.AutoScribeMode.Value);
        Assert.Equal(2, publications);
        Assert.Equal(3UL, store.CurrentGeneration.Value);
    }

    [Fact]
    public void MentorCommandsPublishThroughTheCommittedStore()
    {
        var file = new ConfigFile();
        var config = BepInExAutomataConfiguration.Bind(file);
        var mentor = MentorConfig.Bind(file);
        config.AttachMentor(mentor);
        var publications = 0;
        var store = new AutomataConfigurationStore(config, (_, _) => publications++);

        store.ToggleMentor();

        Assert.Equal(MentorOperationMode.Active, mentor.Mode.Value);
        Assert.Equal(MentorOperationMode.Active, store.Current.Mentor.Mode);
        Assert.Equal(1, publications);
        Assert.Equal(2UL, store.CurrentGeneration.Value);
    }

    private static FeatureStatusSnapshot Status(
        bool configured,
        FeatureStatusState state,
        FeatureStatusReasonCode reason = FeatureStatusReasonCode.None)
    {
        if (state != FeatureStatusState.Operational &&
            reason == FeatureStatusReasonCode.None)
            reason = configured
                ? FeatureStatusReasonCode.GameplayNotReady
                : FeatureStatusReasonCode.ConfigurationDisabled;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "feature"),
            "Feature",
            configured,
            state,
            reason == FeatureStatusReasonCode.None
                ? default
                : new FeatureStatusReason(reason, "reason"));
    }

    private sealed class FakeImageEffects : Behaviour
    {
    }
}
