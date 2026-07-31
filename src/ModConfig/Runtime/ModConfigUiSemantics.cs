namespace OrbModConfig;

internal static class ModConfigTabSelectionPolicy
{
    internal static bool RequestedOpenState(bool currentlyOpen) => true;
}

internal static class ModConfigFirstInstallationPolicy
{
    internal static bool CanAttempt(bool nativeUiStartBoundaryReached) =>
        nativeUiStartBoundaryReached;
}
