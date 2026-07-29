using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

internal sealed partial class ServiceCycleSemanticCausalWriter
{
    private readonly ServiceCycleTraceEventId[] _queuedCycleAnchors;
    private readonly ServiceCycleTraceCycleIdentity[] _queuedCycles;
    private readonly ServiceCycleTraceEventId[] _captureAnchors;
    private readonly ServiceCycleTraceCaptureIdentity[] _captures;
    private readonly ServiceCycleTraceEventId[] _captureTerminalAnchors;
    private readonly ServiceCycleTraceCaptureIdentity[] _captureTerminalIdentities;
    private readonly ServiceCycleTraceEventId[] _startAnchors;
    private readonly ulong[] _startLifecycles;
    private readonly ulong[] _startConfigurations;
    private readonly ServiceCycleTraceEventId[] _actionAttemptAnchors;
    private readonly ServiceActionContext[] _actionAttempts;

    internal void AppendQueuedCycle(
        int ordinal,
        in ServiceCycleTraceCycleIdentity cycle,
        in ServiceCycleSemanticPayload payload)
    {
        var terminal = _captureTerminalAnchors[ordinal];
        var parent = terminal.IsValid && Matches(_captureTerminalIdentities[ordinal], in cycle)
            ? terminal
            : _serviceHeads[ordinal];
        _captureTerminalAnchors[ordinal] = default;
        _captureTerminalIdentities[ordinal] = default;
        _queuedCycleAnchors[ordinal] = AppendService(
            ordinal,
            ServiceCycleSemanticEventKind.CycleQueued,
            in payload,
            parent);
        _queuedCycles[ordinal] = cycle;
    }

    private static bool Matches(
        in ServiceCycleTraceCaptureIdentity capture,
        in ServiceCycleTraceCycleIdentity cycle) =>
        capture.Service == cycle.Service &&
        capture.LifecycleGeneration == cycle.LifecycleGeneration &&
        capture.ConfigurationGeneration == cycle.ConfigurationGeneration &&
        capture.CycleId == cycle.CycleId;

    internal void AppendCycleStarted(
        int ordinal,
        in ServiceCycleTraceCycleIdentity cycle,
        in ServiceCycleSemanticPayload payload)
    {
        var anchor = _queuedCycleAnchors[ordinal];
        if (!anchor.IsValid || _queuedCycles[ordinal] != cycle)
            throw new InvalidOperationException("The delayed worker cycle has no matching queued-cycle anchor.");
        AppendService(ordinal, ServiceCycleSemanticEventKind.CycleStarted, in payload, anchor);
        _queuedCycleAnchors[ordinal] = default;
        _queuedCycles[ordinal] = default;
    }

    internal void AppendCaptureStarted(
        int ordinal,
        in ServiceCycleTraceCaptureIdentity capture,
        in ServiceCycleSemanticPayload payload)
    {
        _captureTerminalAnchors[ordinal] = default;
        _captureTerminalIdentities[ordinal] = default;
        _captureAnchors[ordinal] = AppendService(
            ordinal, ServiceCycleSemanticEventKind.CaptureStarted, in payload);
        _captures[ordinal] = capture;
    }

    internal void AppendCaptureTerminal(
        int ordinal,
        in ServiceCycleTraceCaptureIdentity capture,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        var anchor = _captureAnchors[ordinal];
        if (!anchor.IsValid || _captures[ordinal] != capture)
            throw new InvalidOperationException("The capture result has no matching started-capture anchor.");
        var terminal = AppendService(ordinal, kind, in payload, anchor);
        _captureAnchors[ordinal] = default;
        _captures[ordinal] = default;
        _captureTerminalAnchors[ordinal] = terminal;
        _captureTerminalIdentities[ordinal] = capture;
    }

    internal void AppendStartAttempted(
        int ordinal,
        ulong lifecycle,
        ulong configuration,
        in ServiceCycleSemanticPayload payload)
    {
        if (_startAnchors[ordinal].IsValid)
            throw new InvalidOperationException("The prior start attempt has no terminal fact.");
        _startAnchors[ordinal] = AppendService(
            ordinal, ServiceCycleSemanticEventKind.StartAttempted, in payload);
        _startLifecycles[ordinal] = lifecycle;
        _startConfigurations[ordinal] = configuration;
    }

    internal void AppendStartTerminal(
        int ordinal,
        ulong lifecycle,
        ulong configuration,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        var start = TakeStartAnchor(ordinal, lifecycle, configuration);
        AppendService(ordinal, kind, in payload, start);
    }

    private ServiceCycleTraceEventId TakeStartAnchor(
        int ordinal,
        ulong lifecycle,
        ulong configuration)
    {
        var anchor = _startAnchors[ordinal];
        if (!anchor.IsValid || _startLifecycles[ordinal] != lifecycle ||
            _startConfigurations[ordinal] != configuration)
            throw new InvalidOperationException("The start terminal has no matching attempted-start anchor.");
        _startAnchors[ordinal] = default;
        _startLifecycles[ordinal] = 0;
        _startConfigurations[ordinal] = 0;
        return anchor;
    }

    internal void AppendActionAttempted(
        int ordinal,
        in ServiceActionContext context,
        in ServiceCycleSemanticPayload payload)
    {
        _actionAttemptAnchors[ordinal] = AppendService(
            ordinal,
            ServiceCycleSemanticEventKind.ActionAttempted,
            in payload);
        _actionAttempts[ordinal] = context;
    }

    internal void AppendActionTerminal(
        int ordinal,
        in ServiceActionContext context,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        var anchor = _actionAttemptAnchors[ordinal];
        var attempt = _actionAttempts[ordinal];
        if (!anchor.IsValid || attempt.Cycle != context.Cycle || attempt.Batch != context.Batch ||
            attempt.Action != context.Action || attempt.ActionIndex != context.ActionIndex)
            throw new InvalidOperationException("The action result has no matching attempted-action anchor.");
        AppendService(ordinal, kind, in payload, anchor);
        _actionAttemptAnchors[ordinal] = default;
        _actionAttempts[ordinal] = default;
    }
}
