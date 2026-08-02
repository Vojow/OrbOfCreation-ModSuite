#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpEquipmentLoadoutProjection
{
    internal static GameMcpValue Project(in EquipmentLoadoutSubmission submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested equipped stack count";
        return result.Freeze();
    }
}
#endif
