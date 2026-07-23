using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class PumpSegmentBuilder
{
    private readonly int _capacity;
    private readonly HashSet<ServiceCycleReplayCycleKey> _completed = new();
    private readonly HashSet<ServiceCycleReplayCycleKey> _queued = new();
    private readonly List<ServiceCycleReplayCycleKey> _responses = new();
    private readonly ulong[] _startSequences;
    private readonly ServiceCycleSemanticEvent?[] _actionAttempts;
    private readonly ServiceCycleSemanticEvent?[] _actionTerminals;
    private readonly ServiceCycleSemanticEvent?[] _startAttempts;
    private readonly ServiceCycleSemanticEvent?[] _startTerminals;
    private readonly ServiceCycleSemanticEvent?[] _captureStarts;
    private readonly ServiceCycleSemanticEvent?[] _captureTerminals;
    private readonly ServiceCycleSemanticEvent?[] _queuedEvents;
    private readonly List<(int Index, int Owner, ulong OwnerService, ServiceCycleSemanticEvent Control)> _controls = new();
    private int _lastCallback = -1;
    private ulong _lastCallbackService;
    private long _actionDuration;
    private long _captureDuration;
    private ServiceCycleReplayCycleKey _first;

    internal PumpSegmentBuilder(int capacity, int start)
    {
        _capacity = capacity;
        StartIndex = start;
        _startSequences = new ulong[capacity];
        _actionAttempts = new ServiceCycleSemanticEvent?[capacity];
        _actionTerminals = new ServiceCycleSemanticEvent?[capacity];
        _startAttempts = new ServiceCycleSemanticEvent?[capacity];
        _startTerminals = new ServiceCycleSemanticEvent?[capacity];
        _captureStarts = new ServiceCycleSemanticEvent?[capacity];
        _captureTerminals = new ServiceCycleSemanticEvent?[capacity];
        _queuedEvents = new ServiceCycleSemanticEvent?[capacity];
    }

    internal int StartIndex { get; }

    internal void Observe(
        ServiceCycleSemanticEvent item,
        int index,
        ServiceCycleReplayCycleKey cycle)
    {
        if (!_first.IsValid && cycle.IsValid)
            _first = cycle;

        var serviceIndex = item.Payload.Service > 0 && item.Payload.Service <= (ulong)_capacity
            ? (int)item.Payload.Service - 1
            : -1;

        if (item.Kind is ServiceCycleSemanticEventKind.StartAttempted or
            ServiceCycleSemanticEventKind.ActionAttempted or
            ServiceCycleSemanticEventKind.CaptureStarted)
        {
            _lastCallback = index;
            _lastCallbackService = item.Payload.Service;
        }

        if (item.Kind is ServiceCycleSemanticEventKind.EmergencyEntered or
            ServiceCycleSemanticEventKind.EmergencyCleared or
            ServiceCycleSemanticEventKind.LifecycleRequested or
            ServiceCycleSemanticEventKind.ConfigurationPublished or
            ServiceCycleSemanticEventKind.StrategyPublished)
        {
            _controls.Add((index, _lastCallback, _lastCallbackService, item));
        }

        if (item.Kind == ServiceCycleSemanticEventKind.CaptureCompleted && cycle.IsValid)
            _completed.Add(cycle);
        if (item.Kind == ServiceCycleSemanticEventKind.CycleQueued && cycle.IsValid)
            _queued.Add(cycle);
        if (item.Kind == ServiceCycleSemanticEventKind.CycleStarted &&
            cycle.IsValid &&
            !_responses.Contains(cycle))
        {
            _responses.Add(cycle);
        }
        if (item.Kind == ServiceCycleSemanticEventKind.StartAttempted &&
            item.Payload.Service > 0 &&
            item.Payload.Service <= (ulong)_capacity)
        {
            _startSequences[(int)item.Payload.Service - 1] = item.Id.Sequence;
        }

        if (serviceIndex >= 0)
        {
            if (item.Kind == ServiceCycleSemanticEventKind.ActionAttempted)
                _actionAttempts[serviceIndex] = item;
            else if (item.Kind is ServiceCycleSemanticEventKind.ActionCommitted or
                ServiceCycleSemanticEventKind.ActionRejected or
                ServiceCycleSemanticEventKind.ActionFaulted)
                _actionTerminals[serviceIndex] ??= item;
            else if (item.Kind == ServiceCycleSemanticEventKind.StartAttempted)
                _startAttempts[serviceIndex] = item;
            else if (item.Kind is ServiceCycleSemanticEventKind.StartReady or
                ServiceCycleSemanticEventKind.StartDeferred or
                ServiceCycleSemanticEventKind.StartFaulted)
                _startTerminals[serviceIndex] = item;
            else if (item.Kind == ServiceCycleSemanticEventKind.CaptureStarted)
                _captureStarts[serviceIndex] = item;
            else if (item.Kind is ServiceCycleSemanticEventKind.CaptureCompleted or
                ServiceCycleSemanticEventKind.CaptureUnavailable or
                ServiceCycleSemanticEventKind.CaptureFaulted)
                _captureTerminals[serviceIndex] = item;
            else if (item.Kind == ServiceCycleSemanticEventKind.CycleQueued)
                _queuedEvents[serviceIndex] = item;
        }

        if (item.Kind is ServiceCycleSemanticEventKind.ActionCommitted or
            ServiceCycleSemanticEventKind.ActionRejected or
            ServiceCycleSemanticEventKind.ActionFaulted)
        {
            _actionDuration = checked(_actionDuration + item.Payload.DurationTicks);
        }
        if (item.Kind is ServiceCycleSemanticEventKind.CaptureCompleted or
            ServiceCycleSemanticEventKind.CaptureUnavailable or
            ServiceCycleSemanticEventKind.CaptureFaulted)
        {
            _captureDuration = checked(_captureDuration + item.Payload.DurationTicks);
        }
    }

}
