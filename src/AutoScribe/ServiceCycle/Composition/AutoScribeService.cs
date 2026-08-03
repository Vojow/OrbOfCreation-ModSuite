using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoScribeService
{
    internal static IAutomataServiceDefinition<AutoScribeCycleState, AutoScribeCycleAction> Define(
        AutoScribeIdentityProfile profile,
        IAutoScribeCycleActionPort actions)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        var metadata = new AutomataServiceMetadata(
            AutoScribeServicePolicies.ServiceId,
            WakePolicy.OnPublication,
            new ServiceFaultRecoveryPolicy(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10))));
        return AutomataService.Define<AutoScribeCycleState, AutoScribeCycleAction>(
            in metadata,
            () => new AutoScribeWorkerDefinition(profile),
            ShouldStart,
            static (in AutoScribeCycleAction action) =>
                ServiceActionJournalAttribution.Native(
                    action.RecipeId,
                    ServiceActionNativeTypeId.CraftingRecipeSO),
            actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        AutoScribeConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.OnPublication);
}

internal static class AutoScribeServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.auto-scribe");
}
