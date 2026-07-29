using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal enum AutoItemsTemporaryItemFilter
{
    All,
    Fruit,
    Potion,
    Owned,
    Selected,
}

internal enum AutoItemsTemporaryItemEditorMode
{
    Closed,
    Items,
    Raw,
}

internal sealed class AutoItemsTemporaryItemPickerState
{
    internal AutoItemsTemporaryItemEditorMode Mode { get; private set; }
    internal AutoItemsTemporaryItemFilter Filter { get; private set; }

    internal void ToggleItems() =>
        Mode = Mode == AutoItemsTemporaryItemEditorMode.Items
            ? AutoItemsTemporaryItemEditorMode.Closed
            : AutoItemsTemporaryItemEditorMode.Items;

    internal void ToggleRaw() =>
        Mode = Mode == AutoItemsTemporaryItemEditorMode.Raw
            ? AutoItemsTemporaryItemEditorMode.Closed
            : AutoItemsTemporaryItemEditorMode.Raw;

    internal void CycleFilter()
    {
        Filter = AutoItemsTemporaryItemFiltering.Next(Filter);
        Mode = AutoItemsTemporaryItemEditorMode.Items;
    }
}

internal static class AutoItemsTemporaryItemFiltering
{
    internal static AutoItemsTemporaryItemFilter Next(AutoItemsTemporaryItemFilter current) =>
        current switch
        {
            AutoItemsTemporaryItemFilter.All => AutoItemsTemporaryItemFilter.Fruit,
            AutoItemsTemporaryItemFilter.Fruit => AutoItemsTemporaryItemFilter.Potion,
            AutoItemsTemporaryItemFilter.Potion => AutoItemsTemporaryItemFilter.Owned,
            AutoItemsTemporaryItemFilter.Owned => AutoItemsTemporaryItemFilter.Selected,
            _ => AutoItemsTemporaryItemFilter.All,
        };

    internal static bool Matches(
        AutoItemsTemporaryItemOption option,
        AutoItemsTemporaryItemFilter filter,
        HashSet<Guid> selected) =>
        filter switch
        {
            AutoItemsTemporaryItemFilter.Fruit =>
                option.Family == AutoItemsConsumableFamily.Fruit,
            AutoItemsTemporaryItemFilter.Potion =>
                option.Family == AutoItemsConsumableFamily.Potion,
            AutoItemsTemporaryItemFilter.Owned => option.OwnedQuantity > 0,
            AutoItemsTemporaryItemFilter.Selected => selected.Contains(option.ItemId),
            _ => true,
        };

    internal static bool ShowsUnavailable(AutoItemsTemporaryItemFilter filter) =>
        filter is AutoItemsTemporaryItemFilter.All or AutoItemsTemporaryItemFilter.Selected;
}

internal static class AutoItemsTemporaryItemSelection
{
    internal static HashSet<Guid> Parse(string serialized) =>
        AutoItemsTemporaryItemPolicy.ParseAllowlist(serialized);

    internal static string Toggle(string serialized, Guid itemId)
    {
        if (itemId == Guid.Empty) return Serialize(Parse(serialized));
        var selected = Parse(serialized);
        if (!selected.Add(itemId)) selected.Remove(itemId);
        return Serialize(selected);
    }

    internal static string Serialize(IEnumerable<Guid> itemIds)
    {
        var ordered = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var itemId in itemIds)
        {
            if (itemId != Guid.Empty && seen.Add(itemId)) ordered.Add(itemId);
        }
        ordered.Sort();
        return string.Join(",", ordered.ConvertAll(itemId => itemId.ToString("D")));
    }
}
