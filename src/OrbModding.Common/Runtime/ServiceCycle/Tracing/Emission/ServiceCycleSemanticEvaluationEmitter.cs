using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Builds evaluation and state-projection payloads.</summary>
internal readonly struct ServiceCycleSemanticEvaluationEmitter
{
    private readonly ServiceCycleSemanticCausalWriter _writer;
    private readonly bool _enabled;

    internal ServiceCycleSemanticEvaluationEmitter(ServiceCycleSemanticCausalWriter writer, bool enabled)
    {
        _writer = writer;
        _enabled = enabled;
    }

    internal void EvaluationStarted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt) =>
        Evaluation(
            ordinal,
            in cycle,
            ServiceCycleSemanticEventKind.EvaluationStarted,
            0,
            0,
            observedAt,
            default);

    internal void EvaluationCompleted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        int actionCount,
        WakePolicy returnedWake,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.EvaluationCompleted(
            in traceCycle,
            actionCount,
            returnedWake,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendService(ordinal, ServiceCycleSemanticEventKind.EvaluationCompleted, in payload);
    }

    internal void EvaluationFaulted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        EnsureFault(in fault);
        Evaluation(
            ordinal,
            in cycle,
            ServiceCycleSemanticEventKind.EvaluationFaulted,
            fault.Code.Value,
            0,
            observedAt,
            duration);
    }

    internal void ProjectionFaulted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        int actionCount,
        WakePolicy returnedWake,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        EnsureFault(in fault);
        if (fault.Category != ServiceFaultCategory.StateProjection)
            throw new ArgumentException("Projection-fault evidence requires a state-projection fault.", nameof(fault));
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.ProjectionFaulted(
            in traceCycle,
            fault.Code.Value,
            actionCount,
            returnedWake,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendService(ordinal, ServiceCycleSemanticEventKind.ProjectionFaulted, in payload);
    }

    internal void EvaluationDeferred(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration,
        MonotonicTimestamp retryDue)
    {
        if (!_enabled) return;
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.EvaluationDeferred(
            in traceCycle,
            CommonServiceDecisionCodes.TransientContention.Value,
            observedAt.Ticks,
            duration.Ticks,
            retryDue.Ticks);
        _writer.AppendService(ordinal, ServiceCycleSemanticEventKind.EvaluationDeferred, in payload);
    }

    internal void StatePublished(int ordinal, in ServiceProjectionPublication publication)
    {
        if (!_enabled) return;
        if (!publication.IsPresent)
            throw new ArgumentException("A state publication is required.", nameof(publication));
        var cycle = publication.Context.Cycle;
        var snapshot = publication.Snapshot;
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var fingerprint = ServiceCycleProjectionFingerprint.Compute(in snapshot);
        var payload = ServiceCycleSemanticPayload.State(
            in traceCycle,
            publication.Context.Publication.Value,
            fingerprint,
            publication.Context.ProjectedAt.Ticks);
        _writer.AppendService(ordinal, ServiceCycleSemanticEventKind.StatePublished, in payload);
    }

    private void Evaluation(
        int ordinal,
        in ServiceCycleIdentity cycle,
        ServiceCycleSemanticEventKind kind,
        int code,
        int actionCount,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.Evaluation(
            in traceCycle,
            code,
            actionCount,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendService(ordinal, kind, in payload);
    }

    private static void EnsureFault(in ServiceFault fault)
    {
        if (!fault.IsValid)
            throw new ArgumentException("A valid service fault is required.", nameof(fault));
    }
}
