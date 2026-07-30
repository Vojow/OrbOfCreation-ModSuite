using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class AutoItemsConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoItemsActively &&
        HasEnabledFamily(configuration.AutoItems);

    internal static bool HasEnabledFamily(AutoItemsConfiguration configuration) =>
        configuration.UseScrolls || configuration.UseRelics;

    internal static bool Allows(
        AutoItemsConfiguration configuration,
        AutoItemsConsumableFamily family) =>
        family switch
        {
            AutoItemsConsumableFamily.Scroll => configuration.UseScrolls,
            AutoItemsConsumableFamily.Relic => configuration.UseRelics,
            _ => false,
        };
}
