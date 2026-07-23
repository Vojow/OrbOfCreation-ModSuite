using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

public sealed partial class ServiceRunner<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal void MarkSuperseded()
    {
        AssertOwnerThread();
        if (_disposed || _lifetime.IsSuperseded) return;
        _lifetime.MarkSuperseded(Phase);
        _starts.InvalidateLifecycle();
    }

    internal bool TryRetireForLifecycle(
        MonotonicTimestamp now,
        out ServiceRunnerRetirement retirement)
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        if (!_lifetime.IsSuperseded) MarkSuperseded();
        var acquiredCycle = default(ServiceCycleIdentity);
        var acquiredBatch = default(BatchId);
        var authoritativeResponse = default(ServiceWorkerResponse);
        var authoritativeReceipt = default(BatchReceipt);
        if (_handoff.PhaseHint == ServiceHandoffPhase.ResponseReady)
        {
            acquiredCycle = _main.InFlightCycle;
            acquiredBatch = _main.InFlightBatch;
            if (!_handoff.TryAcquireAuthoritativeTerminalResponseNonBlocking(
                    out var response,
                    out var acquired))
            {
                retirement = default;
                return false;
            }
            if (acquired)
            {
                authoritativeResponse = response;
                _responses.PublishAuthoritativeTerminal(in response, now);
                var previous = _main.PreviousReceipt;
                if (previous.IsPresent &&
                    previous.Cycle == acquiredCycle &&
                    previous.Batch == acquiredBatch)
                    authoritativeReceipt = previous;
            }
        }
        retirement = _batchCompletion.OrphanForLifecycle(
            now,
            _lifetime.SupersededPhase,
            acquiredCycle,
            acquiredBatch,
            authoritativeResponse,
            authoritativeReceipt);
        if (!_retirementSignaled)
        {
            _retirementSignaled = true;
            _disposed = true;
            _handoff.SignalStop();
        }
        return true;
    }

    public void Dispose()
    {
        AssertOwnerThread();
        if (_disposed) return;
        _main.LatestConfigGeneration =
            _configuration.ReadLatest().Generation;
        _disposed = true;
        _retirementSignaled = true;
        _starts.InvalidateLifecycle();
        _batchCompletion.ReleaseForShutdown();
        _handoff.SignalStop();
    }
}
