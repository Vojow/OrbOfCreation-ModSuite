using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal enum AutoAgromancyOperationMode
{
    Disabled = 0,
    Active = 1,
}

internal sealed record AutoAgromancyConfiguration
{
    internal AutoAgromancyOperationMode Mode { get; init; }

    internal MonotonicDuration EvaluationInterval { get; init; } =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
}

internal static class AutoAgromancyConfigurationPolicy
{
    private static readonly MonotonicDuration DefaultInterval =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));

    internal static bool IsOperational(in SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.AutoAgromancy.Mode == AutoAgromancyOperationMode.Active &&
        !configuration.Safety.EmergencyDisable;

    internal static MonotonicDuration EvaluationInterval(
        in SuiteRuntimeConfiguration configuration) =>
        configuration.AutoAgromancy.EvaluationInterval.Ticks > 0
            ? configuration.AutoAgromancy.EvaluationInterval
            : DefaultInterval;
}
