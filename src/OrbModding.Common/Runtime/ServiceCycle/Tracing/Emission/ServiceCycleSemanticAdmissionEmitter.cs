using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Builds start-admission and capture payloads.</summary>
internal readonly struct ServiceCycleSemanticAdmissionEmitter
{
    private readonly ServiceCycleSemanticCausalWriter _writer;
    private readonly bool _enabled;

    internal ServiceCycleSemanticAdmissionEmitter(ServiceCycleSemanticCausalWriter writer, bool enabled)
    {
        _writer = writer;
        _enabled = enabled;
    }

    internal void CaptureStarted(int ordinal, in ServiceCaptureContext capture)
    {
        if (!_enabled) return;
        var traceCapture = _writer.TraceCapture(ordinal, in capture);
        var payload = ServiceCycleSemanticPayload.CaptureFact(
            in traceCapture,
            0,
            0,
            capture.CapturedAt.Ticks,
            0);
        _writer.AppendCaptureStarted(ordinal, in traceCapture, in payload);
    }

    internal void StartAttempted(
        int ordinal,
        in ServiceCycleStartContext context,
        MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        var service = _writer.Identities.ForRegistrationOrdinal(ordinal);
        var payload = ServiceCycleSemanticPayload.StartAttempted(
            service, context.Lifecycle.Value, context.LatestConfig.Value, observedAt.Ticks);
        _writer.AppendStartAttempted(
            ordinal, context.Lifecycle.Value, context.LatestConfig.Value, in payload);
    }

    internal void StartDeferred(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (!decision.IsValid || decision.ShouldStart)
            throw new ArgumentException("A waiting start decision is required.", nameof(decision));
        var service = _writer.Identities.ForRegistrationOrdinal(ordinal);
        var payload = ServiceCycleSemanticPayload.StartDeferred(
            service,
            context.Lifecycle.Value,
            context.LatestConfig.Value,
            decision.Code.Value,
            decision.WakePolicy,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendStartTerminal(
            ordinal, context.Lifecycle.Value, context.LatestConfig.Value,
            ServiceCycleSemanticEventKind.StartDeferred, in payload);
    }

    internal void StartReady(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (!decision.IsValid || !decision.ShouldStart)
            throw new ArgumentException("A ready start decision is required.", nameof(decision));
        var service = _writer.Identities.ForRegistrationOrdinal(ordinal);
        var payload = ServiceCycleSemanticPayload.StartReady(
            service,
            context.Lifecycle.Value,
            context.LatestConfig.Value,
            decision.Code.Value,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendStartTerminal(
            ordinal, context.Lifecycle.Value, context.LatestConfig.Value,
            ServiceCycleSemanticEventKind.StartReady, in payload);
    }

    internal void StartFaulted(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration,
        MonotonicTimestamp retryDue)
    {
        if (!_enabled) return;
        EnsureFault(in fault);
        var service = _writer.Identities.ForRegistrationOrdinal(ordinal);
        var payload = ServiceCycleSemanticPayload.StartFaulted(
            service,
            context.Lifecycle.Value,
            context.LatestConfig.Value,
            fault.Code.Value,
            (int)fault.Category,
            fault.OccurrenceCount,
            observedAt.Ticks,
            duration.Ticks,
            retryDue.Ticks);
        _writer.AppendStartTerminal(
            ordinal, context.Lifecycle.Value, context.LatestConfig.Value,
            ServiceCycleSemanticEventKind.StartFaulted, in payload);
    }

    internal void CaptureCompleted(
        int ordinal,
        in ServiceCaptureContext capture,
        in ServiceCaptureResult result,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (!result.IsValid || !result.IsCaptured)
            throw new ArgumentException("A completed capture requires a captured result.", nameof(result));
        Capture(
            ordinal,
            in capture,
            ServiceCycleSemanticEventKind.CaptureCompleted,
            result.StrategyGeneration.Value,
            result.Code.Value,
            observedAt,
            duration);
    }

    internal void CaptureUnavailable(
        int ordinal,
        in ServiceCaptureContext capture,
        in ServiceCaptureResult result,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (!result.IsValid || result.Disposition != ServiceCaptureDisposition.Unavailable)
            throw new ArgumentException("An unavailable capture requires an unavailable result.", nameof(result));
        var traceCapture = _writer.TraceCapture(ordinal, in capture);
        var payload = ServiceCycleSemanticPayload.CaptureUnavailable(
            in traceCapture,
            result.Code.Value,
            result.WakePolicy,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendCaptureTerminal(
            ordinal, in traceCapture, ServiceCycleSemanticEventKind.CaptureUnavailable, in payload);
    }

    internal void CaptureFaulted(
        int ordinal,
        in ServiceCaptureContext capture,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        EnsureFault(in fault);
        Capture(
            ordinal,
            in capture,
            ServiceCycleSemanticEventKind.CaptureFaulted,
            0,
            fault.Code.Value,
            observedAt,
            duration);
    }

    private void Capture(
        int ordinal,
        in ServiceCaptureContext capture,
        ServiceCycleSemanticEventKind kind,
        ulong strategyGeneration,
        int code,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        var traceCapture = _writer.TraceCapture(ordinal, in capture);
        var payload = ServiceCycleSemanticPayload.CaptureFact(
            in traceCapture,
            strategyGeneration,
            code,
            observedAt.Ticks,
            duration.Ticks);
        _writer.AppendCaptureTerminal(ordinal, in traceCapture, kind, in payload);
    }

    private static void EnsureFault(in ServiceFault fault)
    {
        if (!fault.IsValid)
            throw new ArgumentException("A valid service fault is required.", nameof(fault));
    }
}
