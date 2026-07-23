using System;

namespace OrbModConfig;

/// <summary>Pure cadence state for UI work that must run only under coordinator admission.</summary>
internal sealed class ModConfigRefreshScheduler
{
    private readonly float _intervalSeconds;
    private float _remainingSeconds;
    private bool _open;

    public ModConfigRefreshScheduler(float intervalSeconds)
    {
        if (intervalSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        _intervalSeconds = intervalSeconds;
    }

    public bool IsPending { get; private set; }

    public void Open()
    {
        _open = true;
        _remainingSeconds = _intervalSeconds;
        IsPending = true;
    }

    public void Close()
    {
        _open = false;
        _remainingSeconds = 0f;
        IsPending = false;
    }

    public bool Schedule(float elapsedSeconds)
    {
        if (!_open) return false;
        _remainingSeconds -= Math.Max(0f, elapsedSeconds);
        if (_remainingSeconds <= 0f)
        {
            _remainingSeconds = _intervalSeconds;
            IsPending = true;
        }

        return IsPending;
    }

    public void Complete()
    {
        if (_open) IsPending = false;
    }
}
