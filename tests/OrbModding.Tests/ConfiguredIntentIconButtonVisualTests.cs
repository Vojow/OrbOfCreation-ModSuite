using BepInEx.Configuration;
using System;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common;
using OrbMentor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class ConfiguredIntentIconButtonVisualTests
{
    [Fact]
    public void FeatureQuickControlsCannotRetainATextRenderingPath()
    {
        var featureTypes = new[]
        {
            typeof(AutoBuyToggleButton),
            typeof(AutoCastToggleButton),
            typeof(AutoConceptToggleButton),
            typeof(AutoHarvestToggleButton),
            typeof(AutomataFeatureToggleButton),
            typeof(MentorToggleButton),
        };

        foreach (var featureType in featureTypes)
        {
            var fields = featureType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.DoesNotContain(fields, field => field.FieldType == typeof(TextMeshProUGUI));
            Assert.Contains(
                fields,
                field => field.Name == "_visual" &&
                         field.FieldType == typeof(ConfiguredIntentIconButtonVisual));
        }

        var featureFactory = typeof(ConfiguredIntentIconButtonVisual).GetMethod(
            "TryCreateFeature",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(featureFactory);
        Assert.DoesNotContain(
            featureFactory!.GetParameters(),
            parameter => parameter.ParameterType == typeof(string));
        Assert.DoesNotContain(
            typeof(ConfiguredIntentIconButtonVisual).GetMethods(
                BindingFlags.Static | BindingFlags.NonPublic)
                .Where(method => method.Name != "TryCreateStop"),
            method => method.Name.Contains("Text", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, FeatureStatusState.ConfigurationDisabled, FeatureStatusReasonCode.ConfigurationDisabled, 0)]
    [InlineData(true, FeatureStatusState.Operational, FeatureStatusReasonCode.None, 1)]
    [InlineData(true, FeatureStatusState.NotReady, FeatureStatusReasonCode.GameplayNotReady, 1)]
    [InlineData(true, FeatureStatusState.Degraded, FeatureStatusReasonCode.PartialCapabilityUnavailable, 2)]
    [InlineData(true, FeatureStatusState.ContractUnavailable, FeatureStatusReasonCode.ContractUnavailable, 2)]
    [InlineData(true, FeatureStatusState.Faulted, FeatureStatusReasonCode.RuntimeFailure, 2)]
    [InlineData(true, FeatureStatusState.TemporarilyBlocked, FeatureStatusReasonCode.EmergencyDisabled, 3)]
    public void FeatureStatusMapsToTheCompleteQuickIconMatrix(
        bool configured,
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        int expected)
    {
        var status = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "feature"),
            "Feature",
            configured,
            state,
            reason == FeatureStatusReasonCode.None
                ? default
                : new FeatureStatusReason(reason, "reason"));

        Assert.Equal((ConfiguredIntentIconState)expected, ConfiguredIntentIconButtonVisual.FromFeatureStatus(status));
    }

    [Fact]
    public void ClaimedControlDisablesNativeEffectsAndLeavesNoHoverOrPressPixelTarget()
    {
        var root = new GameObject("suite-control");
        var frame = root.AddComponent<Image>();
        var button = root.AddComponent<Button>();
        var effects = root.AddComponent<FakeImageEffects>();
        button.targetGraphic = frame;
        var suiteColor = new Color(0.4f, 1.0f, 0.55f, 1.0f);
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
            () => new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                store.Current.AutoHarvest.Mode == AutoHarvestOperationMode.Active,
                store.Current.AutoHarvest.Mode == AutoHarvestOperationMode.Active
                    ? FeatureStatusState.Operational
                    : FeatureStatusState.ConfigurationDisabled));

        Assert.False(control.IsOn);
        control.Toggle();

        Assert.True(control.IsOn);
        Assert.Equal(AutoHarvestOperationMode.Active, config.AutoHarvestMode.Value);
        Assert.Equal(1, publications);
        Assert.Equal(2UL, store.CurrentGeneration.Value);
    }

    [Fact]
    public void AutoItemsAndAutoScribeQuickControlsUseTheCommittedConfigurationStore()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var publications = 0;
        var store = new AutomataConfigurationStore(config, (_, _) => publications++);
        var items = new AutoItemsToggleControl(
            store,
            () => Status(
                AutomataFeatureStatuses.AutoItemsFeatureId,
                "Auto Items",
                store.Current.AutoItems.Mode == AutoItemsOperationMode.Active));
        var scribe = new AutoScribeToggleControl(
            store,
            () => Status(
                AutomataFeatureStatuses.AutoScribeFeatureId,
                "Auto Scribe",
                store.Current.AutoScribe.Mode == AutoScribeOperationMode.Active));

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

    private sealed class FakeImageEffects : Behaviour
    {
    }

    private static FeatureStatusSnapshot Status(
        string featureId,
        string displayName,
        bool configured) =>
        new(
            new FeatureStatusKey(PluginIds.SuiteGuid, featureId),
            displayName,
            configured,
            configured
                ? FeatureStatusState.Operational
                : FeatureStatusState.ConfigurationDisabled);
}
