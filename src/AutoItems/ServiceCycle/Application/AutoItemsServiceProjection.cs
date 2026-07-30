using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsServiceProjection
{
    internal const int CapturedKey = 10;
    internal const int RejectedProfilesKey = 11;
    internal const int EligibleRelicsKey = 12;
    internal const int EligibleScrollsKey = 13;
    internal const int PlannedActionsKey = 14;
    internal const int DecisionKindKey = 15;

    internal static void Write(
        in AutoItemsCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(new ServiceProjectionKey(CapturedKey), Integer(decision.Captured));
        output.Add(new ServiceProjectionKey(RejectedProfilesKey), Integer(decision.RejectedProfiles));
        output.Add(new ServiceProjectionKey(EligibleRelicsKey), Integer(decision.EligibleRelics));
        output.Add(new ServiceProjectionKey(EligibleScrollsKey), Integer(decision.EligibleScrolls));
        output.Add(new ServiceProjectionKey(PlannedActionsKey), Integer(decision.PlannedActions));
        output.Add(new ServiceProjectionKey(DecisionKindKey), Integer((int)decision.Kind));
    }

    internal static bool TryReadDecisionKind(
        in ServiceStateProjectionSnapshot projection,
        out AutoItemsDecisionKind kind)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value != DecisionKindKey ||
                entry.Value.Kind != ServiceProjectionValueKind.Integer ||
                entry.Value.Integer is < (int)AutoItemsDecisionKind.Disabled
                    or > (int)AutoItemsDecisionKind.Scroll)
                continue;
            kind = (AutoItemsDecisionKind)entry.Value.Integer;
            return true;
        }
        kind = AutoItemsDecisionKind.Disabled;
        return false;
    }

    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}
