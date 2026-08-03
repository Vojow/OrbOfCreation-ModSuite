using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct GenericDiscoveryComponent
{
    internal GenericDiscoveryComponent(Guid componentId, int count)
    {
        if (componentId == Guid.Empty)
            throw new ArgumentException("A discovery component identity is required.", nameof(componentId));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        ComponentId = componentId;
        Count = count;
    }

    internal Guid ComponentId { get; }
    internal int Count { get; }
}

/// <summary>
/// Stable component-first intent. The target is derived from the admitted immutable world; the
/// action retains the submitted composition so the native recipe can be revalidated before payment.
/// </summary>
internal readonly struct GenericDiscoveryAction
{
    internal GenericDiscoveryAction(
        Guid targetId,
        string expectedNativeType,
        string surface,
        GenericDiscoveryComponent[] components,
        long lifecycleEpoch)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A discoverable identity is required.", nameof(targetId));
        if (string.IsNullOrWhiteSpace(expectedNativeType))
            throw new ArgumentException("An exact native discoverable type is required.", nameof(expectedNativeType));
        if (string.IsNullOrWhiteSpace(surface))
            throw new ArgumentException("A discovery surface is required.", nameof(surface));
        if (components is null || components.Length == 0)
            throw new ArgumentException("At least one discovery component is required.", nameof(components));
        TargetId = targetId;
        ExpectedNativeType = expectedNativeType;
        Surface = surface;
        Components = PublicationTable<GenericDiscoveryComponent>.Create(components);
        LifecycleEpoch = lifecycleEpoch;
    }

    internal Guid TargetId { get; }
    internal string ExpectedNativeType { get; }
    internal string Surface { get; }
    internal PublicationTable<GenericDiscoveryComponent> Components { get; }
    internal long LifecycleEpoch { get; }
}

internal static class GenericDiscoverySurfaces
{
    internal static bool TryResolve(
        string surface,
        out string nativeType,
        out string category)
    {
        nativeType = surface switch
        {
            "glyphcraft" => "GlyphSO",
            "devote" => "RitualSO",
            "runecraft" => "TimeRuneSO",
            "alchemy" => "AlchemyRecipeSO",
            "concepts" => "AlchemyRecipeSO",
            "artifacts" => "EquipmentSO",
            _ => string.Empty,
        };
        category = surface switch
        {
            "glyphcraft" => "glyphs",
            "devote" => "rituals",
            "runecraft" => "time-runes",
            "alchemy" => "alchemy-recipes",
            "concepts" => "alchemy-recipes",
            "artifacts" => "equipment",
            _ => string.Empty,
        };
        return nativeType.Length > 0;
    }

    internal static bool Owns(string surface, string nativeType) =>
        TryResolve(surface, out var owner, out _) && owner == nativeType;
}
