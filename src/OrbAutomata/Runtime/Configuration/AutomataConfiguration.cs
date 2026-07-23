namespace OrbAutomata;

internal sealed record AutomataConfiguration
{
    internal AutomataGeneralConfiguration General { get; init; } = new();
    internal AutoBuyConfiguration AutoBuy { get; init; } = new();
    internal AutoCastConfiguration AutoCast { get; init; } = new();
    internal AutoConceptConfiguration AutoConcept { get; init; } = new();
    internal AutoHarvestConfiguration AutoHarvest { get; init; } = new();
    internal AutomataSafetyConfiguration Safety { get; init; } = new();
    internal AutomataPerformanceConfiguration Performance { get; init; } = new();
    internal AutomataDiagnosticsConfiguration Diagnostics { get; init; } = new();
    internal AutomataReplayConfiguration Replay { get; init; } = new();
    internal AutomataReserveConfiguration Reserves { get; init; } = new();

    internal bool CanStartAutoBuyActively =>
        AutoBuy.Mode == AutoBuyOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoCastActively =>
        AutoCast.Mode == AutoCastOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoConceptActively =>
        AutoConcept.Mode == AutoConceptOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoHarvestActively =>
        AutoHarvest.Mode == AutoHarvestOperationMode.Active && !Safety.EmergencyDisable;
}
