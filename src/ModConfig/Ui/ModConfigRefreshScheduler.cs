using System;

namespace OrbModConfig;

internal readonly record struct ModConfigRefreshDiagnostics(
    bool IsOpen,
    bool IsPending,
    float PendingAgeSeconds,
    bool HasCompleted,
    float LastCompletedAgeSeconds);

/// <summary>Pure cadence state for UI work that must run only under coordinator admission.</summary>
internal sealed class ModConfigRefreshScheduler
{
    private readonly float _intervalSeconds;
    private float _remainingSeconds;
    private float _elapsedSeconds;
    private float _pendingSinceSeconds;
    private float _lastCompletedSeconds;
    private bool _open;
    private bool _hasCompleted;
    private bool _diagnosticsDue;

    public ModConfigRefreshScheduler(float intervalSeconds)
    {
        if (intervalSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        _intervalSeconds = intervalSeconds;
    }

    public bool IsPending { get; private set; }

    public ModConfigRefreshDiagnostics Diagnostics => new(
        _open,
        IsPending,
        IsPending ? Math.Max(0f, _elapsedSeconds - _pendingSinceSeconds) : 0f,
        _hasCompleted,
        _hasCompleted ? Math.Max(0f, _elapsedSeconds - _lastCompletedSeconds) : 0f);

    public void Open()
    {
        _open = true;
        _remainingSeconds = _intervalSeconds;
        _pendingSinceSeconds = _elapsedSeconds;
        IsPending = true;
        _diagnosticsDue = true;
    }

    public void Close()
    {
        _open = false;
        _remainingSeconds = 0f;
        IsPending = false;
        _diagnosticsDue = false;
    }

    public bool Schedule(float elapsedSeconds)
    {
        if (!_open) return false;
        var elapsed = Math.Max(0f, elapsedSeconds);
        _elapsedSeconds += elapsed;
        _remainingSeconds -= elapsed;
        if (_remainingSeconds <= 0f)
        {
            _remainingSeconds = _intervalSeconds;
            if (!IsPending) _pendingSinceSeconds = _elapsedSeconds;
            IsPending = true;
            _diagnosticsDue = true;
        }

        return IsPending;
    }

    public void Complete()
    {
        if (!_open) return;
        IsPending = false;
        _hasCompleted = true;
        _lastCompletedSeconds = _elapsedSeconds;
        _diagnosticsDue = true;
    }

    public bool ConsumeDiagnosticsDue()
    {
        if (!_diagnosticsDue) return false;
        _diagnosticsDue = false;
        return true;
    }
}
