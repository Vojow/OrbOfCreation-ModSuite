namespace OrbModding.Common.Runtime.Configuration;

/// <summary>How much a service says about the decisions it reached.</summary>
internal enum SuiteDecisionLogLevel
{
    Off,
    Summary,
    Verbose,
}

internal sealed record SuiteDiagnosticsConfiguration
{
    internal bool EnableOperationalLogging { get; init; }
    internal int MaxLoggedRejections { get; init; }
    internal SuiteDecisionLogLevel DecisionLogLevel { get; init; }

    internal bool IsOperationalLoggingEnabled =>
        EnableOperationalLogging && DecisionLogLevel != SuiteDecisionLogLevel.Off;
}
