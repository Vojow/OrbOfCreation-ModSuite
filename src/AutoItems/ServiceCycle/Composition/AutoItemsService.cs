using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsService
{
    internal static IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> Define(
        IAutoItemsCycleActionPort actions,
        AutoItemsTemporaryActivationTracker temporaryActivations,
        AutoScribeIdentityProfile? autoScribeIdentityProfile = null)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (temporaryActivations is null)
            throw new ArgumentNullException(nameof(temporaryActivations));
        var metadata = new AutomataServiceMetadata(
            AutoItemsServicePolicies.ServiceId,
            AutoItemsServicePolicies.DefaultWakePolicy,
            AutoItemsServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<AutoItemsCycleState, AutoItemsCycleAction>(
            in metadata,
            () => new AutoItemsWorkerDefinition(
                temporaryActivations,
                autoScribeIdentityProfile),
            ShouldStart,
            actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        AutoItemsConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(AutoItemsConfigurationPolicy.EvaluationInterval(config)));
}

internal static class AutoItemsServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.auto-items");
    internal static WakePolicy DefaultWakePolicy =>
        WakePolicy.AfterDecision(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
}
