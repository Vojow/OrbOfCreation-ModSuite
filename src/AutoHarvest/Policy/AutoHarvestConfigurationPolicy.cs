using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class AutoHarvestConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        !configuration.Safety.EmergencyDisable &&
        configuration.AutoHarvest.Mode == AutoHarvestOperationMode.Active &&
        (configuration.AutoHarvest.CollectFruitTrees || configuration.AutoHarvest.CollectTreasureTrees);

    internal static bool IsSelected(
        SuiteRuntimeConfiguration configuration,
        AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => configuration.AutoHarvest.CollectFruitTrees,
        AutoHarvestPair.TreasureTree => configuration.AutoHarvest.CollectTreasureTrees,
        _ => false,
    };
}
