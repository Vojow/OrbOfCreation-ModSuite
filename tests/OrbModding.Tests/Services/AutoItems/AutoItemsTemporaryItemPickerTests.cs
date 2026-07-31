using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems;

public sealed class AutoItemsTemporaryItemPickerTests : IDisposable
{
    public AutoItemsTemporaryItemPickerTests() => ConsumableSO.All.Clear();

    public void Dispose() => ConsumableSO.All.Clear();

    [Fact]
    public void DiscoveryCaptureListsOnlyVisibleTemporaryItemsInFamilyThenNameOrder()
    {
        var potionIcon = new Sprite();
        var fruitIcon = new Sprite();
        var potion = Item(
            Guid.Parse("00000000-0000-0000-0000-000000000201"),
            KnownEntities.ConsumablePotionType.Uuid,
            "Clear Potion",
            visible: true,
            stock: 2,
            potionIcon);
        var fruit = Item(
            Guid.Parse("00000000-0000-0000-0000-000000000202"),
            KnownEntities.ConsumableFruitType.Uuid,
            "Bright Fruit",
            visible: true,
            stock: 5,
            fruitIcon);
        Item(
            Guid.NewGuid(),
            KnownEntities.ConsumableThreadType.Uuid,
            "Hidden Thread",
            visible: false,
            stock: 4,
            new Sprite());
        Item(
            Guid.NewGuid(),
            KnownEntities.ConsumableRelicType.Uuid,
            "Visible Relic",
            visible: true,
            stock: 1,
            new Sprite());

        var snapshot = AutoItemsTemporaryItemCatalog.Capture();

        Assert.True(snapshot.IsAvailable, snapshot.FailureReason);
        Assert.Collection(
            snapshot.Options,
            option =>
            {
                Assert.Equal(fruit.GetGuid(), option.ItemId);
                Assert.Equal(AutoItemsConsumableFamily.Fruit, option.Family);
                Assert.Equal("Bright Fruit", option.DisplayName);
                Assert.Equal(5, option.Stock);
                Assert.Same(fruitIcon, option.Icon);
            },
            option =>
            {
                Assert.Equal(potion.GetGuid(), option.ItemId);
                Assert.Equal(AutoItemsConsumableFamily.Potion, option.Family);
                Assert.Equal("Clear Potion", option.DisplayName);
                Assert.Equal(2, option.Stock);
                Assert.Same(potionIcon, option.Icon);
            });
    }

    [Fact]
    public void ToggleRoundTripsExactStagedUuidsAndPreservesUnknownStoredEntries()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var later = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var unknown = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var staged = AutoItemsTemporaryItemPickerModel.Toggle($"invalid,{unknown:D}", later);
        staged = AutoItemsTemporaryItemPickerModel.Toggle(staged, first);

        Assert.Equal($"{first:D},{later:D},{unknown:D},invalid", staged);
        Assert.Equal(
            $"{first:D},{unknown:D},invalid",
            AutoItemsTemporaryItemPickerModel.Toggle(staged, later));
    }

    [Fact]
    public void PresentationAlwaysStatesTheDiscoveredApprovalCount()
    {
        var first = Option(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Fruit");
        var second = Option(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Potion");
        var catalog = AutoItemsTemporaryItemCatalogSnapshot.Available(new[] { first, second });

        var empty = AutoItemsTemporaryItemPickerModel.Compose(catalog, string.Empty);
        var one = AutoItemsTemporaryItemPickerModel.Compose(catalog, first.ItemId.ToString("D"));

        Assert.Equal("0 of 2 approved", empty.ApprovalStateLine);
        Assert.Equal("1 of 2 approved", one.ApprovalStateLine);
        Assert.False(empty.Items[0].IsApproved);
        Assert.True(one.Items[0].IsApproved);
    }

    [Fact]
    public void UnknownUuidAndMalformedStoredTokenRenderExplicitlyAndCanBeRemoved()
    {
        var known = Option(Guid.Parse("20000000-0000-0000-0000-000000000001"), "Known");
        var unknown = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var catalog = AutoItemsTemporaryItemCatalogSnapshot.Available(new[] { known });
        var staged = $"{known.ItemId:D},{unknown:D},not-a-uuid";

        var presentation = AutoItemsTemporaryItemPickerModel.Compose(catalog, staged);

        Assert.Collection(
            presentation.UnresolvableEntries,
            entry =>
            {
                Assert.True(entry.IsUuid);
                Assert.Equal(unknown, entry.ItemId);
                Assert.Equal("Unresolvable stored UUID", entry.Heading);
            },
            entry =>
            {
                Assert.False(entry.IsUuid);
                Assert.Equal("not-a-uuid", entry.StoredToken);
                Assert.Equal("Invalid stored value", entry.Heading);
            });

        staged = AutoItemsTemporaryItemPickerModel.Remove(
            staged,
            presentation.UnresolvableEntries[0]);
        var afterUuidRemoval = AutoItemsTemporaryItemPickerModel.Compose(catalog, staged);
        Assert.Single(afterUuidRemoval.UnresolvableEntries);
        staged = AutoItemsTemporaryItemPickerModel.Remove(
            staged,
            afterUuidRemoval.UnresolvableEntries[0]);
        Assert.Equal(known.ItemId.ToString("D"), staged);
    }

    [Fact]
    public void FailedDiscoveryAndGenuinelyEmptyDiscoveryAreDifferentStates()
    {
        var stored = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var empty = AutoItemsTemporaryItemPickerModel.Compose(
            AutoItemsTemporaryItemCatalogSnapshot.Available(Array.Empty<AutoItemsTemporaryItemOption>()),
            string.Empty);
        var failed = AutoItemsTemporaryItemPickerModel.Compose(
            AutoItemsTemporaryItemCatalogSnapshot.Failed("ConsumableSO.All was unreadable."),
            $"{stored:D},bad-token");

        Assert.Equal(AutoItemsTemporaryItemPickerContentState.Empty, empty.ContentState);
        Assert.Equal("0 of 0 approved", empty.ApprovalStateLine);
        Assert.Equal("No discovered temporary items yet.", empty.ContentMessage);
        Assert.Equal(
            AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed,
            failed.ContentState);
        Assert.Equal("Approval count unavailable — discovery read failed", failed.ApprovalStateLine);
        Assert.Contains("ConsumableSO.All was unreadable", failed.ContentMessage);
        Assert.Collection(
            failed.UnresolvableEntries,
            entry => Assert.Equal(stored, entry.ItemId),
            entry => Assert.Equal("bad-token", entry.StoredToken));
    }

    [Fact]
    public void AmbiguousDiscoveredFamilyAndMissingIconFailTheWholeReadLoudly()
    {
        var ambiguous = Item(
            Guid.NewGuid(),
            KnownEntities.ConsumableFruitType.Uuid,
            "Ambiguous",
            visible: true,
            stock: 1,
            new Sprite());
        var secondFamily = new ConsumableTypeSO();
        secondFamily.SetGuid(KnownEntities.ConsumablePotionType.Uuid);
        ambiguous.consumableTypes.Add(secondFamily);

        var ambiguousSnapshot = AutoItemsTemporaryItemCatalog.Capture();

        Assert.False(ambiguousSnapshot.IsAvailable);
        Assert.Contains("ambiguous supported family", ambiguousSnapshot.FailureReason);

        ConsumableSO.All.Clear();
        Item(
            Guid.NewGuid(),
            KnownEntities.ConsumableThreadType.Uuid,
            "No Icon",
            visible: true,
            stock: 1,
            null!);

        var missingIcon = AutoItemsTemporaryItemCatalog.Capture();

        Assert.False(missingIcon.IsAvailable);
        Assert.Contains("returned no audited native icon", missingIcon.FailureReason);
    }

    private static AutoItemsTemporaryItemOption Option(Guid id, string name) =>
        new(
            id,
            AutoItemsConsumableFamily.Fruit,
            name,
            Stock: 1,
            new Sprite());

    private static ConsumableSO Item(
        Guid id,
        Guid familyId,
        string name,
        bool visible,
        int stock,
        Sprite icon)
    {
        var family = new ConsumableTypeSO();
        family.SetGuid(familyId);
        var item = new ConsumableSO
        {
            DisplayName = name,
            Icon = icon,
            visible = visible,
        };
        item.SetGuid(id);
        item.SetStock(stock, 0, 0);
        item.consumableTypes.Add(family);
        ConsumableSO.All.Add(item);
        return item;
    }
}
