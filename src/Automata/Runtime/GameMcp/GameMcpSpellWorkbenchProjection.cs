#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellWorkbenchProjection
{
    internal static GameMcpValue Project(in SpellWorkbenchSubmission submission)
    {
        if (submission.Verified || submission.CallOutcome.MutationAttempts == 0)
            return new JObject().Freeze();
        return new JObject
        {
            ["missingOutcome"] = "requested spell workbench transition",
        }.Freeze();
    }
}
#endif
