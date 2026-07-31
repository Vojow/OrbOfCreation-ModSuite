using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbAutomata;

internal enum AutoItemsTemporaryItemPickerContentState
{
    Items,
    Empty,
    DiscoveryReadFailed,
}

internal readonly record struct AutoItemsTemporaryItemPickerItem(
    AutoItemsTemporaryItemOption Option,
    bool IsApproved);

internal readonly record struct AutoItemsTemporaryItemUnresolvableEntry(
    string StoredToken,
    Guid ItemId,
    bool IsUuid)
{
    internal string Heading => IsUuid
        ? "Unresolvable stored UUID"
        : "Invalid stored value";
}

/// <summary>
/// Native-free presentation state for the staged temporary-item allowlist. The model never edits a
/// configuration entry itself; callers stage its serialized result through the ordinary edit session.
/// </summary>
internal sealed class AutoItemsTemporaryItemPickerPresentation
{
    internal AutoItemsTemporaryItemPickerPresentation(
        AutoItemsTemporaryItemPickerContentState contentState,
        IReadOnlyList<AutoItemsTemporaryItemPickerItem> items,
        IReadOnlyList<AutoItemsTemporaryItemUnresolvableEntry> unresolvableEntries,
        string approvalStateLine,
        string contentMessage)
    {
        ContentState = contentState;
        Items = items;
        UnresolvableEntries = unresolvableEntries;
        ApprovalStateLine = approvalStateLine;
        ContentMessage = contentMessage;
    }

    internal AutoItemsTemporaryItemPickerContentState ContentState { get; }
    internal IReadOnlyList<AutoItemsTemporaryItemPickerItem> Items { get; }
    internal IReadOnlyList<AutoItemsTemporaryItemUnresolvableEntry> UnresolvableEntries { get; }
    internal string ApprovalStateLine { get; }
    internal string ContentMessage { get; }
}

internal static class AutoItemsTemporaryItemPickerModel
{
    internal static AutoItemsTemporaryItemPickerPresentation Compose(
        AutoItemsTemporaryItemCatalogSnapshot catalog,
        string serialized)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        var stored = StoredSelection.Parse(serialized);
        if (!catalog.IsAvailable)
        {
            return new AutoItemsTemporaryItemPickerPresentation(
                AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed,
                Array.Empty<AutoItemsTemporaryItemPickerItem>(),
                Unresolvable(stored, known: null),
                "Approval count unavailable — discovery read failed",
                "Discovery read failed: " + catalog.FailureReason);
        }

        var known = new HashSet<Guid>();
        var items = new AutoItemsTemporaryItemPickerItem[catalog.Options.Count];
        var approved = 0;
        for (var index = 0; index < catalog.Options.Count; index++)
        {
            var option = catalog.Options[index];
            known.Add(option.ItemId);
            var isApproved = stored.ItemIds.Contains(option.ItemId);
            if (isApproved) approved++;
            items[index] = new AutoItemsTemporaryItemPickerItem(option, isApproved);
        }

        var unresolvable = Unresolvable(stored, known);

        var empty = catalog.Options.Count == 0;
        return new AutoItemsTemporaryItemPickerPresentation(
            empty
                ? AutoItemsTemporaryItemPickerContentState.Empty
                : AutoItemsTemporaryItemPickerContentState.Items,
            items,
            unresolvable,
            $"{approved} of {catalog.Options.Count} approved",
            empty ? "No discovered temporary items yet." : string.Empty);
    }

    private static IReadOnlyList<AutoItemsTemporaryItemUnresolvableEntry> Unresolvable(
        StoredSelection stored,
        HashSet<Guid>? known)
    {
        var unresolvable = new List<AutoItemsTemporaryItemUnresolvableEntry>();
        foreach (var itemId in stored.ItemIds
                     .Where(itemId => known is null || !known.Contains(itemId))
                     .OrderBy(id => id))
        {
            unresolvable.Add(new AutoItemsTemporaryItemUnresolvableEntry(
                itemId.ToString("D"),
                itemId,
                IsUuid: true));
        }
        foreach (var invalid in stored.InvalidTokens.OrderBy(token => token, StringComparer.Ordinal))
        {
            unresolvable.Add(new AutoItemsTemporaryItemUnresolvableEntry(
                invalid,
                Guid.Empty,
                IsUuid: false));
        }
        return unresolvable;
    }

    internal static string Toggle(string serialized, Guid itemId)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("An exact item UUID is required.", nameof(itemId));
        var stored = StoredSelection.Parse(serialized);
        if (!stored.ItemIds.Add(itemId)) stored.ItemIds.Remove(itemId);
        return stored.Serialize();
    }

    internal static string Remove(
        string serialized,
        AutoItemsTemporaryItemUnresolvableEntry entry)
    {
        var stored = StoredSelection.Parse(serialized);
        if (entry.IsUuid)
            stored.ItemIds.Remove(entry.ItemId);
        else
            stored.InvalidTokens.Remove(entry.StoredToken);
        return stored.Serialize();
    }

    private sealed class StoredSelection
    {
        private StoredSelection(HashSet<Guid> itemIds, HashSet<string> invalidTokens)
        {
            ItemIds = itemIds;
            InvalidTokens = invalidTokens;
        }

        internal HashSet<Guid> ItemIds { get; }
        internal HashSet<string> InvalidTokens { get; }

        internal static StoredSelection Parse(string? serialized)
        {
            var itemIds = new HashSet<Guid>();
            var invalid = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in (serialized ?? string.Empty).Split(','))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                if (Guid.TryParse(token, out var itemId) && itemId != Guid.Empty)
                    itemIds.Add(itemId);
                else
                    invalid.Add(token);
            }
            return new StoredSelection(itemIds, invalid);
        }

        internal string Serialize()
        {
            var tokens = ItemIds
                .OrderBy(itemId => itemId)
                .Select(itemId => itemId.ToString("D"))
                .Concat(InvalidTokens.OrderBy(token => token, StringComparer.Ordinal));
            return string.Join(",", tokens);
        }
    }
}
