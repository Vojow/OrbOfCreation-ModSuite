using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Fixed-capacity deterministic numeric replay composition.</summary>
public sealed class ServiceCycleReplayExecutionCatalog
{
    private readonly int _ownerThreadId;
    private readonly IServiceCycleReplayExecutionRegistration?[] _registrations;
    private IServiceCycleReplayExecutionRegistration?[]? _sealedRegistrations;
    private int _count;
    private bool _sealed;

    public ServiceCycleReplayExecutionCatalog(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        _registrations = new IServiceCycleReplayExecutionRegistration[capacity];
    }

    public int Capacity => _registrations.Length;
    public int Count => _count;
    public bool IsSealed => _sealed;

    public void Seal()
    {
        AssertOwnerThread();
        if (_sealed) return;
        _sealedRegistrations =
            (IServiceCycleReplayExecutionRegistration?[])_registrations.Clone();
        _sealed = true;
    }

    public void Register<TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>(
        ServiceCycleReplayExecutionRegistration<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> registration)
        where TConfig : notnull
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        AssertOwnerThread();
        if (registration is null) throw new ArgumentNullException(nameof(registration));
        if (_sealed)
            throw new InvalidOperationException("The replay execution composition is sealed.");
        var index = registration.TraceServiceKey - 1;
        if ((uint)index >= (uint)_registrations.Length)
            throw new InvalidOperationException("The replay trace service key exceeds catalog capacity.");
        if (_registrations[index] is not null)
            throw new InvalidOperationException("The replay trace service key is already registered.");
        _registrations[index] = registration;
        _count++;
    }

    public ServiceCycleReplayExecutionResult VerifyEvaluators(ServiceCycleReplayArtifactDocument artifact)
    {
        AssertOwnerThread();
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (_count == 0) throw new InvalidOperationException("Replay execution requires a registration.");
        Seal();
        var registrations = _sealedRegistrations!;
        var completed = 0;
        for (var index = 0; index < registrations.Length; index++)
        {
            var registration = registrations[index];
            if (registration is null) continue;
            var result = registration.VerifyEvaluator(artifact);
            if (!result.Succeeded)
            {
                var failure = result.Failure;
                var mismatch = result.Mismatch;
                return result.Failure.IsValid
                    ? ServiceCycleReplayExecutionResult.Faulted(completed + result.CompletedCycles, in failure)
                    : ServiceCycleReplayExecutionResult.Diverged(completed + result.CompletedCycles, in mismatch);
            }
            completed += result.CompletedCycles;
        }
        return ServiceCycleReplayExecutionResult.Success(completed);
    }

    public ServiceCycleReplayExecutionResult RunProduction(
        ServiceCycleReplayArtifactDocument artifact,
        TimeSpan workerBoundaryTimeout)
    {
        AssertOwnerThread();
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (workerBoundaryTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(workerBoundaryTimeout));
        if (_count == 0) throw new InvalidOperationException("Replay execution requires a registration.");
        Seal();
        return ServiceCycleReplayContainedRunner.Run(
            artifact, _sealedRegistrations!, workerBoundaryTimeout);
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "Replay execution composition and execution must remain on its owning thread.");
    }
}
