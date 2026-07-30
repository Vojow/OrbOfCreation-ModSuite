using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoItemsActively &&
        HasEnabledFamily(configuration.AutoItems);

    internal static bool HasEnabledFamily(AutoItemsConfiguration configuration) =>
        configuration.UseScrolls ||
        configuration.UseRelics ||
        configuration.UseFruits ||
        configuration.UsePotions ||
        configuration.UseThreads;

    internal static MonotonicDuration EvaluationInterval(
        SuiteRuntimeConfiguration configuration)
    {
        var configured = configuration.AutoItems.EvaluationInterval;
        return configured.Ticks > 0
            ? configured
            : MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    }

    internal static bool Allows(
        AutoItemsConfiguration configuration,
        AutoItemsConsumableFamily family,
        Guid itemId) =>
        family switch
        {
            AutoItemsConsumableFamily.Scroll => configuration.UseScrolls,
            AutoItemsConsumableFamily.Relic => configuration.UseRelics,
            AutoItemsConsumableFamily.Fruit =>
                configuration.UseFruits &&
                AutoItemsTemporaryItemAllowlist.Contains(
                    configuration.TemporaryItemAllowlist,
                    itemId),
            AutoItemsConsumableFamily.Potion =>
                configuration.UsePotions &&
                AutoItemsTemporaryItemAllowlist.Contains(
                    configuration.TemporaryItemAllowlist,
                    itemId),
            AutoItemsConsumableFamily.Thread =>
                configuration.UseThreads &&
                AutoItemsTemporaryItemAllowlist.Contains(
                    configuration.TemporaryItemAllowlist,
                    itemId),
            _ => false,
        };

    internal static bool Allows(
        AutoItemsConfiguration configuration,
        AutoItemsConsumableFamily family,
        Guid itemId,
        PublicationTable<Guid>? temporaryAllowlist) =>
        family switch
        {
            AutoItemsConsumableFamily.Scroll => configuration.UseScrolls,
            AutoItemsConsumableFamily.Relic => configuration.UseRelics,
            AutoItemsConsumableFamily.Fruit =>
                configuration.UseFruits &&
                itemId != Guid.Empty &&
                AutoItemsTemporaryItemAllowlist.Contains(temporaryAllowlist, itemId),
            AutoItemsConsumableFamily.Potion =>
                configuration.UsePotions &&
                itemId != Guid.Empty &&
                AutoItemsTemporaryItemAllowlist.Contains(temporaryAllowlist, itemId),
            AutoItemsConsumableFamily.Thread =>
                configuration.UseThreads &&
                itemId != Guid.Empty &&
                AutoItemsTemporaryItemAllowlist.Contains(temporaryAllowlist, itemId),
            _ => false,
        };
}
