using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutoItemsTemporaryItemOption
{
    internal AutoItemsTemporaryItemOption(
        Guid itemId,
        AutoItemsConsumableFamily family,
        string displayName,
        int ownedQuantity,
        double durationSeconds,
        string toxicityCost)
    {
        ItemId = itemId;
        Family = family;
        DisplayName = displayName ?? string.Empty;
        OwnedQuantity = ownedQuantity;
        DurationSeconds = durationSeconds;
        ToxicityCost = toxicityCost ?? string.Empty;
    }

    internal Guid ItemId { get; }
    internal AutoItemsConsumableFamily Family { get; }
    internal string DisplayName { get; }
    internal int OwnedQuantity { get; }
    internal double DurationSeconds { get; }
    internal string ToxicityCost { get; }
}

internal sealed class AutoItemsTemporaryItemCatalogSnapshot
{
    private AutoItemsTemporaryItemCatalogSnapshot(
        IReadOnlyList<AutoItemsTemporaryItemOption> options,
        string unavailableReason)
    {
        Options = options;
        UnavailableReason = unavailableReason;
    }

    internal IReadOnlyList<AutoItemsTemporaryItemOption> Options { get; }
    internal string UnavailableReason { get; }
    internal bool IsAvailable => UnavailableReason.Length == 0;

    internal static AutoItemsTemporaryItemCatalogSnapshot Available(
        IReadOnlyList<AutoItemsTemporaryItemOption> options) =>
        new(options, string.Empty);

    internal static AutoItemsTemporaryItemCatalogSnapshot Unavailable(string reason) =>
        new(Array.Empty<AutoItemsTemporaryItemOption>(), reason);
}

/// <summary>
/// Captures a display-only Fruit/Potion catalog on the Unity main thread. Native objects never
/// leave this call; staged configuration continues to store stable UUIDs only.
/// </summary>
internal static class AutoItemsTemporaryItemCatalog
{
    internal static AutoItemsTemporaryItemCatalogSnapshot Capture()
    {
        try
        {
            if (!AutoItemsTemporaryItemCatalogBindings.TryCreate(
                    out var bindings,
                    out var unavailableReason))
            {
                return AutoItemsTemporaryItemCatalogSnapshot.Unavailable(unavailableReason);
            }
            var native = bindings!;

            var options = new List<AutoItemsTemporaryItemOption>();
            var seen = new HashSet<Guid>();
            foreach (var entry in native.Entries)
            {
                if (entry is null || entry.GetType() != native.ConsumableType)
                    return AutoItemsTemporaryItemCatalogSnapshot.Unavailable(
                        "The native consumable catalog changed element type.");
                if (!Invoke<bool>(native.IsVisible, entry)) continue;
                if (!TryReadFamily(entry, native, out var family)) continue;

                var itemId = Invoke<Guid>(native.ItemGuid, entry);
                if (itemId == Guid.Empty || !seen.Add(itemId))
                    return AutoItemsTemporaryItemCatalogSnapshot.Unavailable(
                        "The native temporary-item catalog contains an invalid or duplicate identity.");
                var displayName = Invoke<string>(native.GetName, entry);
                var quantity = Invoke<int>(native.GetQuantity, entry);
                var duration = native.DurationBase.GetValue(entry) is double value
                    ? value
                    : throw new InvalidOperationException(
                        "A consumable duration did not contain a Double.");
                options.Add(new AutoItemsTemporaryItemOption(
                    itemId,
                    family,
                    string.IsNullOrWhiteSpace(displayName)
                        ? $"Unnamed {family}"
                        : displayName.Trim(),
                    Math.Max(0, quantity),
                    duration,
                    ReadToxicityCost(entry, native)));
            }

            options.Sort(Compare);
            return AutoItemsTemporaryItemCatalogSnapshot.Available(options);
        }
        catch (Exception ex) when (AutoItemsReflectionAccess.IsExpectedFailure(ex))
        {
            return AutoItemsTemporaryItemCatalogSnapshot.Unavailable(
                "The native temporary-item catalog could not be read: " +
                ex.GetBaseException().Message);
        }
    }

    private static bool TryReadFamily(
        object item,
        AutoItemsTemporaryItemCatalogBindings bindings,
        out AutoItemsConsumableFamily family)
    {
        family = AutoItemsConsumableFamily.Unknown;
        if (bindings.Families.GetValue(item) is not IEnumerable entries)
            throw new InvalidOperationException("A consumable family list was unavailable.");
        var supported = 0;
        foreach (var entry in entries)
        {
            if (entry is null || entry.GetType() != bindings.FamilyType)
                throw new InvalidOperationException("A consumable family entry changed type.");
            var candidate = AutoItemsConsumableFamilies.FromTypeId(
                Invoke<Guid>(bindings.FamilyGuid, entry));
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            family = candidate;
            supported++;
        }
        if (supported > 1)
            throw new InvalidOperationException(
                "A consumable belongs to more than one supported Auto Items family.");
        return supported == 1 &&
               family is AutoItemsConsumableFamily.Fruit or AutoItemsConsumableFamily.Potion;
    }

    private static string ReadToxicityCost(
        object item,
        AutoItemsTemporaryItemCatalogBindings bindings)
    {
        var costList = bindings.ConsumeCost.GetValue(item) ??
            throw new InvalidOperationException("A consumable cost list was unavailable.");
        if (bindings.Costs.GetValue(costList) is not IEnumerable entries)
            throw new InvalidOperationException("A consumable cost vector was unavailable.");
        string? toxicityCost = null;
        foreach (var entry in entries)
        {
            if (entry is null || entry.GetType() != bindings.CostEntryType)
                throw new InvalidOperationException("A consumable cost entry changed type.");
            var resourceValue = bindings.Resource.GetValue(entry) ??
                throw new InvalidOperationException("A consumable cost resource was unavailable.");
            if (resourceValue.GetType() != bindings.ResourceType)
                throw new InvalidOperationException("A consumable cost resource changed type.");
            if (Invoke<Guid>(bindings.ResourceGuid, resourceValue) !=
                KnownEntities.PotionToxicity.Uuid)
            {
                continue;
            }
            if (toxicityCost is not null)
                throw new InvalidOperationException(
                    "A consumable contained more than one toxicity cost.");
            toxicityCost = FormatNativeNumber(bindings.ValueBig.GetValue(entry));
        }
        return toxicityCost ?? string.Empty;
    }

    private static string FormatNativeNumber(object? value)
    {
        if (value is null)
            throw new InvalidOperationException("A toxicity cost value was unavailable.");
        var formatted = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString();
        return string.IsNullOrWhiteSpace(formatted)
            ? throw new InvalidOperationException("A toxicity cost value could not be formatted.")
            : formatted;
    }

    private static int Compare(
        AutoItemsTemporaryItemOption left,
        AutoItemsTemporaryItemOption right)
    {
        var family = left.Family.CompareTo(right.Family);
        if (family != 0) return family;
        var name = string.Compare(
            left.DisplayName,
            right.DisplayName,
            StringComparison.OrdinalIgnoreCase);
        return name != 0 ? name : left.ItemId.CompareTo(right.ItemId);
    }

    private static T Invoke<T>(MethodInfo method, object target) =>
        method.Invoke(target, Array.Empty<object>()) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} did not return {typeof(T).Name}.");
}
