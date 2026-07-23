using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleWorker<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    private void Run()
    {
        Volatile.Write(
            ref _managedThreadId,
            Thread.CurrentThread.ManagedThreadId);
        ExceptionDispatchInfo? fatal = null;
        try
        {
            while (true)
            {
                var work = _handoff.WaitForWorkerWork(
                    out var request,
                    out var clearFrom,
                    out var clearCount);
                if (work == ServiceWorkerWorkKind.Stop) break;
                if (work == ServiceWorkerWorkKind.ClearRejectedSuffix)
                {
                    _actions.ClearRejectedSuffixOnWorker(clearFrom, clearCount);
                    _handoff.AcknowledgeCleanup(
                        Thread.CurrentThread.ManagedThreadId);
                    continue;
                }

                var before = _measureAllocations
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0;
                var continueRunning = Evaluate(in request);
                if (_measureAllocations)
                {
                    Interlocked.Exchange(
                        ref _lastCycleAllocatedBytes,
                        GC.GetAllocatedBytesForCurrentThread() - before);
                    Interlocked.Increment(ref _measuredCycleCount);
                }
                if (!continueRunning) break;
            }
        }
        catch (Exception exception) when (
            ServiceCycleFatalExceptionPolicy.MustEscape(_definition, exception))
        {
            fatal = ExceptionDispatchInfo.Capture(exception);
            _handoff.PublishWorkerFatal(fatal);
        }
        finally
        {
            _actions.AbortWorkerWrite();
            var stateReleaseFatal = _workerState.ReleaseForShutdown();
            if (stateReleaseFatal is not null)
                CaptureFirstFatal(ref fatal, stateReleaseFatal);
            var frame = _frame.Value;
            _frame.Value = default!;
            try
            {
                _definition.ReleaseFrame(ref frame);
            }
            catch (Exception exception) when (
                ServiceCycleFatalExceptionPolicy.MustEscape(
                    _definition,
                    exception))
            {
                CaptureFirstFatal(ref fatal, exception);
            }
            catch
            {
            }
            finally
            {
                frame = default!;
                _resourceClaims.Release(_frameClaim);
                _resourceClaims.Release(_workerDefinitionClaim);
            }
            _handoff.PrepareWorkerExit(fatal);
            try
            {
                _exitObserver?.OnWorkerExitPrepared();
            }
            catch
            {
            }
        }
    }

    private void CaptureFirstFatal(
        ref ExceptionDispatchInfo? fatal,
        Exception exception)
    {
        if (fatal is not null) return;
        fatal = ExceptionDispatchInfo.Capture(exception);
        _handoff.PublishWorkerFatal(fatal);
    }
}
