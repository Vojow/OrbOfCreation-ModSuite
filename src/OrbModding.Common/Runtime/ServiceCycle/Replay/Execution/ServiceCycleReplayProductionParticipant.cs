using System;
using System.Runtime.ExceptionServices;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayProductionParticipant<
    TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> :
    IServiceCycleReplayProductionParticipant
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceCycleReplayProductionSource<
        TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>? _source;
    private ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>? _registration;
    private ServiceCycleSlot<TFrame, TConfig, TState, TAction>? _slot;
    private ServiceCycleReplayStrategyGenerationSource? _strategy;

    private ServiceCycleReplayProductionParticipant(
        int traceServiceKey,
        ServiceCycleReplayExecutionResult preparation,
        ServiceCycleReplayProductionSource<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>? source,
        int cycleCount,
        ServiceCycleReplayCycleKey firstCycle)
    {
        TraceServiceKey = traceServiceKey;
        Preparation = preparation;
        _source = source;
        CycleCount = cycleCount;
        FirstCycle = firstCycle;
    }

    public int TraceServiceKey { get; }
    public int CycleCount { get; }
    public ServiceCycleReplayCycleKey FirstCycle { get; }
    public ServiceCycleReplayExecutionResult Preparation { get; }
    public bool NativeComplete => _source is not null && _source.NativeComplete;
    public bool CaptureEvidenceComplete => _source is not null && _source.CaptureEvidenceComplete;

    public void Dispose()
    {
        _registration?.Dispose();
        _registration = null;
        _slot = null;
    }

    public void DisposeAndWait(TimeSpan workerBoundaryTimeout)
    {
        var registration = _registration;
        if (registration is null) return;
        var slot = _slot ?? throw new InvalidOperationException(
            "The production replay participant did not retain its service slot.");
        ExceptionDispatchInfo? firstFailure = null;
        ExceptionDispatchInfo? firstFatal = null;
        try
        {
            registration.Dispose();
        }
        catch (Exception exception)
        {
            firstFailure = ExceptionDispatchInfo.Capture(exception);
            if (!ServiceCycleReplayContainedRunner.IsContainable(exception))
                firstFatal = firstFailure;
        }
        finally
        {
            _registration = null;
            _slot = null;
        }

        try
        {
            if (!slot.WaitForAllWorkersExited(workerBoundaryTimeout))
                throw new TimeoutException("The production replay workers did not complete cleanup.");
        }
        catch (Exception exception)
        {
            var captured = ExceptionDispatchInfo.Capture(exception);
            firstFailure ??= captured;
            if (firstFatal is null && !ServiceCycleReplayContainedRunner.IsContainable(exception))
                firstFatal = captured;
        }
        (firstFatal ?? firstFailure)?.Throw();
    }

}
