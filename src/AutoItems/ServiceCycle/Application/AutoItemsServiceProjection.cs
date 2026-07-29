using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsServiceProjection
{
    internal const int CapturedKey = 10;
    internal const int RejectedProfilesKey = 11;
    internal const int TemporaryItemsKey = 12;
    internal const int EligibleRelicsKey = 13;
    internal const int EligibleScrollsKey = 14;
    internal const int PlannedActionsKey = 15;
    internal const int DecisionKindKey = 16;

    internal static void Write(
        in AutoItemsCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(new ServiceProjectionKey(CapturedKey), ServiceProjectionValue.FromInteger(decision.Captured));
        output.Add(new ServiceProjectionKey(RejectedProfilesKey), ServiceProjectionValue.FromInteger(decision.RejectedProfiles));
        output.Add(new ServiceProjectionKey(TemporaryItemsKey), ServiceProjectionValue.FromInteger(decision.TemporaryItems));
        output.Add(new ServiceProjectionKey(EligibleRelicsKey), ServiceProjectionValue.FromInteger(decision.EligibleRelics));
        output.Add(new ServiceProjectionKey(EligibleScrollsKey), ServiceProjectionValue.FromInteger(decision.EligibleScrolls));
        output.Add(new ServiceProjectionKey(PlannedActionsKey), ServiceProjectionValue.FromInteger(decision.PlannedActions));
        output.Add(new ServiceProjectionKey(DecisionKindKey), ServiceProjectionValue.FromInteger((int)decision.Kind));
    }
}
