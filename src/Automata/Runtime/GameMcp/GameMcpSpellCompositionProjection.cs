#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellCompositionProjection
{
    internal static GameMcpValue Project(in SpellCompositionSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.Evidence.Available)
        {
            result["nativeStage"] = submission.Stage.ToString();
            result["outcome"] = submission.Outcome.ToString();
            var before = submission.Evidence.Before;
            var after = submission.Evidence.After;
            result["before"] = State(in before);
            result["after"] = State(in after);
        }
        return result.Freeze();
    }

    private static JObject State(in SpellCompositionState state) =>
        new JObject
        {
            ["outputLevel"] = state.OutputLevel,
            ["maximumOutputLevel"] = state.MaximumOutputLevel,
        };
}
#endif
