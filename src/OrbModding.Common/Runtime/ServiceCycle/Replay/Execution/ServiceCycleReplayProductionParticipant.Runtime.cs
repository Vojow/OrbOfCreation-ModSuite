using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayProductionParticipant<
    TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>
{
    public bool TryRegister(ServiceCycleRegistry registry, ServiceCycleReplaySession recording)
    {
        if (_source is null) return false;
        TConfig initial;
        try
        {
            initial = _source.ConfigurationForInitialPublication();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        _registration = registry.RegisterReplay(_source, initial, recording);
        _slot = _registration.Slot;
        _strategy = new ServiceCycleReplayStrategyGenerationSource(_source.InitialStrategyGeneration);
        _registration.BindStrategyGenerationSource(_strategy);
        return true;
    }

    public void RegisterWorkerSchedules(
        ServiceCycleReplayClockScript clock,
        ServiceCycleReplayProductionArtifactPlan plan,
        LifecycleGeneration initialLifecycle)
    {
        if (_source is null)
            throw new InvalidOperationException("The production participant did not prepare.");
        clock.RegisterWorker(_source.ServiceId, initialLifecycle, TraceServiceKey);
        for (var index = 0; index < plan.LifecycleCount(TraceServiceKey); index++)
        {
            clock.RegisterWorker(
                _source.ServiceId,
                new LifecycleGeneration(plan.GetLifecycle(TraceServiceKey, index)),
                TraceServiceKey);
        }
    }

    public bool WaitForResponseReadyAndWorkerSettled(
        ServiceCycleReplayCycleKey expectedCycle,
        TimeSpan timeout)
    {
        if (_registration is null ||
            _source is null ||
            expectedCycle.TraceServiceKey != TraceServiceKey)
            return false;
        var identity = new ServiceCycleIdentity(
            _source.ServiceId,
            new LifecycleGeneration(expectedCycle.Lifecycle),
            new ConfigGeneration(expectedCycle.Configuration),
            new StrategyGeneration(expectedCycle.Strategy),
            new CaptureSequence(expectedCycle.Capture),
            new CycleId(expectedCycle.Cycle));
        return _registration.Slot.WaitForResponseReadyAndWorkerSettled(identity, timeout);
    }

    public bool WaitForWorkerReady(TimeSpan timeout) =>
        _registration is not null && _registration.Slot.WaitForCurrentWorkerReady(timeout);

    public void PreparePump(ServiceCycleReplayPumpPlan pump)
    {
        if (_source is null)
            throw new InvalidOperationException("The production participant did not prepare.");
        _source.PreparePump(pump);
    }

    public bool TryPublishConfiguration(ulong generation)
    {
        if (_registration is null || _source is null) return false;
        try
        {
            var current = _registration.Configuration.ReadLatest().Generation.Value;
            if (generation < current) return false;
            if (generation == current)
            {
                _source.ConfigurationFor(generation);
                return true;
            }
            var next = checked(current + 1);
            if (generation != next) return false;
            var saved = ConfigurationSaveResult<TConfig>.Saved(_source.ConfigurationFor(generation));
            _registration.Configuration.CompleteSave(in saved);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    public bool TryPublishStrategy(ulong generation)
    {
        if (_source is null || _strategy is null) return false;
        try
        {
            _source.PublishStrategy(generation, _strategy);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
