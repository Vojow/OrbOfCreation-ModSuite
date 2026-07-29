using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoConceptServiceProjection
{
    internal const int CapturedRecipesKey = 10;
    internal const int EligibleRecipesKey = 11;
    internal const int ActiveRecipesKey = 12;
    internal const int OwnedRecipesKey = 13;
    internal const int PlannedActionsKey = 14;
    internal const int DecisionKindKey = 15;

    public static void Write(in AutoConceptCycleState state, ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(new ServiceProjectionKey(CapturedRecipesKey), ServiceProjectionValue.FromInteger(decision.CapturedRecipes));
        output.Add(new ServiceProjectionKey(EligibleRecipesKey), ServiceProjectionValue.FromInteger(decision.EligibleRecipes));
        output.Add(new ServiceProjectionKey(ActiveRecipesKey), ServiceProjectionValue.FromInteger(decision.ActiveRecipes));
        output.Add(new ServiceProjectionKey(OwnedRecipesKey), ServiceProjectionValue.FromInteger(decision.OwnedRecipes));
        output.Add(new ServiceProjectionKey(PlannedActionsKey), ServiceProjectionValue.FromInteger(decision.PlannedActions));
        output.Add(new ServiceProjectionKey(DecisionKindKey), ServiceProjectionValue.FromInteger((int)decision.Kind));
    }
}
