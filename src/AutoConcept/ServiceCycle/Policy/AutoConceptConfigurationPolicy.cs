using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal static class AutoConceptConfigurationPolicy
{
    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled && configuration.CanStartAutoConceptActively;

    internal static MonotonicDuration FallbackInterval(SuiteRuntimeConfiguration configuration) =>
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(Math.Clamp(
            configuration.AutoConcept.FallbackEvaluationIntervalSeconds, 10, 1800)));

    internal static int TrainingPeriodSeconds(SuiteRuntimeConfiguration configuration) =>
        Math.Clamp(configuration.AutoConcept.TrainingPeriodSeconds, 10, 3600);

    internal static HashSet<string> ParseUuids(string value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            if (Guid.TryParse(token.Trim(), out var parsed)) result.Add(parsed.ToString());
        return result;
    }
}
