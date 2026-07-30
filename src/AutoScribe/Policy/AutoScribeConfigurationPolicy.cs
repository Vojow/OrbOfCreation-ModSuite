using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class AutoScribeConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoScribeActively &&
        configuration.CanStartAutoItemsActively &&
        configuration.AutoItems.UseScrolls;
}
