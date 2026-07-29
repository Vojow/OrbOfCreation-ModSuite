using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Builds cycle lifecycle payloads.</summary>
internal readonly struct ServiceCycleSemanticCycleEmitter
{
    private readonly ServiceCycleSemanticCausalWriter _writer;
    private readonly bool _enabled;

    internal ServiceCycleSemanticCycleEmitter(ServiceCycleSemanticCausalWriter writer, bool enabled)
    {
        _writer = writer;
        _enabled = enabled;
    }

    internal void CycleQueued(
        int ordinal,
        in ServiceCycleIdentity cycle,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (!decision.IsValid || !decision.ShouldStart)
            throw new ArgumentException("A queued cycle requires a ready start decision.", nameof(decision));
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.CycleFact(
            in traceCycle,
            decision.Code.Value,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendQueuedCycle(ordinal, in traceCycle, in payload);
    }

    internal void CycleStarted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.CycleFact(
            in traceCycle,
            0,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendCycleStarted(ordinal, in traceCycle, in payload);
    }

    internal void CycleCompleted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        Cycle(ordinal, in cycle, ServiceCycleSemanticEventKind.CycleCompleted, 0, observedAt, duration);

    internal void CycleOrphaned(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        Cycle(
            ordinal,
            in cycle,
            ServiceCycleSemanticEventKind.CycleOrphaned,
            CommonActionResultCodes.LifecycleReplaced.Value,
            observedAt,
            duration);

    internal void CycleFaulted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (!fault.IsValid)
            throw new ArgumentException("A valid service fault is required.", nameof(fault));
        Cycle(
            ordinal,
            in cycle,
            ServiceCycleSemanticEventKind.CycleFaulted,
            fault.Code.Value,
            observedAt,
            duration);
    }

    private void Cycle(
        int ordinal,
        in ServiceCycleIdentity cycle,
        ServiceCycleSemanticEventKind kind,
        int code,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.CycleFact(
            in traceCycle,
            code,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendService(ordinal, kind, in payload);
    }
}
