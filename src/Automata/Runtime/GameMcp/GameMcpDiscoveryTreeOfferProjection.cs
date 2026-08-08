#if SERVICE_CYCLE_PROFILE
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpDiscoveryTreeOfferProjection
{
    internal static GameMcpValue Project(
        DiscoveryTreeOfferActionKind kind,
        in DiscoveryTreeOfferSubmission submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.CallOutcome.MutationAttempts > 0)
        {
            result["missingOutcome"] = kind switch
            {
                DiscoveryTreeOfferActionKind.Initiate => "crafting mode",
                DiscoveryTreeOfferActionKind.Select => "requested offer selected",
                DiscoveryTreeOfferActionKind.Confirm => "requested offer discovered",
                DiscoveryTreeOfferActionKind.Reroll => "crafting mode",
                _ => "requested transition",
            };
        }
        return result.Freeze();
    }
}
#endif
