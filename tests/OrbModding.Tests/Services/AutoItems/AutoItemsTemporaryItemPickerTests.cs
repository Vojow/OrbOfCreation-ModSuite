using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems;

public sealed class AutoItemsTemporaryItemPickerTests : IDisposable
{
    public AutoItemsTemporaryItemPickerTests() => ConsumableSO.All.Clear();

    public void Dispose() => ConsumableSO.All.Clear();

    [Fact]
    public void CatalogShowsOnlyVisibleTemporaryFamiliesWithNamesAndStock()
    {
        var fruit = Item(
            KnownEntities.ConsumableFruitType.Uuid,
            "Star Apple",
            visible: true,
            quantity: 3);
        Item(
            KnownEntities.ConsumablePotionType.Uuid,
            "Hidden Tonic",
            visible: false,
            quantity: 2);
        Item(
            KnownEntities.ConsumableRelicType.Uuid,
            "Ancient Relic",
            visible: true,
            quantity: 1);

        var snapshot = AutoItemsTemporaryItemCatalog.Capture();

        Assert.True(snapshot.IsAvailable, snapshot.UnavailableReason);
        var option = Assert.Single(snapshot.Options);
        Assert.Equal(fruit.GetGuid(), option.ItemId);
        Assert.Equal(AutoItemsConsumableFamily.Fruit, option.Family);
        Assert.Equal("Star Apple", option.DisplayName);
        Assert.Equal(3, option.OwnedQuantity);
        Assert.Equal(90d, option.DurationSeconds);
        Assert.Equal("12", option.ToxicityCost);
    }

    [Fact]
    public void SelectionToggleStoresSortedStableUuids()
    {
        var later = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var selected = AutoItemsTemporaryItemSelection.Toggle(string.Empty, later);
        selected = AutoItemsTemporaryItemSelection.Toggle(selected, first);

        Assert.Equal($"{first:D},{later:D}", selected);
        Assert.Equal(first.ToString("D"), AutoItemsTemporaryItemSelection.Toggle(selected, later));
    }

    [Fact]
    public void SelectionNormalizationDropsInvalidAndDuplicateTokens()
    {
        var item = Guid.Parse("10000000-0000-0000-0000-000000000001");

        var normalized = AutoItemsTemporaryItemSelection.Serialize(
            AutoItemsTemporaryItemSelection.Parse($"invalid, {item:D}, {item:D}"));

        Assert.Equal(item.ToString("D"), normalized);
    }

    [Fact]
    public void FiltersCoverFamiliesOwnedAndSelectedItems()
    {
        var selected = new System.Collections.Generic.HashSet<Guid>();
        var fruit = new AutoItemsTemporaryItemOption(
            Guid.NewGuid(),
            AutoItemsConsumableFamily.Fruit,
            "Fruit",
            0,
            10d,
            "1");
        var potion = new AutoItemsTemporaryItemOption(
            Guid.NewGuid(),
            AutoItemsConsumableFamily.Potion,
            "Potion",
            2,
            20d,
            "2");
        selected.Add(fruit.ItemId);

        Assert.True(AutoItemsTemporaryItemFiltering.Matches(
            fruit,
            AutoItemsTemporaryItemFilter.Fruit,
            selected));
        Assert.False(AutoItemsTemporaryItemFiltering.Matches(
            fruit,
            AutoItemsTemporaryItemFilter.Owned,
            selected));
        Assert.True(AutoItemsTemporaryItemFiltering.Matches(
            fruit,
            AutoItemsTemporaryItemFilter.Selected,
            selected));
        Assert.True(AutoItemsTemporaryItemFiltering.Matches(
            potion,
            AutoItemsTemporaryItemFilter.Potion,
            selected));
        Assert.True(AutoItemsTemporaryItemFiltering.Matches(
            potion,
            AutoItemsTemporaryItemFilter.Owned,
            selected));
    }

    [Fact]
    public void PickerStateKeepsEditorModesMutuallyExclusive()
    {
        var state = new AutoItemsTemporaryItemPickerState();

        state.ToggleItems();
        Assert.Equal(AutoItemsTemporaryItemEditorMode.Items, state.Mode);

        state.ToggleRaw();
        Assert.Equal(AutoItemsTemporaryItemEditorMode.Raw, state.Mode);

        state.ToggleRaw();
        Assert.Equal(AutoItemsTemporaryItemEditorMode.Closed, state.Mode);

        state.CycleFilter();
        Assert.Equal(AutoItemsTemporaryItemFilter.Fruit, state.Filter);
        Assert.Equal(AutoItemsTemporaryItemEditorMode.Items, state.Mode);
    }

    [Fact]
    public void CatalogFailsClosedForAmbiguousTemporaryFamily()
    {
        var item = Item(
            KnownEntities.ConsumableFruitType.Uuid,
            "Ambiguous",
            visible: true,
            quantity: 1);
        var potion = new ConsumableTypeSO();
        potion.SetGuid(KnownEntities.ConsumablePotionType.Uuid);
        item.consumableTypes.Add(potion);

        var snapshot = AutoItemsTemporaryItemCatalog.Capture();

        Assert.False(snapshot.IsAvailable);
        Assert.Contains("more than one supported", snapshot.UnavailableReason);
    }

    [Fact]
    public void CatalogFailsClosedForDuplicateStableIdentity()
    {
        var first = Item(
            KnownEntities.ConsumableFruitType.Uuid,
            "First",
            visible: true,
            quantity: 1);
        var duplicate = Item(
            KnownEntities.ConsumablePotionType.Uuid,
            "Duplicate",
            visible: true,
            quantity: 1);
        duplicate.SetGuid(first.GetGuid());

        var snapshot = AutoItemsTemporaryItemCatalog.Capture();

        Assert.False(snapshot.IsAvailable);
        Assert.Contains("duplicate identity", snapshot.UnavailableReason);
    }

    private static ConsumableSO Item(
        Guid familyId,
        string displayName,
        bool visible,
        int quantity)
    {
        var family = new ConsumableTypeSO();
        family.SetGuid(familyId);
        var item = new ConsumableSO
        {
            displayName = displayName,
            visible = visible,
        };
        item.SetGuid(Guid.NewGuid());
        item.SetStock(quantity, 0, 0);
        item.durationBase = 90d;
        item.consumableTypes.Add(family);
        var toxicity = new ResourceSO
        {
            uuid = KnownEntities.PotionToxicity.Uuid.ToString("D"),
        };
        item.consumeCost.costs.Add(new ResourceTuple(toxicity, new BigDouble(12d)));
        ConsumableSO.All.Add(item);
        return item;
    }
}
