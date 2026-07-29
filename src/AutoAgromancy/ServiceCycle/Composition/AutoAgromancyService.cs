using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoAgromancyService
{
    internal static IAutomataServiceDefinition<
        AutoAgromancyCycleState,
        AutoAgromancyCycleAction> Define(IAutoAgromancyCycleActionPort actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        var metadata = new AutomataServiceMetadata(
            AutoAgromancyServicePolicies.ServiceId,
            AutoAgromancyServicePolicies.DefaultWakePolicy,
            AutoAgromancyServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<AutoAgromancyCycleState, AutoAgromancyCycleAction>(
            in metadata,
            static () => new AutoAgromancyWorkerDefinition(),
            ShouldStart,
            actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration configuration,
        in ServiceCycleStartContext context) =>
        AutoAgromancyConfigurationPolicy.IsOperational(in configuration)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(
                    AutoAgromancyConfigurationPolicy.EvaluationInterval(
                        in configuration)));
}

internal static class AutoAgromancyServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.auto-agromancy");
    internal static WakePolicy DefaultWakePolicy =>
        WakePolicy.AfterDecision(
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
}
