#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

internal enum GameMcpGadgetAccess
{
    Framebuffer = 1,
    Navigation = 2,
    Probe = 3,
    ScreenCatalog = 4,
    TooltipCatalog = 5,
    TooltipRead = 6,
    ContinueRun = 7,
    Modal = 8,
}

/// <summary>Closed-world names for native probes whose implementations are fixed in the mod.</summary>
internal static class GameMcpGadgetPolicy
{
    internal static GameMcpGadgetAccess AccessFor(GameMcpCommandKind kind) => kind switch
    {
        GameMcpCommandKind.Screenshot => GameMcpGadgetAccess.Framebuffer,
        GameMcpCommandKind.Navigation => GameMcpGadgetAccess.Navigation,
        GameMcpCommandKind.Probe => GameMcpGadgetAccess.Probe,
        GameMcpCommandKind.ScreenCatalog => GameMcpGadgetAccess.ScreenCatalog,
        GameMcpCommandKind.TooltipCatalog => GameMcpGadgetAccess.TooltipCatalog,
        GameMcpCommandKind.TooltipRead => GameMcpGadgetAccess.TooltipRead,
        GameMcpCommandKind.ContinueRun => GameMcpGadgetAccess.ContinueRun,
        GameMcpCommandKind.Modal => GameMcpGadgetAccess.Modal,
        _ => throw new ArgumentException(
            "the command does not name a request-time MCP gadget",
            nameof(kind)),
    };

    internal static bool IsAllowlistedProbe(string probe) =>
        probe is "runtime" or "action_queue_room" or "navigation";

    internal static bool IsCurrentContentSubtabPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("Canvas[0]/ContentArea[", StringComparison.Ordinal);

    internal static bool IsPlotDestination(string screen, string? subtab) =>
        string.Equals(screen, "World", StringComparison.Ordinal) &&
        string.Equals(subtab, "Agromancy", StringComparison.Ordinal);
}
#endif
