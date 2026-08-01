using System;

namespace OrbAutomata;

/// <summary>
/// Prevents Auto Items and Auto Scribe from acting on a world captured before the other feature's
/// latest native mutation attempt. Only frame and lifecycle identities cross this boundary.
/// </summary>
internal sealed class ConsumableMutationGate
{
    private readonly object _sync = new();
    private long _lifecycle;
    private long _mutationFrame;

    internal bool Blocks(long lifecycle, long collectedFrame)
    {
        lock (_sync)
            return lifecycle > 0 && lifecycle == _lifecycle &&
                _mutationFrame > 0 && collectedFrame <= _mutationFrame;
    }

    internal void ObserveAttempt(long lifecycle, long mutationFrame)
    {
        if (lifecycle <= 0 || mutationFrame <= 0) return;
        lock (_sync)
        {
            if (_lifecycle != lifecycle)
            {
                _lifecycle = lifecycle;
                _mutationFrame = mutationFrame;
                return;
            }
            _mutationFrame = Math.Max(_mutationFrame, mutationFrame);
        }
    }

    internal void Invalidate(long lifecycle)
    {
        lock (_sync)
        {
            _lifecycle = lifecycle;
            _mutationFrame = 0;
        }
    }
}
