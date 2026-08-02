#if SERVICE_CYCLE_PROFILE
using System;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellLoadoutProjection
{
    internal static GameMcpValue Project(in SpellLoadoutSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject
        {
            ["preflight"] = GameMcpEntityWireNormalizer.Snake(submission.Preflight.ToString()),
        };
        if (submission.Evidence.Available)
        {
            result["nativeStage"] = GameMcpEntityWireNormalizer.Snake(submission.Stage.ToString());
            result["outcome"] = GameMcpEntityWireNormalizer.Snake(submission.Outcome.ToString());
            result["sourceSlot"] = submission.Evidence.SourceSlot;
            if (submission.Evidence.DestinationSlot >= 0)
                result["destinationSlot"] = submission.Evidence.DestinationSlot;
            var before = submission.Evidence.Before;
            var after = submission.Evidence.After;
            result["before"] = State(in before);
            result["after"] = State(in after);
        }
        return result.Freeze();
    }

    private static JObject State(in SpellLoadoutState state)
    {
        var slots = new JArray();
        for (var index = 0; index < state.Slots.Length; index++)
        {
            var item = new JObject { ["slot"] = index };
            if (state.Slots[index] == Guid.Empty) item["empty"] = true;
            else item["spellInstance"] = new JObject
            {
                ["uuid"] = state.Slots[index].ToString("D"),
                ["name"] = state.Names[index],
            };
            slots.Add(item);
        }
        return new JObject { ["slots"] = slots };
    }
}
#endif
