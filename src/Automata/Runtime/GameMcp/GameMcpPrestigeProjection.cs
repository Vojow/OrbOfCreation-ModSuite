#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPrestigeProjection
{
    internal static GameMcpValue Project(in PrestigeSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder
        {
            ["preflight"] = GameMcpEntityWireNormalizer.Snake(submission.Preflight.ToString()),
        };
        if (submission.Receipt.EvidenceAvailable)
        {
            result["nativeStage"] = GameMcpEntityWireNormalizer.Snake(submission.Stage.ToString());
            result["outcome"] = GameMcpEntityWireNormalizer.Snake(submission.Outcome.ToString());
            result["before"] = State(submission.Receipt.Before);
            result["observedLifecycleGeneration"] = submission.Receipt.After.LifecycleEpoch;
        }
        if (submission.Preflight is PrestigePreflight.Quarantined or
            PrestigePreflight.PostCommitFault or PrestigePreflight.VerificationFailed)
            result["quarantined"] = true;
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
