using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayVirtualClock : IMonotonicClock
{
    private MonotonicTimestamp _now;

    public ServiceCycleReplayVirtualClock(MonotonicTimestamp initial) => _now = initial;
    public MonotonicTimestamp Now => _now;

    public void AdvanceTo(MonotonicTimestamp timestamp)
    {
        if (timestamp < _now)
            throw new InvalidOperationException("Replay monotonic time cannot move backwards.");
        _now = timestamp;
    }
}

public enum ServiceCycleReplayControlKind
{
    ConfigurationPublished = 1,
    StrategyPublished = 2,
    LifecycleRequested = 3,
    EmergencyEntered = 4,
    EmergencyCleared = 5,
    PumpCompleted = 6,
}

public readonly struct ServiceCycleReplayControlStep
{
    private readonly ServiceCycleReplayCycleKey[]? _lifecycleWaitCycles;

    internal ServiceCycleReplayControlStep(
        ServiceCycleReplayControlKind kind,
        int traceServiceKey,
        ulong generation,
        long frameIdentity,
        int code,
        MonotonicTimestamp observedAt,
        int semanticStartIndex,
        int semanticEndIndex,
        ServiceCycleReplayCycleKey[]? lifecycleWaitCycles = null)
    {
        Kind = kind;
        TraceServiceKey = traceServiceKey;
        Generation = generation;
        FrameIdentity = frameIdentity;
        Code = code;
        ObservedAt = observedAt;
        SemanticStartIndex = semanticStartIndex;
        SemanticEndIndex = semanticEndIndex;
        _lifecycleWaitCycles = lifecycleWaitCycles;
    }

    public ServiceCycleReplayControlKind Kind { get; }
    public int TraceServiceKey { get; }
    public ulong Generation { get; }
    public long FrameIdentity { get; }
    public int Code { get; }
    public MonotonicTimestamp ObservedAt { get; }
    internal int SemanticStartIndex { get; }
    internal int SemanticEndIndex { get; }
    internal int LifecycleWaitCount => _lifecycleWaitCycles?.Length ?? 0;
    internal ServiceCycleReplayCycleKey GetLifecycleWait(int index) => _lifecycleWaitCycles![index];
}

/// <summary>
/// Finite semantic control program. It retains the original event order, including publication,
/// lifecycle, emergency and accepted/rejected pump boundaries; there is no timer polling step.
/// </summary>
public sealed class ServiceCycleReplayControlScript
{
    private readonly ServiceCycleReplayControlStep[] _steps;

    private ServiceCycleReplayControlScript(ServiceCycleReplayControlStep[] steps) => _steps = steps;

    internal static ServiceCycleReplayControlScript FromPlan(ServiceCycleReplayProductionArtifactPlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        var steps = new ServiceCycleReplayControlStep[plan.ControlCount];
        for (var index = 0; index < steps.Length; index++) steps[index] = plan.GetControl(index);
        return new ServiceCycleReplayControlScript(steps);
    }

    public int Count => _steps.Length;
    public ServiceCycleReplayControlStep this[int index] => _steps[index];

    public static ServiceCycleReplayControlScript FromArtifact(ServiceCycleReplayArtifactDocument artifact)
        => FromArtifact(artifact, null);

    internal static ServiceCycleReplayControlScript FromArtifact(
        ServiceCycleReplayArtifactDocument artifact,
        int[]? replayTraceServiceKeys)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (!artifact.IsComplete)
            throw new InvalidOperationException("Only a complete joined replay artifact can produce a control script.");
        var semantic = artifact.SemanticTrace;
        var count = 0;
        ulong scheduledLifecycle = 0;
        for (var index = 0; index < semantic.Count; index++)
            if (IncludeControl(semantic[index], replayTraceServiceKeys, ref scheduledLifecycle)) count++;
        var steps = new ServiceCycleReplayControlStep[count];
        var output = 0;
        var previousTimestamp = 0L;
        var emergencyDepth = 0;
        var semanticStartIndex = 0;
        scheduledLifecycle = 0;
        for (var index = 0; index < semantic.Count; index++)
        {
            var item = semantic[index];
            if (!IncludeControl(item, replayTraceServiceKeys, ref scheduledLifecycle) ||
                !TryKind(item.Kind, out var kind)) continue;
            var payload = item.Payload;
            if (payload.TimestampTicks < previousTimestamp)
                throw new InvalidOperationException("Replay control timestamps are not monotonic.");
            previousTimestamp = payload.TimestampTicks;
            if (kind == ServiceCycleReplayControlKind.EmergencyEntered)
            {
                if (emergencyDepth != 0)
                    throw new InvalidOperationException("Replay emergency controls overlap.");
                emergencyDepth = 1;
            }
            else if (kind == ServiceCycleReplayControlKind.EmergencyCleared)
            {
                if (emergencyDepth == 0)
                    throw new InvalidOperationException("Replay emergency clear has no entered episode.");
                emergencyDepth = 0;
            }
            steps[output++] = new ServiceCycleReplayControlStep(
                kind,
                checked((int)payload.Service),
                kind switch
                {
                    ServiceCycleReplayControlKind.ConfigurationPublished => payload.Configuration,
                    ServiceCycleReplayControlKind.StrategyPublished => payload.Strategy,
                    ServiceCycleReplayControlKind.LifecycleRequested => payload.Lifecycle,
                    _ => 0,
                },
                payload.FrameIdentity,
                payload.Code,
                new MonotonicTimestamp(payload.TimestampTicks),
                semanticStartIndex,
                index + 1);
            if (kind == ServiceCycleReplayControlKind.PumpCompleted)
                semanticStartIndex = index + 1;
        }
        return new ServiceCycleReplayControlScript(steps);
    }

    private static bool IncludeControl(
        ServiceCycleSemanticEvent item,
        int[]? replayTraceServiceKeys,
        ref ulong scheduledLifecycle)
    {
        if (!TryKind(item.Kind, out var kind)) return false;
        if (kind is ServiceCycleReplayControlKind.ConfigurationPublished or
            ServiceCycleReplayControlKind.StrategyPublished)
            return IncludesService(item.Payload.Service, replayTraceServiceKeys);
        if (kind != ServiceCycleReplayControlKind.LifecycleRequested) return true;
        if (!IncludesService(item.Payload.Service, replayTraceServiceKeys) ||
            scheduledLifecycle == item.Payload.Lifecycle) return false;
        scheduledLifecycle = item.Payload.Lifecycle;
        return true;
    }

    private static bool IncludesService(ulong service, int[]? replayTraceServiceKeys)
    {
        if (replayTraceServiceKeys is null) return true;
        for (var index = 0; index < replayTraceServiceKeys.Length; index++)
            if (service == (ulong)replayTraceServiceKeys[index]) return true;
        return false;
    }

    private static bool TryKind(
        ServiceCycleSemanticEventKind semantic,
        out ServiceCycleReplayControlKind control)
    {
        control = semantic switch
        {
            ServiceCycleSemanticEventKind.ConfigurationPublished =>
                ServiceCycleReplayControlKind.ConfigurationPublished,
            ServiceCycleSemanticEventKind.StrategyPublished =>
                ServiceCycleReplayControlKind.StrategyPublished,
            ServiceCycleSemanticEventKind.LifecycleRequested =>
                ServiceCycleReplayControlKind.LifecycleRequested,
            ServiceCycleSemanticEventKind.EmergencyEntered =>
                ServiceCycleReplayControlKind.EmergencyEntered,
            ServiceCycleSemanticEventKind.EmergencyCleared =>
                ServiceCycleReplayControlKind.EmergencyCleared,
            ServiceCycleSemanticEventKind.PumpCompleted =>
                ServiceCycleReplayControlKind.PumpCompleted,
            _ => default,
        };
        return control != default;
    }
}
