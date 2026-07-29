using System;

namespace OrbAutomata;

internal sealed class DecisionLogGate
{
    private readonly TimeSpan _repeatInterval;
    private string? _lastState;
    private TimeSpan _lastLoggedAt;

    public DecisionLogGate(TimeSpan repeatInterval)
    {
        _repeatInterval = repeatInterval < TimeSpan.Zero ? TimeSpan.Zero : repeatInterval;
    }

    public bool ShouldLog(string state, TimeSpan now)
    {
        if (!string.Equals(state, _lastState, StringComparison.Ordinal) || now - _lastLoggedAt >= _repeatInterval)
        {
            _lastState = state;
            _lastLoggedAt = now;
            return true;
        }

        return false;
    }
}
