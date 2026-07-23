using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal enum ServiceCycleWorkerStateCreationResult
{
    Created = 1,
    Rejected = 2,
    Contended = 3,
}

internal struct ServiceCycleWorkerState<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    private readonly IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> _definition;
    private readonly ServiceResourceClaimLedger _resourceClaims;
    private readonly IServiceCycleStateFactoryGate? _factoryGate;
    private TState _value;
    private ServiceResourceClaim? _claim;
    private bool _hasValue;
    private int _contentionCount;
    private long _contentionTotal;

    internal ServiceCycleWorkerState(
        IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceResourceClaimLedger resourceClaims,
        IMonotonicClock clock)
    {
        _definition = definition;
        _resourceClaims = resourceClaims;
        _factoryGate = clock as IServiceCycleStateFactoryGate;
        _value = default!;
        _claim = null;
        _hasValue = false;
        _contentionCount = 0;
        _contentionTotal = 0;
    }

    internal bool HasValue => _hasValue;
    internal long ContentionTotal => Interlocked.Read(ref _contentionTotal);

    internal static ref TState BorrowValue(
        ref ServiceCycleWorkerState<TFrame, TConfig, TState, TAction> owner) =>
        ref owner._value;

    internal ServiceCycleWorkerStateCreationResult TryCreate(LifecycleGeneration lifecycle)
    {
        ServiceResourceClaim? claim = null;
        var gated = false;
        if (!typeof(TState).IsValueType && _factoryGate is not null)
        {
            _factoryGate.EnterStateFactory();
            gated = true;
        }
        try
        {
            if (!typeof(TState).IsValueType)
            {
                var admission = _resourceClaims.TryBeginFactory(
                    ServiceResourceRole.State,
                    out claim);
                if (admission == ServiceResourceClaimResult.Contended)
                    return ServiceCycleWorkerStateCreationResult.Contended;
                if (admission != ServiceResourceClaimResult.Claimed)
                    return ServiceCycleWorkerStateCreationResult.Rejected;
                _contentionCount = 0;
            }

            var value = default(TState)!;
            var aliased = false;
            try
            {
                value = _definition.CreateState(lifecycle);
                if (!typeof(TState).IsValueType &&
                    value is not null &&
                    _resourceClaims.FinalizeFactory(claim!, (object)value) ==
                        ServiceResourceClaimResult.Aliased)
                {
                    aliased = true;
                }
            }
            finally
            {
                if (!typeof(TState).IsValueType)
                    _resourceClaims.EndFactory(claim!);
            }

            if (aliased)
            {
                claim = null;
                value = default!;
                return ServiceCycleWorkerStateCreationResult.Rejected;
            }
            if (!typeof(TState).IsValueType && value is null)
            {
                claim = null;
                return ServiceCycleWorkerStateCreationResult.Rejected;
            }
            _value = value;
            _claim = claim;
            _hasValue = true;
            return ServiceCycleWorkerStateCreationResult.Created;
        }
        finally
        {
            if (gated) _factoryGate!.ExitStateFactory();
        }
    }

    internal ServiceCycleWorkerStateCreationResult Recreate(LifecycleGeneration lifecycle)
    {
        var damaged = _value;
        var hadValue = _hasValue;
        _value = default!;
        _hasValue = false;
        if (hadValue)
        {
            var damagedClaim = _claim;
            _claim = null;
            try { _definition.ReleaseState(ref damaged); }
            catch (Exception exception) when (
                !ServiceCycleFatalExceptionPolicy.MustEscape(_definition, exception)) { }
            finally
            {
                _resourceClaims.Release(damagedClaim);
                damaged = default!;
            }
        }

        try
        {
            return TryCreate(lifecycle);
        }
        catch (Exception exception) when (
            !ServiceCycleFatalExceptionPolicy.MustEscape(_definition, exception))
        {
            ResetAfterCreationFailure();
            return ServiceCycleWorkerStateCreationResult.Rejected;
        }
    }

    internal void ResetAfterCreationFailure()
    {
        _value = default!;
        _hasValue = false;
    }

    internal Exception? ReleaseForShutdown()
    {
        if (!_hasValue) return null;
        _hasValue = false;
        var value = _value;
        var claim = _claim;
        _claim = null;
        _value = default!;
        try { _definition.ReleaseState(ref value); }
        catch (Exception exception) when (
            ServiceCycleFatalExceptionPolicy.MustEscape(_definition, exception))
        {
            return exception;
        }
        catch { }
        finally { _resourceClaims.Release(claim); }
        return null;
    }

    internal int RecordContention()
    {
        _contentionCount = Math.Min(_contentionCount + 1, 7);
        Interlocked.Increment(ref _contentionTotal);
        return Math.Min(1000, 16 << (_contentionCount - 1));
    }
}
