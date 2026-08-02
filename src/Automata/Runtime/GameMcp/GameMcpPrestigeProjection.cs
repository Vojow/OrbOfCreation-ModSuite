#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPrestigeProjection
{
    internal static GameMcpValue Project(in PrestigeSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Receipt.EvidenceAvailable)
        {
            result["nativeStage"] = submission.Stage.ToString();
            result["outcome"] = submission.Outcome.ToString();
            result["before"] = State(submission.Receipt.Before);
            result["observedLifecycleGeneration"] = submission.Receipt.After.LifecycleEpoch;
        }
        return result.Freeze();
    }

    private static GameMcpObjectBuilder State(PrestigeState state) => new()
    {
        ["lifecycleGeneration"] = state.LifecycleEpoch,
        ["worldCycleComplete"] = state.WorldCycleComplete,
        ["challengesFetched"] = state.ChallengesFetched,
        ["resetCount"] = new GameMcpDomainValue(new BigDouble(state.ResetCount)),
    };
}
#endif
