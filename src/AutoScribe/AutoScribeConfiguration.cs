using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal enum AutoScribeOperationMode
{
    Disabled = 0,
    Active = 1,
}

internal sealed record AutoScribeConfiguration
{
    internal AutoScribeOperationMode Mode { get; init; }
    internal string Roles { get; init; } = string.Empty;
    internal MonotonicDuration EvaluationInterval { get; init; }
}
