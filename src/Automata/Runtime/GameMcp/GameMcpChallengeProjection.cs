#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpChallengeProjection
{
    internal static GameMcpValue Project(in ChallengeSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Receipt.EvidenceAvailable)
        {
            result["nativeStage"] = submission.Stage.ToString();
            result["outcome"] = submission.Outcome.ToString();
            result["requestedMode"] = submission.Receipt.Kind == ChallengeActionKind.Queue
                ? "activate"
                : submission.Receipt.Kind.ToString();
            result["before"] = State(submission.Receipt.Before);
            result["after"] = State(submission.Receipt.After);
        }
        return result.Freeze();
    }

    private static GameMcpObjectBuilder State(ChallengeState state) => new()
    {
        ["targetState"] = state.TargetState < 0 ? null : StateName(state.TargetState),
        ["selected"] = state.Selected,
        ["inTimeOffers"] = state.InTimeOffers,
        ["inPrestigeOffers"] = state.InPrestigeOffers,
        ["worldCycleComplete"] = state.WorldCycleComplete,
        ["challengesFetched"] = state.ChallengesFetched,
        ["rerollsLeft"] = new GameMcpDomainValue(new BigDouble(state.RerollsLeft)),
        ["rerollsMaximum"] = new GameMcpDomainValue(new BigDouble(state.RerollsMaximum)),
        ["timeOffers"] = Ids(state.TimeOffers),
        ["prestigeOffers"] = Ids(state.PrestigeOffers),
        ["timeOffersQueued"] = state.TimeOffersQueued,
        ["prestigeOffersQueued"] = state.PrestigeOffersQueued,
    };

    private static GameMcpArrayBuilder Ids(Guid[] ids)
    {
        var result = new GameMcpArrayBuilder();
        for (var index = 0; index < ids.Length; index++) result.Add(ids[index]);
        return result;
    }

    private static string StateName(int state) => state switch
    {
        0 => "idle",
        1 => "queued",
        2 => "active",
        3 => "passed",
        4 => "failed",
        _ => "unknown",
    };
}
#endif
