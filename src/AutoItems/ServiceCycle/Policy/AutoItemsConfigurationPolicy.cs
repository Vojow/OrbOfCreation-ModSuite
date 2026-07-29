using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class AutoItemsConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoItemsActively &&
        (configuration.AutoItems.UseScrolls ||
         configuration.AutoItems.UseRelics ||
         configuration.AutoItems.UseFruits ||
         configuration.AutoItems.UsePotions);

    internal static MonotonicDuration EvaluationInterval(
        SuiteRuntimeConfiguration configuration)
    {
        var configured = configuration.AutoItems.EvaluationInterval;
        return configured.Ticks > 0
            ? configured
            : MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    }
}
