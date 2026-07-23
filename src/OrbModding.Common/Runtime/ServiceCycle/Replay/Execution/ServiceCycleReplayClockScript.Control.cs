using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayClockScript
{
    internal void PrepareConstructor()
    {
        if (_semantic.Count == 0)
            throw new InvalidOperationException("Replay semantic evidence contains no constructor observation.");
        SetOwner(new MonotonicTimestamp(_semantic[0].Payload.TimestampTicks));
    }

    internal void PrepareControl(ServiceCycleReplayControlStep step, ServiceCycleReplayPumpPlan? pumpPlan = null)
    {
        EnsureOwnerComplete();
        switch (step.Kind)
        {
            case ServiceCycleReplayControlKind.LifecycleRequested:
                SetOwner(step.ObservedAt);
                break;
            case ServiceCycleReplayControlKind.EmergencyEntered:
                _emergency = true;
                SetOwner(step.ObservedAt);
                break;
            case ServiceCycleReplayControlKind.EmergencyCleared:
                _emergency = false;
                SetOwner(step.ObservedAt);
                break;
            case ServiceCycleReplayControlKind.ConfigurationPublished:
            case ServiceCycleReplayControlKind.StrategyPublished:
                SetOwner();
                break;
            case ServiceCycleReplayControlKind.PumpCompleted:
                PreparePump(step, pumpPlan);
                break;
            default:
                throw new InvalidOperationException("Replay clock received an unknown control.");
        }
    }

    private void PreparePump(ServiceCycleReplayControlStep step, ServiceCycleReplayPumpPlan? pumpPlan)
    {
        if (_plan is not null)
        {
            if (pumpPlan is null)
                throw new InvalidOperationException("Production replay pump has no prepared clock plan.");
            SetOwner(pumpPlan.CopyOwnerClock());
            return;
        }
        var pump = _semantic[step.SemanticEndIndex - 1];
        if (pump.Kind != ServiceCycleSemanticEventKind.PumpCompleted)
            throw new InvalidOperationException("Replay pump clock evidence is not terminal.");
        var payload = pump.Payload;
        if (!payload.PumpAccepted)
        {
            SetOwner(new MonotonicTimestamp(payload.TimestampTicks));
            return;
        }

        var values = new List<MonotonicTimestamp>();
        var frameEnd = new MonotonicTimestamp(payload.TimestampTicks);
        var frameStart = new MonotonicTimestamp(checked(payload.TimestampTicks - payload.TotalDurationTicks));
        var current = frameStart;
        values.Add(frameStart);
        var transitioned = new bool[_serviceCount];
        var startingOrdinal = _nextStartOrdinal;

        var responseServices = Services(step, ServiceCycleSemanticEventKind.CycleStarted);
        var responseCount = 0;
        for (var index = 0; index < responseServices.Length; index++)
            if (responseServices[index]) responseCount++;
        var responseOrdinal = 0;
        for (var offset = 0; offset < _serviceCount; offset++)
        {
            var ordinal = Ordinal(startingOrdinal, offset);
            if (!responseServices[ordinal]) continue;
            var duration = DistributedDuration(
                payload.ResponseDurationTicks, responseCount, responseOrdinal++);
            values.Add(current);
            current = new MonotonicTimestamp(checked(current.Ticks + duration));
            values.Add(current);
            transitioned[ordinal] = true;
        }

        if (_emergency)
        {
            values.Add(current);
        }
        else
        {
            AppendActionReads(step, values, transitioned, startingOrdinal, ref current);
            AppendStartReads(step, values, transitioned, startingOrdinal, ref current);
        }

        values.Add(current);
        values.Add(current);
        values.Add(frameEnd);
        _nextStartOrdinal = (_nextStartOrdinal + 1) % _serviceCount;
        SetOwner(values.ToArray());
    }

    private void AppendActionReads(
        ServiceCycleReplayControlStep step,
        List<MonotonicTimestamp> values,
        bool[] transitioned,
        int startingOrdinal,
        ref MonotonicTimestamp current)
    {
        for (var offset = 0; offset < _serviceCount; offset++)
        {
            var ordinal = Ordinal(startingOrdinal, offset);
            if (transitioned[ordinal]) continue;
            var action = FindForService(step, _artifactKeys[ordinal],
                ServiceCycleSemanticEventKind.ActionCommitted,
                ServiceCycleSemanticEventKind.ActionRejected,
                ServiceCycleSemanticEventKind.ActionFaulted);
            var attempted = FindForService(
                step, _artifactKeys[ordinal], ServiceCycleSemanticEventKind.ActionAttempted);
            var started = attempted.HasValue
                ? new MonotonicTimestamp(attempted.Value.Payload.TimestampTicks)
                : current;
            values.Add(started);
            if (action.HasValue)
            {
                var completed = new MonotonicTimestamp(action.Value.Payload.TimestampTicks);
                values.Add(new MonotonicTimestamp(checked(completed.Ticks - action.Value.Payload.DurationTicks)));
                values.Add(completed);
                current = completed;
                transitioned[ordinal] = true;
            }
            values.Add(current);
        }
    }

    private void AppendStartReads(
        ServiceCycleReplayControlStep step,
        List<MonotonicTimestamp> values,
        bool[] transitioned,
        int startingOrdinal,
        ref MonotonicTimestamp current)
    {
        for (var offset = 0; offset < _serviceCount; offset++)
        {
            var ordinal = Ordinal(startingOrdinal, offset);
            if (transitioned[ordinal]) continue;
            values.Add(current);
            var startAttempted = FindForService(
                step, _artifactKeys[ordinal], ServiceCycleSemanticEventKind.StartAttempted);
            if (!startAttempted.HasValue)
            {
                values.Add(current);
                continue;
            }
            values.Add(new MonotonicTimestamp(startAttempted.Value.Payload.TimestampTicks));
            var startTerminal = FindDirectStartTerminal(step, startAttempted.Value);
            var startCompleted = new MonotonicTimestamp(startTerminal.Payload.TimestampTicks);
            values.Add(startCompleted);
            current = startCompleted;
            if (startTerminal.Kind is ServiceCycleSemanticEventKind.StartDeferred or
                ServiceCycleSemanticEventKind.StartFaulted)
            {
                values.Add(current);
                continue;
            }
            var captureStarted = FindForService(
                step, _artifactKeys[ordinal], ServiceCycleSemanticEventKind.CaptureStarted);
            if (!captureStarted.HasValue)
            {
                values.Add(current);
                continue;
            }
            if (captureStarted.Value.Parent != startTerminal.Id)
                throw new InvalidOperationException("Ready replay start evidence has an unrelated capture child.");
            var terminal = FindForCapture(step, captureStarted.Value.Payload);
            values.Add(new MonotonicTimestamp(captureStarted.Value.Payload.TimestampTicks));
            values.Add(new MonotonicTimestamp(
                checked(terminal.Payload.TimestampTicks - terminal.Payload.DurationTicks)));
            current = new MonotonicTimestamp(terminal.Payload.TimestampTicks);
            values.Add(current);
            if (terminal.Kind == ServiceCycleSemanticEventKind.CaptureCompleted)
            {
                var queued = FindForService(
                    step, _artifactKeys[ordinal], ServiceCycleSemanticEventKind.CycleQueued);
                if (!queued.HasValue)
                    throw new InvalidOperationException("Captured replay clock evidence has no queue event.");
                current = new MonotonicTimestamp(queued.Value.Payload.TimestampTicks);
                values.Add(current);
            }
            values.Add(current);
        }
    }
}
