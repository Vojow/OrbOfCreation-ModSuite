using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using ArtifactCodecRole = OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// One immutable, artifact-wide execution index. Production preflight and every replay component
/// consume this snapshot; none independently rescan the artifact or semantic document.
/// </summary>
internal sealed partial class ServiceCycleReplayProductionArtifactPlan
{
    private readonly ServiceCycleReplayArtifactDocument _artifact;
    private readonly byte[] _codecRoles;
    private readonly int[][] _cycleIndices;
    private readonly ServiceCycleReplayCycleKey[] _firstCycles;
    private readonly ulong[][] _lifecycles;
    private readonly ServiceCycleReplayNativeOutcome[][] _nativeOutcomes;
    private readonly ServiceCycleReplayServiceEvidence[] _services;
    private readonly ServiceCycleReplayPumpPlan[] _pumps;
    private readonly ServiceCycleReplayControlStep[] _controls;
    private readonly Dictionary<WorkerKey, MonotonicTimestamp[]> _workerSchedules;

    internal ServiceCycleReplayProductionArtifactPlan(ServiceCycleReplayArtifactDocument artifact)
    {
        _artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        Capacity = Math.Max(0, artifact.SemanticTrace.ServiceCapacity);
        _codecRoles = new byte[Capacity];
        var cycleLists = NewLists<int>(Capacity);
        var lifecycleSets = NewSets<ulong>(Capacity);
        var nativeLists = NewLists<ServiceCycleReplayNativeOutcome>(Capacity);
        _firstCycles = new ServiceCycleReplayCycleKey[Capacity];

        for (var index = 0; index < artifact.CodecCount; index++)
        {
            CodecVisitCount++;
            var codec = artifact.GetCodec(index);
            var serviceIndex = codec.TraceServiceKey - 1;
            if ((uint)serviceIndex >= (uint)Capacity) continue;
            var role = codec.Role switch
            {
                ArtifactCodecRole.CycleInput => (byte)1,
                ArtifactCodecRole.State => (byte)2,
                ArtifactCodecRole.Action => (byte)4,
                _ => (byte)0,
            };
            _codecRoles[serviceIndex] = role == 0 || (_codecRoles[serviceIndex] & role) != 0
                ? (byte)0xff
                : (byte)(_codecRoles[serviceIndex] | role);
        }

        for (var index = 0; index < artifact.CycleCount; index++)
        {
            CycleVisitCount++;
            var key = artifact.GetCycle(index).Key;
            var serviceIndex = key.TraceServiceKey - 1;
            if ((uint)serviceIndex >= (uint)Capacity) continue;
            cycleLists[serviceIndex].Add(index);
            lifecycleSets[serviceIndex].Add(key.Lifecycle);
            if (!_firstCycles[serviceIndex].IsValid) _firstCycles[serviceIndex] = key;
        }

        var configurations = NewLists<ulong>(Capacity);
        var captures = NewLists<ServiceCycleReplayCaptureAttempt>(Capacity);
        var starts = NewLists<MutableStartAttempt>(Capacity);
        var startIndices = new Dictionary<ServiceCycleTraceEventId, StartLocation>();
        var strategyDrafts = NewLists<StrategyDraft>(Capacity);
        var captureStarts = new HashSet<ServiceCycleTraceEventId>();
        var captureCompletions = new HashSet<CapturePublicationIdentity>();
        var initialLifecycles = new ulong[Capacity];
        var sawCycleIdentity = new bool[Capacity];
        var workerSchedules = new Dictionary<WorkerKey, List<MonotonicTimestamp>>();
        var controlDrafts = new List<ControlDraft>();
        var pumps = new List<ServiceCycleReplayPumpPlan>();
        var segment = new PumpSegmentBuilder(Capacity, 0);
        var unsupportedLifecycle = default(ServiceCycleReplayCycleKey);
        var boundaryFailure = default(ServiceCycleReplayControlBoundaryFailure);
        var delayedPublication = default(ServiceCycleReplayCycleKey);
        var timingFailure = default(ServiceCycleReplayCycleKey);
        var scheduledLifecycle = 0UL;
        var replayEmergency = false;
        var nextStartOrdinal = 0;
        var acquiredCycles = new HashSet<ServiceCycleReplayCycleKey>();
        ControlDraft? pendingLifecycleControl = null;
        var semantic = artifact.SemanticTrace;

        for (var index = 0; index < semantic.Count; index++)
        {
            SemanticVisitCount++;
            var item = semantic[index];
            var serviceIndex = item.Payload.Service == 0 || item.Payload.Service > (ulong)Capacity
                ? -1 : checked((int)item.Payload.Service - 1);
            var hasCycle = TryCycle(item.Payload, out var cycle);
            if (hasCycle && item.Kind == ServiceCycleSemanticEventKind.CycleStarted)
                acquiredCycles.Add(cycle);
            if (hasCycle && item.Kind == ServiceCycleSemanticEventKind.CycleOrphaned &&
                pendingLifecycleControl is not null && !acquiredCycles.Contains(cycle))
                pendingLifecycleControl.AddLifecycleWait(cycle);
            if (serviceIndex >= 0 && hasCycle) sawCycleIdentity[serviceIndex] = true;

            if (serviceIndex >= 0)
            {
                var beforeInitialLifecycle = initialLifecycles[serviceIndex] == 0;
                if (!sawCycleIdentity[serviceIndex] &&
                    item.Kind == ServiceCycleSemanticEventKind.LifecycleActivated &&
                    initialLifecycles[serviceIndex] == 0)
                    initialLifecycles[serviceIndex] = item.Payload.Lifecycle;
                IndexServiceEvent(
                    item, serviceIndex, configurations, captures, starts, startIndices,
                    strategyDrafts, captureStarts, captureCompletions, workerSchedules, nativeLists,
                    beforeInitialLifecycle);
            }

            if (!unsupportedLifecycle.IsValid && IsUnsupportedLifecycleConstruction(item))
                unsupportedLifecycle = hasCycle ? cycle : FirstCycle(serviceIndex + 1, StableFailureCycle);

            segment.Observe(item, index, hasCycle ? cycle : default);
            if (item.Kind == ServiceCycleSemanticEventKind.EmergencyEntered) replayEmergency = true;
            else if (item.Kind == ServiceCycleSemanticEventKind.EmergencyCleared) replayEmergency = false;
            if (TryControlKind(item.Kind, out var controlKind))
            {
                if (controlKind != ServiceCycleReplayControlKind.LifecycleRequested ||
                    serviceIndex >= 0 && scheduledLifecycle != item.Payload.Lifecycle)
                {
                    if (controlKind == ServiceCycleReplayControlKind.LifecycleRequested)
                        scheduledLifecycle = item.Payload.Lifecycle;
                    var draft = new ControlDraft(item, controlKind, segment.StartIndex, index + 1);
                    controlDrafts.Add(draft);
                    if (controlKind == ServiceCycleReplayControlKind.LifecycleRequested)
                        pendingLifecycleControl = draft;
                }
            }

            if (item.Kind != ServiceCycleSemanticEventKind.PumpCompleted) continue;
            var pump = segment.Freeze(item, index + 1, nextStartOrdinal, replayEmergency, captureStarts, captureCompletions);
            pumps.Add(pump);
            if (item.Payload.PumpAccepted) nextStartOrdinal = (nextStartOrdinal + 1) % Math.Max(1, Capacity);
            if (!boundaryFailure.IsValid) boundaryFailure = pump.BoundaryFailure;
            if (!delayedPublication.IsValid) delayedPublication = pump.DelayedPublication;
            if (!timingFailure.IsValid) timingFailure = pump.TimingFailure;
            segment = new PumpSegmentBuilder(Capacity, index + 1);
        }

        _cycleIndices = Freeze(cycleLists);
        _lifecycles = Freeze(lifecycleSets);
        _nativeOutcomes = Freeze(nativeLists);
        _workerSchedules = Freeze(workerSchedules);
        _pumps = pumps.ToArray();
        _services = new ServiceCycleReplayServiceEvidence[Capacity];
        for (var index = 0; index < Capacity; index++)
        {
            PostProcessVisitCount = checked(PostProcessVisitCount + starts[index].Count + strategyDrafts[index].Count);
            var frozenStarts = new ServiceCycleReplayStartAttempt[starts[index].Count];
            for (var startIndex = 0; startIndex < frozenStarts.Length; startIndex++)
            {
                if (!starts[index][startIndex].HasTerminal)
                    throw new InvalidOperationException("A replay start attempt has no terminal evidence.");
                frozenStarts[startIndex] = starts[index][startIndex].Freeze();
            }
            var strategies = new ServiceCycleReplayStrategyPublication[strategyDrafts[index].Count];
            for (var strategyIndex = 0; strategyIndex < strategies.Length; strategyIndex++)
            {
                var draft = strategyDrafts[index][strategyIndex];
                var captureDerived = draft.Parent.IsValid && captureStarts.Contains(draft.Parent) &&
                    captureCompletions.Contains(new CapturePublicationIdentity(
                        draft.Parent, draft.Generation, draft.TimestampTicks));
                strategies[strategyIndex] = new ServiceCycleReplayStrategyPublication(
                    draft.Generation, captureDerived, !captureDerived && draft.BeforeInitialLifecycle);
            }
            _services[index] = new ServiceCycleReplayServiceEvidence(
                configurations[index].ToArray(), captures[index].ToArray(), frozenStarts, strategies);
        }

        var controls = new List<ServiceCycleReplayControlStep>(controlDrafts.Count);
        var previousTimestamp = 0L;
        var emergencyDepth = 0;
        for (var index = 0; index < controlDrafts.Count; index++)
        {
            PostProcessVisitCount++;
            var draft = controlDrafts[index];
            if (draft.Kind == ServiceCycleReplayControlKind.StrategyPublished &&
                IsCaptureDerived(draft.Item, captureStarts, captureCompletions)) continue;
            var payload = draft.Item.Payload;
            if (payload.TimestampTicks < previousTimestamp)
                throw new InvalidOperationException("Replay control timestamps are not monotonic.");
            previousTimestamp = payload.TimestampTicks;
            if (draft.Kind == ServiceCycleReplayControlKind.EmergencyEntered)
            {
                if (emergencyDepth != 0) throw new InvalidOperationException("Replay emergency controls overlap.");
                emergencyDepth = 1;
            }
            else if (draft.Kind == ServiceCycleReplayControlKind.EmergencyCleared)
            {
                if (emergencyDepth == 0) throw new InvalidOperationException("Replay emergency clear has no entered episode.");
                emergencyDepth = 0;
            }
            controls.Add(draft.ToStep());
        }
        _controls = controls.ToArray();
        InitialLifecycle = SharedInitialLifecycle(initialLifecycles);
        UnsupportedLifecycleConstruction = unsupportedLifecycle;
        ControlBoundaryFailure = boundaryFailure;
        DelayedRequestPublication = delayedPublication;
        PumpTimingFailure = timingFailure;
    }
}
