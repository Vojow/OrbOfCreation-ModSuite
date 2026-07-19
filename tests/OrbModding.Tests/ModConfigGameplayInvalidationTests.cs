using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbModConfig;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigGameplayInvalidationTests
{
    [Fact]
    public void SuccessfulApplyPublishesExactCommittedConfigurationTargets()
    {
        const string pluginGuid = "com.example.configuration";
        var config = new ConfigFile();
        config.Bind(
            "InternalSection",
            "Enabled",
            true,
            new ConfigDescription(
                "Enabled",
                null,
                new ModConfigMetadata(
                    0,
                    0,
                    displaySection: "Friendly Section",
                    displayName: "Friendly Name")));
        config.Bind("InternalSection", "Count", 2, "Count");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource(pluginGuid, "Visible Plugin Name", "1.0.0", config),
        });
        var settings = catalog.Mods.Single().Sections
            .SelectMany(section => section.Settings)
            .ToDictionary(setting => setting.Key);
        var session = new ConfigEditSession(catalog);
        session.Get(settings["Enabled"]).Stage("false");
        session.Get(settings["Count"]).Stage("4");

        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Configuration,
                GameplayInvalidationDomains.ModConfig),
            received.Add);

        Assert.True(session.Apply(out var error, out var appliedSettings), error);
        Assert.Equal(2, ModConfigInvalidationPublisher.PublishAppliedSettings(bus, 9, appliedSettings));
        bus.Pump(10, GameplayInvalidationBus.DefaultMaxOperationsPerFrame);

        Assert.Equal(2, received.Count);
        Assert.All(received, change =>
        {
            Assert.Equal(GameplayInvalidationKind.Configuration, change.Kinds);
            Assert.Equal(GameplayInvalidationDomains.ModConfig, change.Domain);
            Assert.Equal(string.Empty, change.ExpectedTypeName);
            Assert.Equal(PluginIds.ModConfigGuid, change.Source);
        });
        Assert.Equal(
            new[]
            {
                ModConfigInvalidationPublisher.CreateEntityId(pluginGuid, "InternalSection", "Count"),
                ModConfigInvalidationPublisher.CreateEntityId(pluginGuid, "InternalSection", "Enabled"),
            },
            received.Select(change => change.EntityId).OrderBy(value => value, StringComparer.Ordinal));
        Assert.DoesNotContain(received, change => change.EntityId.Contains("Friendly", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidationFailurePublishesNothing()
    {
        var config = new ConfigFile();
        config.Bind("General", "Enabled", true, "Enabled");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("invalid.plugin", "Invalid", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        session.Values.Single().Stage("not-a-boolean");

        AssertFailedApplyPublishesNothing(session);
    }

    [Fact]
    public void SaveFailureRollbackPublishesNothing()
    {
        var config = new ConfigFile { ThrowOnSaveCall = 1 };
        config.Bind("General", "Value", 1, "Value");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("save.failure", "Save Failure", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        session.Values.Single().Stage("2");

        AssertFailedApplyPublishesNothing(session);
    }

    [Fact]
    public void SettingChangedRollbackPublishesNothing()
    {
        var config = new ConfigFile();
        var entry = config.Bind("General", "Value", 1, "Value");
        entry.SettingChanged += (_, _) => throw new InvalidOperationException("simulated subscriber failure");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("subscriber.failure", "Subscriber Failure", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        session.Values.Single().Stage("2");

        AssertFailedApplyPublishesNothing(session);
    }

    private static void AssertFailedApplyPublishesNothing(ConfigEditSession session)
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Configuration,
                GameplayInvalidationDomains.ModConfig),
            received.Add);

        Assert.False(session.Apply(out _, out var appliedSettings));
        Assert.Empty(appliedSettings);
        Assert.Equal(0, ModConfigInvalidationPublisher.PublishAppliedSettings(bus, 4, appliedSettings));
        bus.Pump(5, GameplayInvalidationBus.DefaultMaxOperationsPerFrame);

        Assert.Empty(received);
        Assert.Equal(0, bus.GetSnapshot().Published);
    }
}
