using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class AutoConceptConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled && configuration.CanStartAutoConceptActively;

    internal static int TrainingPeriodSeconds(SuiteRuntimeConfiguration configuration) =>
        Math.Clamp(configuration.AutoConcept.TrainingPeriodSeconds, 10, 3600);
}
