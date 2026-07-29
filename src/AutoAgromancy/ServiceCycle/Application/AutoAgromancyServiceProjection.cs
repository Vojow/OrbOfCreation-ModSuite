using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoAgromancyServiceProjection
{
    internal const int ActivePairsKey = 10;
    internal const int SweepCursorKey = 11;
    internal const int PlannedActionsKey = 12;
    internal const int DecisionKindKey = 13;
    internal const int PlanDispositionKey = 14;

    internal static void Write(
        in AutoAgromancyCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(
            new ServiceProjectionKey(ActivePairsKey),
            ServiceProjectionValue.FromInteger(decision.ActivePairs));
        output.Add(
            new ServiceProjectionKey(SweepCursorKey),
            ServiceProjectionValue.FromInteger(decision.SweepCursor));
        output.Add(
            new ServiceProjectionKey(PlannedActionsKey),
            ServiceProjectionValue.FromInteger(decision.PlannedActions));
        output.Add(
            new ServiceProjectionKey(DecisionKindKey),
            ServiceProjectionValue.FromInteger((int)decision.Kind));
        output.Add(
            new ServiceProjectionKey(PlanDispositionKey),
            ServiceProjectionValue.FromInteger((int)decision.PlanDisposition));
    }

    internal static bool TryReadDecision(
        in ServiceStateProjectionSnapshot projection,
        out AutoAgromancyDecisionKind decision,
        out int plannedActions)
    {
        var decisionValue = default(ServiceProjectionValue);
        var plannedValue = default(ServiceProjectionValue);
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == DecisionKindKey) decisionValue = entry.Value;
            else if (entry.Key.Value == PlannedActionsKey) plannedValue = entry.Value;
        }

        if (decisionValue.Kind != ServiceProjectionValueKind.Integer ||
            plannedValue.Kind != ServiceProjectionValueKind.Integer ||
            decisionValue.Integer < (int)AutoAgromancyDecisionKind.Disabled ||
            decisionValue.Integer > (int)AutoAgromancyDecisionKind.AlreadyBalanced ||
            plannedValue.Integer is < 0 or > int.MaxValue)
        {
            decision = default;
            plannedActions = 0;
            return false;
        }

        decision = (AutoAgromancyDecisionKind)decisionValue.Integer;
        plannedActions = (int)plannedValue.Integer;
        return true;
    }
}
