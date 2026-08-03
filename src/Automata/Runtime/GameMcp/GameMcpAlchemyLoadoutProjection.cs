#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpAlchemyLoadoutProjection
{
    internal static GameMcpValue Project(in AlchemyLoadoutSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested Alchemy loadout transition";
        return result.Freeze();
    }
}
#endif
