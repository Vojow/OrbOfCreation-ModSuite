using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoConceptService
{
    internal static IAutomataServiceDefinition<AutoConceptCycleState, AutoConceptCycleAction> Define(
        IAutoConceptCycleActionPort actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        var metadata = new AutomataServiceMetadata(
            AutoConceptServicePolicies.ServiceId,
            AutoConceptServicePolicies.DefaultWakePolicy,
            AutoConceptServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<AutoConceptCycleState, AutoConceptCycleAction>(
            in metadata,
            static () => new AutoConceptWorkerDefinition(),
            ShouldStart,
            actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        AutoConceptConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(AutoConceptConfigurationPolicy.FallbackInterval(config)));
}

internal static class AutoConceptServicePolicies
{
    public static ServiceId ServiceId => new("orbautomata.auto-concept");
    public static WakePolicy DefaultWakePolicy =>
        WakePolicy.AfterDecision(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
}
