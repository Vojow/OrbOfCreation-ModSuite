#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

/// <summary>Closed-world names for native probes whose implementations are fixed in the mod.</summary>
internal static class GameMcpGadgetPolicy
{
    internal static bool IsAllowlistedProbe(string probe) =>
        probe is "runtime" or "action_queue_room" or "navigation";

    internal static bool IsCurrentContentSubtabPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("Canvas[0]/ContentArea[", StringComparison.Ordinal);
}
#endif
