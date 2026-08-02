#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCraftingProjection
{
    internal static GameMcpValue Project(in CraftingPlayerSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject
        {
            ["preflight"] = GameMcpEntityWireNormalizer.Snake(
                submission.Preflight.ToString()),
        };
        if (submission.Evidence.Available)
        {
            var before = submission.Evidence.Before;
            var after = submission.Evidence.After;
            result["nativeStage"] = GameMcpEntityWireNormalizer.Snake(
                submission.Stage.ToString());
            result["outcome"] = GameMcpEntityWireNormalizer.Snake(
                submission.Outcome.ToString());
            result["before"] = State(in before);
            result["after"] = State(in after);
        }
        return result.Freeze();
    }

    private static GameMcpValue State(in CraftingPlayerState state)
    {
        var result = new JObject
        {
            ["execution"] = GameMcpEntityWireNormalizer.Snake(state.Pipeline.ToString()),
            ["purchaseAmount"] = new GameMcpDomainValue(state.PurchaseAmount),
        };
        if (state.Pipeline is CraftingPlayerPipeline.QueueStack or
            CraftingPlayerPipeline.QueueNew or CraftingPlayerPipeline.QueueInstant)
        {
            result["queuedAmount"] = new GameMcpDomainValue(state.QueuedAmount);
            result["queueUsed"] = state.QueueUsed;
            result["queueMaximum"] = state.QueueMaximum;
        }
        return result.Freeze();
    }
}
#endif
