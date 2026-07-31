using System;
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
        AutoItemsTemporaryItemAllowlist.HasAnyValidEntry(
            configuration.TemporaryItemAllowlist);

    internal static bool Allows(
        AutoItemsConfiguration configuration,
        AutoItemsConsumableFamily family,
        Guid itemId) =>
        family switch
        {
            AutoItemsConsumableFamily.Scroll => configuration.UseScrolls,
            AutoItemsConsumableFamily.Relic => configuration.UseRelics,
            AutoItemsConsumableFamily.Fruit or
            AutoItemsConsumableFamily.Potion or
            AutoItemsConsumableFamily.Thread =>
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
            AutoItemsConsumableFamily.Fruit or
            AutoItemsConsumableFamily.Potion or
            AutoItemsConsumableFamily.Thread =>
                AutoItemsTemporaryItemAllowlist.Contains(temporaryAllowlist, itemId),
            _ => false,
        };
}
