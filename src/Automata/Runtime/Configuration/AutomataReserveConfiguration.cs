namespace OrbAutomata;

internal sealed record AutomataReserveConfiguration
{
    internal string AbsoluteReserve { get; init; } = string.Empty;
    internal float RelativeReserveMultiplier { get; init; }
}
