using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoScribeService
{
    internal static IAutomataServiceDefinition<AutoScribeCycleState, AutoScribeCycleAction> Define(
        AutoScribeIdentityProfile profile,
        IAutoScribeCycleActionPort actions,
        Func<bool> ownsActionFamily)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (ownsActionFamily is null) throw new ArgumentNullException(nameof(ownsActionFamily));
        var metadata = new AutomataServiceMetadata(
            AutoScribeServicePolicies.ServiceId,
            WakePolicy.OnPublication,
            new ServiceFaultRecoveryPolicy(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
                MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10))));
        return AutomataService.Define<AutoScribeCycleState, AutoScribeCycleAction>(
            in metadata,
            () => new AutoScribeWorkerDefinition(profile),
            (in SuiteRuntimeConfiguration config, in ServiceCycleStartContext context) =>
                ShouldStart(in config, in context, ownsActionFamily),
            actions.TryExecute);
    }

    internal static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context,
        Func<bool> ownsActionFamily) =>
        AutoScribeConfigurationPolicy.IsOperational(config) && Owns(ownsActionFamily)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.OnPublication);

    private static bool Owns(Func<bool> ownsActionFamily)
    {
        try { return ownsActionFamily(); }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }
}

internal static class AutoScribeServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.auto-scribe");
}
