#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpLoadoutProjection
{
    internal static GameMcpValue Project(in LoadoutSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested loadout transition";
        return result.Freeze();
    }
}
#endif
