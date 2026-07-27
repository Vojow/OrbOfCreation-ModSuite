using System;
using System.Collections.Generic;
using BepInEx.Logging;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Builds the one Automata-owned ServiceCycle runtime and contains a startup failure
/// so that a single feature's misconfiguration cannot crash plugin bootstrap. On a
/// contained failure the whole host is unavailable, so every feature is asked to
/// surface its own faulted status.
/// </summary>
internal static class AutomataServiceCycleProductionComposition
{
    public static AutomataServiceCycleRuntime? TryCreate(
        SuiteRuntimeConfiguration configuration,
        AutomataServiceCycleHostDependencies hostDependencies,
        IReadOnlyList<IAutomataServiceCycleFeature> features,
        ManualLogSource log)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (hostDependencies is null) throw new ArgumentNullException(nameof(hostDependencies));
        if (features is null) throw new ArgumentNullException(nameof(features));
        if (log is null) throw new ArgumentNullException(nameof(log));
        try
        {
            var runtime = AutomataServiceCycleComposition.Create(
                configuration,
                hostDependencies,
                features,
                log);
            log.LogAutomataInfo("Automata ServiceCycle runtime registered.");
            return runtime;
        }
        catch (Exception exception) when (IsContainedStartupFailure(exception))
        {
            for (var index = 0; index < features.Count; index++)
                features[index].ObserveStartupFailure(configuration, exception);
            log.LogAutomataError(
                "Automata ServiceCycle host initialization failed and its features are disabled: " +
                exception.GetBaseException().Message);
            return null;
        }
    }

    internal static bool IsContainedStartupFailure(Exception exception) =>
        exception is not StackOverflowException and
        not OutOfMemoryException and
        not AccessViolationException;
}
