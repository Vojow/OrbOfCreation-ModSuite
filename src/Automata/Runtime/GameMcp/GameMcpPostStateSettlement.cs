#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

/// <summary>One bounded freshness rule shared by every MCP mutation projection.</summary>
internal static class GameMcpPostStateSettlement
{
    internal const float MaximumWaitSeconds = 1f;

    internal static bool IsStrictlyNewer(ulong candidate, ulong mutationWorld) =>
        candidate > mutationWorld;

    internal static bool HasFreshWorld(GameMcpFrameContext? state, ulong mutationWorld) =>
        state?.World is not null &&
        IsStrictlyNewer(state.World.Generation.Value, mutationWorld);

    /// <summary>
    /// The single settlement predicate used by mutation response dispatch. Most actions settle
    /// as soon as the next immutable world exists; discovery offers additionally require the
    /// event-driven offer transition to be visible in that world.
    /// </summary>
    internal static bool IsReady(
        GameMcpFrameContext? state,
        ulong mutationWorld,
        GameMcpCommand command) =>
        HasFreshWorld(state, mutationWorld) &&
        (command.Kind != GameMcpCommandKind.DiscoveryTreeOffer ||
         GameMcpWorldQuery.HasDiscoveryPostState(
             state!,
             command.TargetId,
             command.Mode,
             command.SecondaryId));
}
#endif
