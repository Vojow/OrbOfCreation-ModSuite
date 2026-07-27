using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Pure predicates over the immutable configuration that gate whether the Auto Buy service
/// should run and how often it wakes. These are the config-only rules — no native state, no
/// worker state — shared by the start decision, the action port, and the wake scheduler.
/// </summary>
internal static class AutoBuyConfigurationPolicy
{
    /// <summary>Matches the legacy Auto Buy default idle scan interval (unscaled seconds).</summary>
    internal const float DefaultEvaluationIntervalSeconds = 0.5f;

    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        !configuration.Safety.EmergencyDisable &&
        configuration.AutoBuy.Mode == AutoBuyOperationMode.Active &&
        (configuration.AutoBuy.IncludeStructures || configuration.AutoBuy.IncludeUpgrades);

    /// <summary>
    /// Whether the configuration still selects a candidate of this kind. The action port
    /// revalidates this at execution time so a config that dropped Structures/Upgrades between
    /// planning and execution cannot commit a purchase the operator no longer wants.
    /// </summary>
    internal static bool IsSelected(SuiteRuntimeConfiguration configuration, AutoBuyCandidateKind kind) =>
        kind switch
        {
            AutoBuyCandidateKind.Structure => configuration.AutoBuy.IncludeStructures,
            AutoBuyCandidateKind.Upgrade => configuration.AutoBuy.IncludeUpgrades,
            _ => false,
        };

    internal static MonotonicDuration EvaluationInterval(SuiteRuntimeConfiguration configuration)
    {
        var seconds = configuration.AutoBuy.EvaluationIntervalSeconds;
        if (!(seconds > 0f)) seconds = DefaultEvaluationIntervalSeconds;
        return MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }
}
