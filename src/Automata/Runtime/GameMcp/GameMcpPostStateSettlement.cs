#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>One bounded freshness rule shared by every MCP mutation projection.</summary>
internal static class GameMcpPostStateSettlement
{
    internal const float MaximumWaitSeconds = 1f;

    internal static bool IsStrictlyNewer(ulong candidate, ulong mutationWorld) =>
        candidate > mutationWorld;

    internal static bool HasSettledWorld(
        GameMcpFrameContext? state,
        ulong mutationWorld,
        long actionCompletedAtUtcTicks) =>
        state?.World is not null &&
        IsStrictlyNewer(state.World.Generation.Value, mutationWorld) &&
        state.World.Snapshot.CollectedAtUtcTicks > actionCompletedAtUtcTicks;

    /// <summary>
    /// The single settlement predicate used by mutation response dispatch. Most actions settle
    /// as soon as the next immutable world exists; discovery offers additionally require the
    /// event-driven offer transition to be visible in that world.
    /// </summary>
    internal static bool IsReady(
        GameMcpFrameContext? state,
        ulong mutationWorld,
        long actionCompletedAtUtcTicks,
        GameMcpCommand command) =>
        HasSettledWorld(state, mutationWorld, actionCompletedAtUtcTicks) &&
        HasRequestedOutcome(state!, command) &&
        (command.Kind != GameMcpCommandKind.DiscoveryTreeOffer ||
         GameMcpWorldQuery.HasDiscoveryPostState(
             state!,
             command.TargetId,
             command.Mode,
             command.SecondaryId));

    private static bool HasRequestedOutcome(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (command.Kind == GameMcpCommandKind.Cast &&
            string.Equals(command.Mode, "toggle_off", System.StringComparison.Ordinal))
        {
            return WorldSpellSlotLookup.TryFind(
                    state.World!.Snapshot.SpellSlots,
                    command.Amount - 1,
                    out var slot) &&
                slot.Occupied &&
                slot.SpellRecipeId == command.TargetId &&
                slot.Toggled &&
                !slot.Casting;
        }
        if (command.Kind == GameMcpCommandKind.StructureLifecycle)
        {
            return WorldLookup.TryFind(
                    state.World!.Snapshot.Structures,
                    command.TargetId,
                    out var structure) &&
                structure.Reading.Disabled ==
                    string.Equals(command.Mode, "disable", System.StringComparison.Ordinal);
        }
        if (command.Kind != GameMcpCommandKind.SpellComposition) return true;
        var workbench = state.World!.Snapshot.SpellWorkbench;
        return command.Mode switch
        {
            "set_output_level" => workbench.OutputLevel == command.Amount,
            "set_reserve_level" => workbench.ReserveLevel == command.Amount,
            _ => false,
        };
    }

    internal static GameMcpValue TimedOut(
        GameMcpCommand command,
        GameMcpFrameContext? latest)
    {
        if (command.Kind == GameMcpCommandKind.SpellComposition && latest?.World is not null)
        {
            var workbench = latest.World.Snapshot.SpellWorkbench;
            var observed = command.Mode == "set_output_level"
                ? workbench.OutputLevel
                : workbench.ReserveLevel;
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled " + command.PayloadKey + " dial is " + observed +
                ", not the requested " + command.Amount);
        }
        if (command.Kind == GameMcpCommandKind.Cast &&
            string.Equals(command.Mode, "toggle_off", System.StringComparison.Ordinal))
        {
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled spell slot did not show the requested toggle as off");
        }
        if (command.Kind == GameMcpCommandKind.StructureLifecycle)
        {
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled attribute did not show the requested enabled state");
        }
        return GameMcpWorldQuery.PostStateUnavailable(
            "post_state_timeout",
            "no world captured after the action exposed its committed post-state within one second");
    }
}
#endif
