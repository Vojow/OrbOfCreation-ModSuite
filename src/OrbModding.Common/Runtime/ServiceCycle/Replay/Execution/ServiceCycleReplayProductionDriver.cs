using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Standalone typed entry point over the shared real-registry production coordinator.</summary>
public static class ServiceCycleReplayProductionDriver
{
    public static ServiceCycleReplayExecutionResult Run<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        ServiceCycleReplayArtifactDocument artifact,
        ServiceCycleReplayExecutionRegistration<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> registration,
        IServiceCycleReplayExecutionFactory<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> factory,
        TimeSpan workerBoundaryTimeout)
        where TConfig : notnull
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (registration is null) throw new ArgumentNullException(nameof(registration));
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        if (workerBoundaryTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(workerBoundaryTimeout));
        if (!ReferenceEquals(factory, registration.Factory))
            throw new InvalidOperationException("The standalone replay factory must own its typed registration.");
        return ServiceCycleReplayContainedRunner.Run(
            artifact,
            new IServiceCycleReplayExecutionRegistration?[] { registration },
            workerBoundaryTimeout);
    }
}
