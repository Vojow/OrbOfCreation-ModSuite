using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleHandoff<TConfig>
    where TConfig : notnull
{
    internal bool WaitForWorkerReady(TimeSpan timeout)
    {
        GetBoundedTimeout(
            timeout,
            out var timeoutMilliseconds,
            out var timeoutStopwatchTicks);
        lock (_gate)
        {
            ThrowIfWorkerFatal();
            if (_workerWaitCount != 0) return true;
            if (_phase is ServiceHandoffPhase.Stopping or ServiceHandoffPhase.Stopped ||
                Volatile.Read(ref _stopRequested) ||
                timeoutMilliseconds == 0)
                return false;

            var startedAt = Stopwatch.GetTimestamp();
            _workerReadyWaiterCount++;
            try
            {
                if (Volatile.Read(ref _stopRequested)) return false;
                var remaining = timeoutMilliseconds;
                while (true)
                {
                    var pulsed = Monitor.Wait(_gate, remaining);
                    ThrowIfWorkerFatal();
                    if (_workerWaitCount != 0) return true;
                    if (_phase is ServiceHandoffPhase.Stopping or ServiceHandoffPhase.Stopped ||
                        Volatile.Read(ref _stopRequested) ||
                        !pulsed)
                        return false;
                    remaining = RemainingTimeoutMilliseconds(
                        startedAt,
                        timeoutStopwatchTicks);
                    if (remaining == 0) return false;
                }
            }
            finally
            {
                _workerReadyWaiterCount--;
            }
        }
    }

    internal bool WaitForResponseReady(TimeSpan timeout) =>
        WaitForResponseReadyCore(default, false, timeout);

    internal bool WaitForResponseReady(
        ServiceCycleIdentity expectedCycle,
        TimeSpan timeout)
    {
        if (!expectedCycle.IsValid)
            throw new ArgumentException(
                "A valid expected cycle is required.",
                nameof(expectedCycle));
        return WaitForResponseReadyCore(expectedCycle, false, timeout);
    }

    internal bool WaitForResponseReadyAndWorkerSettled(
        ServiceCycleIdentity expectedCycle,
        TimeSpan timeout)
    {
        if (!expectedCycle.IsValid)
            throw new ArgumentException(
                "A valid expected cycle is required.",
                nameof(expectedCycle));
        return WaitForResponseReadyCore(expectedCycle, true, timeout);
    }

    private bool WaitForResponseReadyCore(
        ServiceCycleIdentity? expectedCycle,
        bool requireWorkerSettled,
        TimeSpan timeout)
    {
        GetBoundedTimeout(
            timeout,
            out var timeoutMilliseconds,
            out var timeoutStopwatchTicks);
        lock (_gate)
        {
            ThrowIfWorkerFatal();
            if (expectedCycle.HasValue &&
                !IsExpectedCycleUnderGate(expectedCycle.Value))
                return false;
            if (ResponseReadyUnderGate(expectedCycle, requireWorkerSettled))
                return true;
            if (_phase is ServiceHandoffPhase.MainOwnedBatch or
                ServiceHandoffPhase.Stopping or ServiceHandoffPhase.Stopped ||
                Volatile.Read(ref _stopRequested) ||
                timeoutMilliseconds == 0)
                return false;

            var startedAt = Stopwatch.GetTimestamp();
            _responseWaiterCount++;
            try
            {
                if (Volatile.Read(ref _stopRequested)) return false;
                var remaining = timeoutMilliseconds;
                while (true)
                {
                    var pulsed = Monitor.Wait(_gate, remaining);
                    ThrowIfWorkerFatal();
                    if (expectedCycle.HasValue &&
                        !IsExpectedCycleUnderGate(expectedCycle.Value))
                        return false;
                    if (ResponseReadyUnderGate(expectedCycle, requireWorkerSettled))
                        return true;
                    if (_phase is ServiceHandoffPhase.MainOwnedBatch or
                        ServiceHandoffPhase.Stopping or ServiceHandoffPhase.Stopped ||
                        Volatile.Read(ref _stopRequested) ||
                        !pulsed)
                        return false;
                    remaining = RemainingTimeoutMilliseconds(
                        startedAt,
                        timeoutStopwatchTicks);
                    if (remaining == 0) return false;
                }
            }
            finally
            {
                _responseWaiterCount--;
            }
        }
    }

    private bool ResponseReadyUnderGate(
        ServiceCycleIdentity? expectedCycle,
        bool requireWorkerSettled) =>
        _phase == ServiceHandoffPhase.ResponseReady &&
        (!expectedCycle.HasValue ||
         _response.Cycle == expectedCycle.Value) &&
        (!requireWorkerSettled ||
         _workerWaitCount > _responsePublishedWorkerWaitCount);

    private bool IsExpectedCycleUnderGate(
        ServiceCycleIdentity expectedCycle) =>
        _phase switch
        {
            ServiceHandoffPhase.RequestReady or ServiceHandoffPhase.Evaluating =>
                _request.Context.Identity == expectedCycle,
            ServiceHandoffPhase.ResponseReady =>
                _response.Cycle == expectedCycle,
            _ => false,
        };

    private void PulseOfflineWaitersUnderGate()
    {
        if (_responseWaiterCount == 0 && _workerReadyWaiterCount == 0) return;
        if (_responseWaiterCount != 0) _responseWakePulseCount++;
        Monitor.PulseAll(_gate);
    }

    private static void GetBoundedTimeout(
        TimeSpan timeout,
        out int timeoutMilliseconds,
        out long timeoutStopwatchTicks)
    {
        if (timeout < TimeSpan.Zero ||
            timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "A finite bounded timeout is required.");
        }
        timeoutMilliseconds = timeout == TimeSpan.Zero
            ? 0
            : checked((int)Math.Ceiling(timeout.TotalMilliseconds));
        timeoutStopwatchTicks = timeout == TimeSpan.Zero
            ? 0L
            : checked((long)Math.Ceiling(
                timeout.TotalSeconds * Stopwatch.Frequency));
    }

    private static int RemainingTimeoutMilliseconds(
        long startedAt,
        long timeoutStopwatchTicks)
    {
        var remainingTicks =
            timeoutStopwatchTicks - (Stopwatch.GetTimestamp() - startedAt);
        return remainingTicks <= 0
            ? 0
            : Math.Max(
                1,
                checked((int)Math.Ceiling(
                    remainingTicks * 1000d / Stopwatch.Frequency)));
    }
}
