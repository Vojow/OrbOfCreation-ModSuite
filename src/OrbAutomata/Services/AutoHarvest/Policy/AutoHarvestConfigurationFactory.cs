using System;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal static class AutoHarvestConfigurationFactory
{
    internal static AutomataConfiguration Create(
        bool masterEnabled,
        bool emergencyDisabled,
        bool activeMode,
        bool fruitSelected,
        bool treasureSelected,
        MonotonicDuration evaluationInterval)
    {
        if (evaluationInterval.Ticks <= 0)
            throw new ArgumentOutOfRangeException(nameof(evaluationInterval));

        return new AutomataConfiguration
        {
            General = new AutomataGeneralConfiguration { Enabled = masterEnabled },
            Safety = new AutomataSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            AutoHarvest = new AutoHarvestConfiguration
            {
                Mode = activeMode
                    ? AutoHarvestOperationMode.Active
                    : AutoHarvestOperationMode.Disabled,
                CollectFruitTrees = fruitSelected,
                CollectTreasureTrees = treasureSelected,
                EvaluationInterval = evaluationInterval,
            },
        };
    }
}
