using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OrbChronicle;

internal enum ChronicleMilestoneState
{
    Pending = 0,
    Reached = 1,
    Preexisting = 2,
    Blocked = 3,
}

internal readonly struct ChronicleMilestoneDefinition
{
    internal ChronicleMilestoneDefinition(
        string id,
        string label,
        Guid targetId,
        string expectedNativeType,
        int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A milestone ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A milestone label is required.", nameof(label));
        if (string.IsNullOrWhiteSpace(expectedNativeType))
        {
            throw new ArgumentException(
                "An expected native type is required.",
                nameof(expectedNativeType));
        }
        if (displayOrder is < 0 or > 63)
            throw new ArgumentOutOfRangeException(nameof(displayOrder));

        Id = id;
        Label = label;
        TargetId = targetId;
        ExpectedNativeType = expectedNativeType;
        DisplayOrder = displayOrder;
    }

    internal string Id { get; }
    internal string Label { get; }
    internal Guid TargetId { get; }
    internal string ExpectedNativeType { get; }
    internal int DisplayOrder { get; }
    internal ulong Mask => 1UL << DisplayOrder;
}

internal static class ChronicleMilestones
{
    internal const string SchemaId = "orb-major-v1";
    internal const string ClockId = "gameplay-active-monotonic-v1";
    internal const int MagicIndex = 0;
    internal const int WorldRestoredIndex = 7;

    private static readonly ReadOnlyCollection<ChronicleMilestoneDefinition> Definitions =
        Array.AsReadOnly(new[]
        {
            new ChronicleMilestoneDefinition(
                "magic",
                "Magic",
                Guid.Empty,
                "SuiteRunStart",
                0),
            new ChronicleMilestoneDefinition(
                "scholar",
                "Scholar",
                Guid.Parse("9ea5d6e1-739b-4dec-832b-f5f3ba3ad2ca"),
                "ViewSO",
                1),
            new ChronicleMilestoneDefinition(
                "world",
                "World",
                Guid.Parse("efd92b91-780a-4e47-b65b-4056a9d81af5"),
                "ViewSO",
                2),
            new ChronicleMilestoneDefinition(
                "workshop",
                "Workshop",
                Guid.Parse("c662d72a-2211-4cd6-b9d2-104071a5e6e9"),
                "ViewSO",
                3),
            new ChronicleMilestoneDefinition(
                "alchemy",
                "Alchemy",
                Guid.Parse("3ae45ec0-4449-4903-b3d0-b5182e03dca3"),
                "ViewSO",
                4),
            new ChronicleMilestoneDefinition(
                "rituals",
                "Rituals",
                Guid.Parse("9cfb2e96-ee2f-4001-8397-7c1680ab9573"),
                "ViewSO",
                5),
            new ChronicleMilestoneDefinition(
                "restoration-unlocked",
                "Restoration unlocked",
                Guid.Parse("14b35ebc-f284-4d53-bd3f-f57a885cf2b1"),
                "UpgradeSO",
                6),
            new ChronicleMilestoneDefinition(
                "world-restored",
                "World restored",
                Guid.Parse("dcabdc8a-3e8f-4991-88f2-9374279b694b"),
                "BoolVariable",
                7),
        });

    internal static IReadOnlyList<ChronicleMilestoneDefinition> All => Definitions;
    internal static int Count => Definitions.Count;
    internal static ulong NativeMask { get; } = BuildNativeMask();

    internal static ChronicleMilestoneDefinition At(int index) => Definitions[index];

    private static ulong BuildNativeMask()
    {
        ulong mask = 0;
        for (var index = 0; index < Definitions.Count; index++)
        {
            var definition = Definitions[index];
            if (definition.DisplayOrder != index)
            {
                throw new InvalidOperationException(
                    "Chronicle milestone display order must be contiguous and index-aligned.");
            }
            if (index != MagicIndex) mask |= definition.Mask;
        }
        return mask;
    }
}
