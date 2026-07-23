using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

public readonly struct ServiceCycleReplayTypedArtifactResult<
    TCycleInputRecord,
    TStateRecord,
    TActionRecord>
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord>[] _cycles;

    internal ServiceCycleReplayTypedArtifactResult(
        ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord>[] cycles)
    {
        _cycles = cycles;
        Failure = default;
        Succeeded = true;
    }

    internal ServiceCycleReplayTypedArtifactResult(ServiceCycleReplayCycleFailure failure)
    {
        _cycles = Array.Empty<ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord>>();
        Failure = failure;
        Succeeded = false;
    }

    public bool Succeeded { get; }
    public int CycleCount => _cycles?.Length ?? 0;

    public ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord> GetCycle(int index)
    {
        if ((uint)index >= (uint)CycleCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _cycles[index];
    }

    public ServiceCycleReplayCycleFailure Failure { get; }
}
