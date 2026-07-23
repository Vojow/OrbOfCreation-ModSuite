using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;

internal sealed partial class ServiceCycleReplayDefinitionAdapter<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord>
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    internal void BindTraceServiceKey(int traceServiceKey)
    {
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        if (_traceServiceKey != 0 && _traceServiceKey != traceServiceKey)
            throw new InvalidOperationException("Replay registration cannot change trace service identity.");
        _traceServiceKey = traceServiceKey;
        var manifestWorker = _candidateWorker ?? _pendingWorker;
        if (manifestWorker is null)
            throw new InvalidOperationException("Replay registration has no constructed worker codec manifest.");
        BindCodecManifest(manifestWorker);
        _pendingBridge?.BindTraceServiceKey(traceServiceKey);
        _candidateBridge?.BindTraceServiceKey(traceServiceKey);
        for (var index = 0; index < _activeBridges.Length; index++)
            _activeBridges[index]?.BindTraceServiceKey(traceServiceKey);
    }

    internal void RollBackConstruction()
    {
        RollBackPendingPair();
        RollBackUnpublishedCandidate();
    }

    private ServiceCycleReplayInputBridge<TCycleInputRecord>? FindBridge(ulong lifecycle)
    {
        PruneReleasedWorkers();
        if (_candidateBridge is { } candidate && candidate.Lifecycle == lifecycle && !candidate.IsReleased)
        {
            var slot = FindVacantSlot();
            if (slot < 0)
            {
                candidate.MarkReleased();
                _candidateBridge = null;
                _candidateWorker = null;
                return null;
            }
            _activeBridges[slot] = candidate;
            _activeWorkers[slot] = _candidateWorker;
            _candidateBridge = null;
            _candidateWorker = null;
            return candidate;
        }

        for (var index = 0; index < _activeBridges.Length; index++)
        {
            var bridge = _activeBridges[index];
            if (bridge is not null && !bridge.IsReleased && bridge.Lifecycle == lifecycle)
                return bridge;
        }
        return null;
    }

    private void EnsureIndependentCodecs(ServiceCycleReplayWorker<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> candidate)
    {
        for (var index = 0; index < _activeWorkers.Length; index++)
        {
            var live = _activeWorkers[index];
            if (live is null) continue;
            if (ReferenceEquals(candidate.CycleInputCodecIdentity, live.CycleInputCodecIdentity) ||
                ReferenceEquals(candidate.StateCodecIdentity, live.StateCodecIdentity) ||
                ReferenceEquals(candidate.ActionCodecIdentity, live.ActionCodecIdentity))
                throw new InvalidOperationException("Each physical replay worker requires independent codec instances.");
        }
    }

    private void BindCodecManifest(ServiceCycleReplayWorker<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> worker) => _session.BindCodecManifest(
            _traceServiceKey,
            this,
            worker.CycleInputCodecDescriptor,
            worker.StateCodecDescriptor,
            worker.ActionCodecDescriptor);

    private bool IsActualWorkerAlias(ServiceCycleReplayWorker<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> candidate)
    {
        for (var index = 0; index < _activeWorkers.Length; index++)
        {
            if (ReferenceEquals(candidate, _activeWorkers[index])) return true;
        }
        return false;
    }

    private int FindVacantSlot()
    {
        for (var index = 0; index < _activeBridges.Length; index++)
        {
            if (_activeBridges[index] is null) return index;
        }
        return -1;
    }

    private void TrackLiveUncapturedCandidate()
    {
        var bridge = _candidateBridge;
        if (bridge is null) return;
        if (bridge.IsReleased)
        {
            _candidateBridge = null;
            _candidateWorker = null;
            return;
        }

        var slot = FindVacantSlot();
        if (slot < 0)
            throw new InvalidOperationException("Replay physical worker tracking capacity is exhausted.");
        var worker = _candidateWorker ??
            throw new InvalidOperationException("Replay candidate tracking lost its physical worker.");
        _activeBridges[slot] = bridge;
        _activeWorkers[slot] = worker;
        _candidateBridge = null;
        _candidateWorker = null;
    }

    private void PruneReleasedWorkers()
    {
        for (var index = 0; index < _activeBridges.Length; index++)
        {
            if (_activeBridges[index] is not { IsReleased: true }) continue;
            _activeBridges[index] = null;
            _activeWorkers[index] = null;
        }
    }

    private void RollBackPendingPair()
    {
        _pendingBridge?.MarkReleased();
        _pendingBridge = null;
        _pendingWorker = null;
    }

    private void RollBackUnpublishedCandidate()
    {
        if (_candidateBridge is null) return;
        _candidateBridge.MarkReleased();
        _candidateBridge = null;
        _candidateWorker = null;
    }
}
