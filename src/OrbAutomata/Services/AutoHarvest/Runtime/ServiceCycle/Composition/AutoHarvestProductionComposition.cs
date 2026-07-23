using System;
using BepInEx.Logging;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal static class AutoHarvestProductionComposition
{
    public static AutoHarvestServiceCycleRuntime? TryCreate(
        AutomataConfiguration configuration,
        AutoHarvestServiceCycleDependencies dependencies,
        ManualLogSource log)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        if (log is null) throw new ArgumentNullException(nameof(log));
        try
        {
            var runtime = AutoHarvestServiceCycleFactory.Create(configuration, dependencies, log);
            log.LogAutomataInfo("Auto Harvest ServiceCycle runtime registered.");
            return runtime;
        }
        catch (Exception exception) when (IsContainedStartupFailure(exception))
        {
            if (configuration.General.Enabled &&
                configuration.AutoHarvest.Mode == AutoHarvestOperationMode.Active &&
                (configuration.AutoHarvest.CollectFruitTrees || configuration.AutoHarvest.CollectTreasureTrees))
            {
                dependencies.FeatureStatus?.Observe(
                    true,
                    FeatureStatusState.Faulted,
                    FeatureStatusReasonCode.RuntimeFailure,
                    "Auto Harvest could not initialize its ServiceCycle runtime.");
            }
            log.LogAutomataError(
                "Auto Harvest initialization failed and Auto Harvest alone is disabled: " +
                exception.GetBaseException().Message);
            return null;
        }
    }

    internal static bool IsContainedStartupFailure(Exception exception) =>
        exception is not StackOverflowException and
        not OutOfMemoryException and
        not AccessViolationException;
}
