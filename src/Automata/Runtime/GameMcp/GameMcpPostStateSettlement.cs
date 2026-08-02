#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

/// <summary>One bounded freshness rule shared by every MCP mutation projection.</summary>
internal static class GameMcpPostStateSettlement
{
    internal const float MaximumWaitSeconds = 0.25f;

    internal static bool IsStrictlyNewer(ulong candidate, ulong mutationWorld) =>
        candidate > mutationWorld;
}
#endif
