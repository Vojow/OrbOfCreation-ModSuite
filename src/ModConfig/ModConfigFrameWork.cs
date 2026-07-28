using System;
using OrbModding.Common;

namespace OrbModConfig;

internal sealed class ModConfigFrameWork : IDisposable
{
    private readonly Func<long> _readFrameIdentity;
    private bool _enabled;
    private bool _pending;
    private long _lastRunFrame = -1;

    public ModConfigFrameWork(Func<long> readFrameIdentity)
    {
        _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
    }

    internal bool IsPending => _pending;

    public void SetState(bool enabled, bool pending)
    {
        _enabled = enabled;
        _pending = enabled && pending;
    }

    public bool TryRun(bool enabled, bool pending, Action run)
    {
        SetState(enabled, pending);
        if (!_enabled || !_pending) return false;
        var frame = _readFrameIdentity();
        if (frame == _lastRunFrame) return false;
        _lastRunFrame = frame;
        run();
        _pending = false;
        return true;
    }

    public void Clear() => SetState(false, false);

    public void Dispose() => Clear();
}
