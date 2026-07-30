using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal enum AutoItemsOperationMode
{
    Disabled = 0,
    Active = 1,
}

internal sealed record AutoItemsConfiguration
{
    internal AutoItemsOperationMode Mode { get; init; }
    internal bool UseScrolls { get; init; } = true;
    internal bool UseRelics { get; init; } = true;
    internal bool UseFruits { get; init; }
    internal bool UsePotions { get; init; }
    internal bool UseThreads { get; init; }
    internal string TemporaryItemAllowlist { get; init; } = string.Empty;
    internal MonotonicDuration EvaluationInterval { get; init; }
}
