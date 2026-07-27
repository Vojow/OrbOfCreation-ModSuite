namespace OrbAutomata;

internal sealed record AutoConceptConfiguration
{
    internal AutoConceptOperationMode Mode { get; init; }
    internal AutoConceptSlotManagementMode SlotManagement { get; init; }
    internal bool ShowToggleButton { get; init; }
    internal int TrainingPeriodSeconds { get; init; }
    internal int FallbackEvaluationIntervalSeconds { get; init; }
    internal int QuantityCap { get; init; }
    internal float RateReservePercent { get; init; }
    internal float MinimumResourcePercent { get; init; }
    internal float MinimumDrainRatio { get; init; }
    internal string AllowedUuids { get; init; } = string.Empty;
    internal string BlockedUuids { get; init; } = string.Empty;
}
