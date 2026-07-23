using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed class ServiceCycleReplayServiceEvidence
{
    private readonly ulong[] _configurations;
    private readonly ServiceCycleReplayCaptureAttempt[] _captures;
    private readonly ServiceCycleReplayStartAttempt[] _starts;
    private readonly ServiceCycleReplayStrategyPublication[] _strategies;

    internal ServiceCycleReplayServiceEvidence(
        ulong[] configurations,
        ServiceCycleReplayCaptureAttempt[] captures,
        ServiceCycleReplayStartAttempt[] starts,
        ServiceCycleReplayStrategyPublication[] strategies)
    {
        _configurations = configurations;
        _captures = captures;
        _starts = starts;
        _strategies = strategies;
    }

    internal int ConfigurationCount => _configurations.Length;
    internal ulong GetConfiguration(int index) => _configurations[index];
    internal int CaptureCount => _captures.Length;
    internal ServiceCycleReplayCaptureAttempt GetCapture(int index) => _captures[index];
    internal int StartCount => _starts.Length;
    internal ServiceCycleReplayStartAttempt GetStart(int index) => _starts[index];
    internal int StrategyCount => _strategies.Length;
    internal ServiceCycleReplayStrategyPublication GetStrategy(int index) => _strategies[index];
}

internal readonly struct ServiceCycleReplayCaptureAttempt
{
    internal ServiceCycleReplayCaptureAttempt(
        ServiceCycleSemanticEventKind kind,
        ServiceCycleSemanticPayload payload)
    {
        Kind = kind;
        TraceServiceKey = checked((int)payload.Service);
        Lifecycle = payload.Lifecycle;
        Configuration = payload.Configuration;
        Strategy = payload.Strategy;
        Capture = payload.Capture;
        Cycle = payload.Cycle;
        Code = payload.Code;
        Wake = payload.TryGetReturnedWake(out var wake) ? wake : default;
    }

    internal ServiceCycleSemanticEventKind Kind { get; }
    internal int TraceServiceKey { get; }
    internal ulong Lifecycle { get; }
    internal ulong Configuration { get; }
    internal ulong Strategy { get; }
    internal ulong Capture { get; }
    internal ulong Cycle { get; }
    internal int Code { get; }
    internal WakePolicy Wake { get; }
}

internal readonly struct ServiceCycleReplayStartAttempt
{
    internal ServiceCycleReplayStartAttempt(
        ServiceCycleSemanticEvent attempted,
        ServiceCycleSemanticEvent terminal)
    {
        Sequence = attempted.Id.Sequence;
        Lifecycle = attempted.Payload.Lifecycle;
        Configuration = attempted.Payload.Configuration;
        Kind = terminal.Kind;
        Code = terminal.Payload.Code;
        Wake = terminal.Payload.TryGetReturnedWake(out var wake) ? wake : default;
    }

    internal ulong Sequence { get; }
    internal ulong Lifecycle { get; }
    internal ulong Configuration { get; }
    internal ServiceCycleSemanticEventKind Kind { get; }
    internal int Code { get; }
    internal WakePolicy Wake { get; }
}

internal readonly struct ServiceCycleReplayStrategyPublication
{
    internal ServiceCycleReplayStrategyPublication(
        ulong generation,
        bool captureDerived,
        bool initial)
    {
        Generation = generation;
        IsCaptureDerived = captureDerived;
        IsInitial = initial;
    }

    internal ulong Generation { get; }
    internal bool IsCaptureDerived { get; }
    internal bool IsInitial { get; }
}
