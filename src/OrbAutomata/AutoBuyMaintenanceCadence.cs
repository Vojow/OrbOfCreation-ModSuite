using System;

namespace OrbAutomata;

internal sealed class AutoBuyMaintenanceCadence
{
    private readonly TimeSpan _interval;
    private readonly int _activeRefreshCount;
    private readonly int _slowRefreshCount;
    private TimeSpan _nextRefreshAt;

    public AutoBuyMaintenanceCadence(
        TimeSpan interval,
        int activeRefreshCount,
        int slowRefreshCount)
    {
        _interval = interval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : interval;
        _activeRefreshCount = Math.Max(0, activeRefreshCount);
        _slowRefreshCount = Math.Max(0, slowRefreshCount);
    }

    public bool TryTake(
        TimeSpan now,
        out int activeRefreshCount,
        out int slowRefreshCount)
    {
        if (now < _nextRefreshAt)
        {
            activeRefreshCount = 0;
            slowRefreshCount = 0;
            return false;
        }

        // Do not accumulate catch-up debt after a long pause or scene load.
        // One bounded slice is enough; the next slice is scheduled from now.
        _nextRefreshAt = now + _interval;
        activeRefreshCount = _activeRefreshCount;
        slowRefreshCount = _slowRefreshCount;
        return true;
    }

    public void Reset(TimeSpan now)
    {
        _nextRefreshAt = now;
    }
}
