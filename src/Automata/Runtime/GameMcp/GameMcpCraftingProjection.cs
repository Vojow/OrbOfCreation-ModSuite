#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCraftingProjection
{
    internal static GameMcpValue Project(in CraftingPlayerSubmission submission)
    {
        if (submission.Verified)
        {
            var result = new JObject();
            if (submission.Postcondition is CraftingPlayerPostcondition.DirectEffectAdvanced or
                CraftingPlayerPostcondition.InstantCompleted)
                result["completed"] = true;
            return result.Freeze();
        }
        if (submission.CallOutcome.MutationAttempts == 0) return new JObject().Freeze();
        return new JObject
        {
            ["missingOutcome"] = "requested craft completion",
        }.Freeze();
    }

    internal static bool ProvedCompletion(GameMcpValue? details)
    {
        if (details is not GameMcpObject result) return false;
        for (var index = 0; index < result.Properties.Count; index++)
        {
            var property = result.Properties[index];
            if (property.Name == "completed" && property.Value is GameMcpScalar scalar &&
                scalar.Value is bool completed)
                return completed;
        }
        return false;
    }

    internal static GameMcpValue Project(
        in CraftingInstanceLifecycleSubmission submission)
    {
        if (submission.Verified || submission.CallOutcome.MutationAttempts == 0)
            return new JObject().Freeze();
        var failure = new JObject
        {
            ["missingOutcome"] = "requested crafting-instance transition",
        };
        if (submission.SideEffect.Observed)
        {
            failure["observed"] = new JObject
            {
                ["automation"] = new JObject
                {
                    ["before"] = submission.SideEffect.AutomationBefore,
                    ["after"] = submission.SideEffect.AutomationAfter,
                },
            };
        }
        return failure.Freeze();
    }
}
#endif
