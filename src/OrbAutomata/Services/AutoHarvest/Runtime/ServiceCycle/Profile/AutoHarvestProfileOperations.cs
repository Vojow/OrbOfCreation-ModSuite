#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbAutomata.Runtime.ServiceCycle.Profile;

internal sealed class AutoHarvestProfileOperations
{
    private readonly ServiceCycleProfileProbe _probe;
    private uint _reflectedFieldReads;
    private uint _reflectedMethodCalls;
    private uint _stableIdReads;
    private uint _listEntries;
    private uint _selectedPairs;
    private uint _readyPairs;
    private uint _invocationArgumentArrays;
    private bool _active;
    private bool _failed;

    internal AutoHarvestProfileOperations(ServiceCycleProfileProbe probe) =>
        _probe = probe ?? throw new System.ArgumentNullException(nameof(probe));

    internal AutoHarvestProfileStageScope Begin(
        int stageCode,
        in ServiceCaptureContext capture,
        ServiceCycleProfileTemperature temperature)
    {
        var coordinates = capture.ProfileCoordinates;
        return Begin(
            stageCode,
            capture.Lifecycle.Value,
            capture.Cycle.Value,
            in coordinates,
            temperature);
    }

    internal AutoHarvestProfileStageScope Begin(
        int stageCode,
        in ServiceActionContext action,
        ServiceCycleProfileTemperature temperature)
    {
        var coordinates = action.ProfileCoordinates;
        return Begin(
            stageCode,
            action.Cycle.Lifecycle.Value,
            action.Cycle.Cycle.Value,
            in coordinates,
            temperature);
    }

    private AutoHarvestProfileStageScope Begin(
        int stageCode,
        ulong lifecycle,
        ulong cycle,
        in ServiceCycleProfileCoordinates coordinates,
        ServiceCycleProfileTemperature temperature)
    {
        if (_active)
        {
            _failed = true;
            _probe.Fail(ServiceCycleProfileProbeFault.StageOverlapRejected);
            return default;
        }
        if (!coordinates.TryCreateContext(
                stageCode,
                lifecycle,
                cycle,
                temperature,
                out var context))
        {
            _probe.Fail(ServiceCycleProfileProbeFault.ContextRejected);
            return default;
        }
        var measurement = _probe.Begin(in context);
        if (!measurement.IsActive)
        {
            measurement.Abandon();
            return default;
        }
        Reset();
        _active = true;
        return new AutoHarvestProfileStageScope(this, in measurement);
    }

    internal void AddReflectedFieldRead() => Add(ref _reflectedFieldReads);
    internal void AddReflectedMethodCall() => Add(ref _reflectedMethodCalls);
    internal void AddStableIdRead() => Add(ref _stableIdReads);
    internal void AddListEntry() => Add(ref _listEntries);
    internal void AddSelectedPairs(uint count) => Add(ref _selectedPairs, count);
    internal void AddReadyPairs(uint count) => Add(ref _readyPairs, count);
    internal void AddInvocationArgumentArray() => Add(ref _invocationArgumentArrays);

    internal void Complete(ref ServiceCycleProfileStageScope measurement)
    {
        if (!_active)
        {
            measurement.Abandon();
            return;
        }
        _active = false;
        if (_failed)
        {
            measurement.Abandon();
            return;
        }
        measurement.AddReflectedFieldReads(_reflectedFieldReads);
        measurement.AddReflectedMethodCalls(_reflectedMethodCalls);
        measurement.AddStableIdReads(_stableIdReads);
        measurement.AddListEntries(_listEntries);
        measurement.AddSelectedPairs(_selectedPairs);
        measurement.AddReadyPairs(_readyPairs);
        measurement.AddInvocationArgumentArrays(_invocationArgumentArrays);
        measurement.Complete();
    }

    internal void Abandon(ref ServiceCycleProfileStageScope measurement)
    {
        if (_active) _active = false;
        measurement.Abandon();
    }

    private void Reset()
    {
        _reflectedFieldReads = 0;
        _reflectedMethodCalls = 0;
        _stableIdReads = 0;
        _listEntries = 0;
        _selectedPairs = 0;
        _readyPairs = 0;
        _invocationArgumentArrays = 0;
        _failed = false;
    }

    private void Add(ref uint value, uint count = 1)
    {
        if (!_active || _failed) return;
        if (uint.MaxValue - value < count)
        {
            _failed = true;
            _probe.Fail(ServiceCycleProfileProbeFault.OperationCounterExhausted);
            return;
        }
        value += count;
    }
}

internal ref struct AutoHarvestProfileStageScope
{
    private AutoHarvestProfileOperations? _operations;
    private ServiceCycleProfileStageScope _measurement;

    internal AutoHarvestProfileStageScope(
        AutoHarvestProfileOperations operations,
        in ServiceCycleProfileStageScope measurement)
    {
        _operations = operations;
        _measurement = measurement;
    }

    internal bool IsActive => _operations is not null;

    internal void Complete()
    {
        var operations = _operations;
        if (operations is null) return;
        _operations = null;
        operations.Complete(ref _measurement);
    }

    internal void Abandon()
    {
        var operations = _operations;
        if (operations is null) return;
        _operations = null;
        operations.Abandon(ref _measurement);
    }
}
#endif
