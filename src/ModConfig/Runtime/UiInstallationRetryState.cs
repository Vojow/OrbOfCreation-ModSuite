namespace OrbModConfig;

internal readonly record struct UiInstallationRetryObservation(
    int Attempt,
    bool ShouldLogRetry,
    bool IsTerminal);

/// <summary>
/// Shared retry-reporting discipline for the Mods rail and quick-controls surface.
/// Capture cadence remains owned by the plugin's single UI retry interval.
/// </summary>
internal sealed class UiInstallationRetryState
{
    internal const int TerminalAttempt = 3;

    private int _attempts;
    private bool _retryLogged;

    internal UiInstallationRetryObservation ObserveFailure()
    {
        _attempts++;
        var shouldLogRetry = !_retryLogged;
        _retryLogged = true;
        return new UiInstallationRetryObservation(
            _attempts,
            shouldLogRetry,
            _attempts >= TerminalAttempt);
    }

    internal void Reset()
    {
        _attempts = 0;
        _retryLogged = false;
    }
}
