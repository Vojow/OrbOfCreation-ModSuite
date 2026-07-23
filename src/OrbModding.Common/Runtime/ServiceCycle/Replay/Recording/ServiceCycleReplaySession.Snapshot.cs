using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public sealed partial class ServiceCycleReplaySession
{
    public ServiceCycleReplayRecordingSnapshot Snapshot =>
        TryReadSnapshot(out var snapshot) ? snapshot : default;

    public bool TryReadSnapshot(out ServiceCycleReplayRecordingSnapshot snapshot)
    {
        if (Volatile.Read(ref _snapshotWriters) != 0)
        {
            snapshot = default;
            return false;
        }
        var before = Volatile.Read(ref _fenceVersion);
        var publication = Interlocked.Read(ref _fencePublication);
        var recordSequence = Interlocked.Read(ref _recordSequence);
        var footerSequence = Interlocked.Read(ref _footerSequence);
        var recordCount = Volatile.Read(ref _recordCount);
        var footerCount = Volatile.Read(ref _footerCount);
        var byteCount = Volatile.Read(ref _byteCount);
        var codecManifestCount = Volatile.Read(ref _codecManifestCount);
        var codecManifestPublication = Interlocked.Read(ref _codecManifestPublication);
        var failureState = Volatile.Read(ref _failureState);
        var firstIncompleteCycle = _firstIncompleteCycle;
        var completeness = failureState == 1
            ? _completeness
            : ServiceCycleReplayCompleteness.Complete;
        var fault = failureState == 1 ? _fault : default;
        var after = Volatile.Read(ref _fenceVersion);
        if (failureState < 0 || before != after || Volatile.Read(ref _snapshotWriters) != 0)
        {
            snapshot = default;
            return false;
        }

        var fence = new ServiceCycleReplayHighWaterFence(
            publication, recordSequence, footerSequence, recordCount, footerCount, byteCount);
        snapshot = new ServiceCycleReplayRecordingSnapshot(
            _traceSession,
            _encodingEnabled,
            new ServiceCycleReplayCodecManifestFence(codecManifestPublication, codecManifestCount),
            fence,
            failureState == 1 ? firstIncompleteCycle : default,
            completeness,
            fault);
        return true;
    }

    public bool TryReadHighWaterFence(out ServiceCycleReplayHighWaterFence fence)
    {
        if (!TryReadSnapshot(out var snapshot))
        {
            fence = default;
            return false;
        }
        fence = snapshot.HighWater;
        return true;
    }

    /// <summary>
    /// Offline-only blocking wait for a footer sequence newer than <paramref name="sequence"/>.
    /// This acquires a monitor and is neither a Unity/main-thread API nor a hard-real-time primitive.
    /// </summary>
    public bool WaitForFooterAfter(long sequence, TimeSpan timeout)
    {
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite bounded timeout is required.");
        if (!_encodingEnabled) return false;

        var timeoutMilliseconds = timeout == TimeSpan.Zero
            ? 0
            : checked((int)Math.Ceiling(timeout.TotalMilliseconds));
        var timeoutStopwatchTicks = timeout == TimeSpan.Zero
            ? 0L
            : checked((long)Math.Ceiling(timeout.TotalSeconds * Stopwatch.Frequency));
        lock (_commitGate)
        {
            if (_footerSequence > sequence) return true;
            if (_footerCount == _cycleFooterCapacity || timeoutMilliseconds == 0) return false;

            var startedAt = Stopwatch.GetTimestamp();
            _offlineFooterWaiterCount++;
            try
            {
                var remaining = timeoutMilliseconds;
                while (true)
                {
                    var pulsed = Monitor.Wait(_commitGate, remaining);
                    if (_footerSequence > sequence) return true;
                    if (_footerCount == _cycleFooterCapacity) return false;
                    if (!pulsed) return false;

                    var elapsed = Stopwatch.GetTimestamp() - startedAt;
                    if (elapsed >= timeoutStopwatchTicks) return false;
                    remaining = Math.Max(
                        1,
                        checked((int)Math.Ceiling(
                            (timeoutStopwatchTicks - elapsed) * 1000d / Stopwatch.Frequency)));
                }
            }
            finally
            {
                _offlineFooterWaiterCount--;
            }
        }
    }
}
