using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal sealed partial class ServiceCycleSlot<TState, TAction>
{
    internal bool WaitForResponseReady(
        ServiceCycleIdentity expectedCycle,
        TimeSpan timeout)
    {
        ThrowIfDisposed();
        if (expectedCycle.Service != ServiceId) return false;
        if (_position0.OwnsLifecycle(expectedCycle.Lifecycle))
            return _position0.WaitForResponseReady(expectedCycle, timeout);
        return _position1.OwnsLifecycle(expectedCycle.Lifecycle) &&
            _position1.WaitForResponseReady(expectedCycle, timeout);
    }

    internal bool WaitForCurrentWorkerReady(TimeSpan timeout)
    {
        ThrowIfDisposed();
        return CurrentRunner?.WaitForWorkerReady(timeout) ?? false;
    }

    internal bool WaitForResponseReadyAndWorkerSettled(
        ServiceCycleIdentity expectedCycle,
        TimeSpan timeout)
    {
        ThrowIfDisposed();
        if (expectedCycle.Service != ServiceId) return false;
        if (_position0.OwnsLifecycle(expectedCycle.Lifecycle))
            return _position0.WaitForResponseReadyAndWorkerSettled(
                expectedCycle,
                timeout);
        return _position1.OwnsLifecycle(expectedCycle.Lifecycle) &&
            _position1.WaitForResponseReadyAndWorkerSettled(
                expectedCycle,
                timeout);
    }

    internal bool WaitForAllWorkersExited(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero ||
            timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "A finite bounded timeout is required.");
        }
        var startedAt = Stopwatch.GetTimestamp();
        ExceptionDispatchInfo? firstFailure = null;
        var allExited = true;
        try
        {
            allExited &= _position0.WaitForWorkerExit(timeout);
        }
        catch (Exception exception)
        {
            firstFailure = ExceptionDispatchInfo.Capture(exception);
        }
        try
        {
            allExited &= _position1.WaitForWorkerExit(
                Remaining(timeout, startedAt));
        }
        catch (Exception exception)
        {
            firstFailure ??= ExceptionDispatchInfo.Capture(exception);
        }
        firstFailure?.Throw();
        return allExited;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Exception? firstFailure = null;
        try
        {
            try
            {
                _position0.SignalDispose();
            }
            catch (Exception ex)
            {
                firstFailure = ex;
            }
            try
            {
                _position1.SignalDispose();
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }
        finally
        {
            // Released, not disposed: the publication belongs to the registry, which owns it for
            // the whole suite and disposes it after every slot has gone.
            _configuration = null;
        }
        if (firstFailure is not null) throw firstFailure;
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(
                nameof(ServiceCycleSlot<TState, TAction>));
    }

    private static TimeSpan Remaining(TimeSpan timeout, long startedAt)
    {
        if (timeout == TimeSpan.Zero) return TimeSpan.Zero;
        var timeoutTicks = checked(
            (long)Math.Ceiling(
                timeout.TotalSeconds * Stopwatch.Frequency));
        var remainingTicks =
            timeoutTicks - (Stopwatch.GetTimestamp() - startedAt);
        return remainingTicks <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(
                remainingTicks / (double)Stopwatch.Frequency);
    }
}
