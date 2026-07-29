using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbMentor;

internal static class MentorService
{
    internal static IAutomataServiceDefinition<MentorCycleState, MentorCycleAction> Define(
        IMentorCycleActionPort actions)
    {
        var metadata = new AutomataServiceMetadata(
            MentorServicePolicies.ServiceId,
            MentorServicePolicies.DefaultWakePolicy,
            MentorServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<MentorCycleState, MentorCycleAction>(
            in metadata,
            static () => new MentorWorkerDefinition(),
            ShouldStart,
            actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        MentorConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(MentorConfigurationPolicy.IdleInterval));
}

internal static class MentorServicePolicies
{
    internal static ServiceId ServiceId => new("orbmentor.mastery-sharing");
    internal static WakePolicy DefaultWakePolicy =>
        WakePolicy.AfterDecision(MentorConfigurationPolicy.IdleInterval);
    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(System.TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(System.TimeSpan.FromSeconds(10)));
}
