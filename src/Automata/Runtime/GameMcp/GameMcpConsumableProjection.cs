#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpConsumableProjection
{
    internal static GameMcpValue Project(in ConsumablePlayerSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.Evidence.Available)
        {
            var before = submission.Evidence.Before;
            var after = submission.Evidence.After;
            result["nativeStage"] = submission.Stage.ToString();
            result["outcome"] = submission.Outcome.ToString();
            result["before"] = State(in before);
            result["after"] = State(in after);
        }
        return result.Freeze();
    }

    private static GameMcpValue State(in ConsumablePlayerState state)
    {
        var result = new JObject
        {
            ["amount"] = state.Amount,
            ["queued"] = state.Queued,
            ["randomized"] = state.Randomized,
        };
        if (state.UsageIds.Length > 0)
        {
            var usages = new JArray();
            for (var index = 0; index < state.UsageIds.Length; index++)
                usages.Add(new JObject
                {
                    ["usageId"] = state.UsageIds[index].ToString("D"),
                });
            result["usages"] = usages;
        }
        if (state.OrderedList.Length > 0)
        {
            var slots = new JArray();
            for (var index = 0; index < state.OrderedList.Length; index++)
            {
                var slot = new JObject { ["position"] = index };
                if (state.OrderedList[index] == System.Guid.Empty) slot["empty"] = true;
                else slot["consumableId"] = state.OrderedList[index].ToString("D");
                slots.Add(slot);
            }
            result["orderedList"] = slots;
        }
        return result.Freeze();
    }
}
#endif
