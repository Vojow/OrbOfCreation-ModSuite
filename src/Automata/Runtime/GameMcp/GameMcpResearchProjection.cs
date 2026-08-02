#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpResearchProjection
{
    internal static GameMcpValue Project(in ResearchSubmission submission)
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
            result["requestedMode"] = GameMcpEntityWireNormalizer.Snake(submission.Receipt.Kind.ToString());
            result["before"] = State(submission.Receipt.Before);
            result["after"] = State(submission.Receipt.After);
        }
        if (submission.Preflight is ResearchPreflight.Quarantined or
            ResearchPreflight.PostCommitFault or ResearchPreflight.VerificationFailed)
            result["quarantined"] = true;
        return result.Freeze();
    }

    private static GameMcpObjectBuilder State(ResearchState state) => new()
    {
        ["route"] = state.QueueMode ? "queue" : "immediate",
        ["state"] = state.IsDeveloping ? state.IsActive ? "active" : "paused" : "idle",
        ["purchasedLevel"] = Number(state.PurchasedLevels),
        ["bonusLevel"] = Number(state.BonusLevel),
        ["totalLevel"] = Number(state.TotalLevel),
        ["queuedLevels"] = Number(state.QueuedLevels),
        ["stage"] = Number(state.Stage),
        ["selfBonusLevels"] = Number(state.SelfBonusLevels),
        ["investmentLevel"] = Number(state.CurrentInvestmentLevel),
        ["progress"] = new GameMcpDomainValue(state.TimeRatio),
        ["freeBonusLevels"] = Number(state.FreeBonusLevels),
    };

    private static GameMcpDomainValue Number(int value) => new(new BigDouble(value));
}
#endif
