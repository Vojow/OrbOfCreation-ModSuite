using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorGameplayInvalidationBridgeTests
{
    [Fact]
    public void PublishesCanonicalTypedProgressionAndBroadFallback()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var bridge = new MentorGameplayInvalidationBridge(bus);
        var received = new List<GameplayInvalidation>();
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(GameplayInvalidationKind.Progression),
            received.Add,
            nameof(PublishesCanonicalTypedProgressionAndBroadFallback));

        bridge.PublishProgression(
            MentorDomain.Spells,
            frame: 4,
            entityId: "a39a2748-2bc4-4ad0-9872-2a29f5c88c90");
        bridge.PublishProgression(MentorDomain.Artifacts, frame: 4, entityId: "not-a-stable-uuid");
        bridge.Pump(frame: 5);

        Assert.Collection(
            received,
            targeted =>
            {
                Assert.Equal(GameplayInvalidationKind.Progression, targeted.Kinds);
                Assert.Equal(GameplayInvalidationDomains.MentorSpells, targeted.Domain);
                Assert.Equal("a39a2748-2bc4-4ad0-9872-2a29f5c88c90", targeted.EntityId);
                Assert.Equal("SpellRecipeSO", targeted.ExpectedTypeName);
                Assert.Equal(PluginIds.MentorGuid, targeted.Source);
            },
            broad =>
            {
                Assert.Equal(GameplayInvalidationDomains.MentorArtifacts, broad.Domain);
                Assert.Empty(broad.EntityId);
                Assert.Empty(broad.ExpectedTypeName);
            });
    }

    [Fact]
    public void PublishesBroadSpellLoadoutInventoryChange()
    {
        var monitor = new GameLifecycleMonitor(() => 1);
        using var bus = new GameplayInvalidationBus(monitor, readThreadId: () => 1);
        var bridge = new MentorGameplayInvalidationBridge(bus);
        GameplayInvalidation received = default;
        using var subscription = bus.Subscribe(
            new GameplayInvalidationFilter(
                GameplayInvalidationKind.Inventory,
                GameplayInvalidationDomains.MentorSpellLoadout),
            change => received = change,
            nameof(PublishesBroadSpellLoadoutInventoryChange));

        bridge.PublishSpellLoadout(frame: 7);
        bridge.Pump(frame: 8);

        Assert.Equal(GameplayInvalidationKind.Inventory, received.Kinds);
        Assert.Equal(GameplayInvalidationDomains.MentorSpellLoadout, received.Domain);
        Assert.True(received.IsBroad);
        Assert.Equal(PluginIds.MentorGuid, received.Source);
    }
}
