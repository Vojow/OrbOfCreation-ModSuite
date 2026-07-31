using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsService
{
    internal static IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> Define(
        IAutoItemsCycleActionPort actions,
        ConsumableMutationPublicationGapCoordinator publicationGap)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (publicationGap is null) throw new ArgumentNullException(nameof(publicationGap));
        var metadata = new AutomataServiceMetadata(
            AutoItemsServicePolicies.ServiceId,
            AutoItemsServicePolicies.DefaultWakePolicy,
            AutoItemsServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<AutoItemsCycleState, AutoItemsCycleAction>(
            in metadata,
            () => new AutoItemsWorkerDefinition(publicationGap),
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
                WakePolicy.OnPublication);
}

internal static class AutoItemsServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.auto-items");
    internal static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;
    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
}
