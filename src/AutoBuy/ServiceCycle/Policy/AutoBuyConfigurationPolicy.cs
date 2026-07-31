using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// Pure predicates over the immutable configuration that gate whether the Auto Buy service
/// should run. These are the config-only rules — no native state and no worker state — shared by
/// the start decision and the action port.
/// </summary>
internal static class AutoBuyConfigurationPolicy
{
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
}
