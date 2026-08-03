#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpRitualLifecycleProjection
{
    internal static GameMcpValue Project(in RitualLifecycleSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested Ritual transition";
        return result.Freeze();
    }
}
#endif
