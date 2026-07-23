namespace OrbAutomata;

internal sealed record AutomataGeneralConfiguration
{
    internal bool Enabled { get; init; }
}

internal sealed record AutomataSafetyConfiguration
{
    internal bool EmergencyDisable { get; init; }
}

internal sealed record AutomataPerformanceConfiguration
{
    internal float CpuBudgetMilliseconds { get; init; }
}

internal sealed record AutomataDiagnosticsConfiguration
{
    internal bool EnableOperationalLogging { get; init; }
    internal int MaxLoggedRejections { get; init; }
    internal AutomataDecisionLogLevel DecisionLogLevel { get; init; }

    internal bool IsOperationalLoggingEnabled =>
        EnableOperationalLogging && DecisionLogLevel != AutomataDecisionLogLevel.Off;
}

internal sealed record AutomataReplayConfiguration
{
    internal bool EnableAutoHarvestCapture { get; init; }
}

internal sealed record AutomataReserveConfiguration
{
    internal string AbsoluteReserve { get; init; } = string.Empty;
    internal float RelativeReserveMultiplier { get; init; }
}
