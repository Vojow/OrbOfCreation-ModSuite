using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

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
        configuration.UsePotions;

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
            _ => false,
        };

    internal static bool Allows(
        AutoItemsConfiguration configuration,
        AutoItemsConsumableFamily family,
        Guid itemId,
        ISet<Guid>? temporaryAllowlist) =>
        family switch
        {
            AutoItemsConsumableFamily.Scroll => configuration.UseScrolls,
            AutoItemsConsumableFamily.Relic => configuration.UseRelics,
            AutoItemsConsumableFamily.Fruit =>
                configuration.UseFruits &&
                itemId != Guid.Empty &&
                temporaryAllowlist?.Contains(itemId) == true,
            AutoItemsConsumableFamily.Potion =>
                configuration.UsePotions &&
                itemId != Guid.Empty &&
                temporaryAllowlist?.Contains(itemId) == true,
            _ => false,
        };
}
