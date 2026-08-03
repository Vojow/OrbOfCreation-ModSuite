#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpHarvestLifecycleProjection
{
    internal static GameMcpValue Project(in HarvestLifecycleSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested harvest-list transition";
        return result.Freeze();
    }
}
#endif
