namespace OrbAutomata;

internal sealed record AutoBuyConfiguration
{
    internal AutoBuyOperationMode Mode { get; init; }
    internal AutoBuyAffordabilityMode StructureAffordability { get; init; }
    internal AutoBuyAffordabilityMode UpgradeAffordability { get; init; }
    internal bool IncludeStructures { get; init; }
    internal bool IncludeUpgrades { get; init; }
    internal bool AutoLevelSpells { get; init; }
    internal AutoBuyPurchaseGroupingMode PurchaseGrouping { get; init; }
    internal float EvaluationIntervalSeconds { get; init; }
    internal int LeaveQueueSlots { get; init; }
    internal AutoBuyBatchSizingMode BatchSizing { get; init; }
    internal int MaxPurchasesPerBatch { get; init; }
    internal int FixedGroupSize { get; init; }
    internal bool PrioritizeCostAndQualityStructures { get; init; }
    internal string AllowedUuids { get; init; } = string.Empty;
    internal string BlockedUuids { get; init; } = string.Empty;
}
