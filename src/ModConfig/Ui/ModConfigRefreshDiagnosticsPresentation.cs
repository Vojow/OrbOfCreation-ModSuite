using System.Globalization;

namespace OrbModConfig;

internal static class ModConfigRefreshDiagnosticsPresentation
{
    public static string Build(ModConfigRefreshDiagnostics diagnostics)
    {
        if (!diagnostics.IsOpen) return "Mods refresh is inactive while this page is closed.";
        var lastCompleted = diagnostics.HasCompleted
            ? FormatAge(diagnostics.LastCompletedAgeSeconds) + " ago"
            : "not yet";
        var pending = diagnostics.IsPending
            ? "pending for " + FormatAge(diagnostics.PendingAgeSeconds)
            : "idle";
        return "Runtime evidence updates live. Mods refresh: " + pending +
               "; last completed " + lastCompleted + ".";
    }

    private static string FormatAge(float seconds) =>
        seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
}
