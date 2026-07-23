using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Immutable, service-scoped execution index built by one traversal of semantic evidence and one
/// traversal of decoded cycles. Replay callbacks use indexed lookups rather than rescanning artifacts.
/// </summary>
internal sealed class ServiceCycleReplayTypedServicePlan<TCycleInputRecord, TStateRecord, TActionRecord>
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceCycleReplayTypedArtifactResult<
        TCycleInputRecord, TStateRecord, TActionRecord> _decoded;
    private readonly Dictionary<ServiceCycleReplayCycleKey, int> _cycleIndices;
    private readonly Dictionary<ulong, int> _configurationCycleIndices;
    private readonly HashSet<ulong> _configurationGenerations;
    private readonly CaptureAttempt[] _captures;
    private readonly StartAttempt[] _starts;
    private readonly ulong[] _configurationPublications;
    private readonly StrategyPublicationAttempt[] _strategyPublications;

    internal ServiceCycleReplayTypedServicePlan(
        ServiceCycleReplayProductionArtifactPlan artifactPlan,
        int traceServiceKey,
        ServiceCycleReplayTypedArtifactResult<TCycleInputRecord, TStateRecord, TActionRecord> decoded)
    {
        if (artifactPlan is null) throw new ArgumentNullException(nameof(artifactPlan));
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        if (!decoded.Succeeded || decoded.CycleCount == 0)
            throw new ArgumentException("A successful non-empty decoded plan is required.", nameof(decoded));

        _decoded = decoded;
        _cycleIndices = new Dictionary<ServiceCycleReplayCycleKey, int>(decoded.CycleCount);
        _configurationCycleIndices = new Dictionary<ulong, int>(decoded.CycleCount);
        for (var index = 0; index < decoded.CycleCount; index++)
        {
            var cycle = decoded.GetCycle(index);
            var key = cycle.Context.Cycle;
            if (!_cycleIndices.TryAdd(key, index))
                throw new InvalidOperationException("Decoded replay cycles contain a duplicate identity.");
            _configurationCycleIndices.TryAdd(key.Configuration, index);
        }
        CycleIndexBuildOperationCount = decoded.CycleCount;

        var configurationSet = new HashSet<ulong>();
        var evidence = artifactPlan.GetService(traceServiceKey);
        _starts = new StartAttempt[evidence.StartCount];
        for (var index = 0; index < _starts.Length; index++)
            _starts[index] = new StartAttempt(evidence.GetStart(index));
        _captures = new CaptureAttempt[evidence.CaptureCount];
        for (var index = 0; index < _captures.Length; index++)
            _captures[index] = new CaptureAttempt(evidence.GetCapture(index));
        _configurationPublications = new ulong[evidence.ConfigurationCount];
        for (var index = 0; index < _configurationPublications.Length; index++)
        {
            _configurationPublications[index] = evidence.GetConfiguration(index);
            configurationSet.Add(_configurationPublications[index]);
        }
        _strategyPublications = new StrategyPublicationAttempt[evidence.StrategyCount];
        for (var index = 0; index < _strategyPublications.Length; index++)
            _strategyPublications[index] = new StrategyPublicationAttempt(evidence.GetStrategy(index));
        _configurationGenerations = configurationSet;
        InitialStrategyGeneration = FindInitialStrategy(_strategyPublications);
    }

    internal int CycleCount => _decoded.CycleCount;
    internal int CaptureCount => _captures.Length;
    internal int StartCount => _starts.Length;
    internal int StrategyPublicationCount => _strategyPublications.Length;
    internal int ConfigurationPublicationCount => _configurationPublications.Length;
    internal ulong InitialStrategyGeneration { get; }
    internal int SemanticScanOperationCount { get; }
    internal int CycleIndexBuildOperationCount { get; }

    internal ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord> GetCycle(int index) =>
        _decoded.GetCycle(index);
    internal CaptureAttempt GetCapture(int index) => _captures[index];
    internal StartAttempt GetStart(int index) => _starts[index];
    internal StrategyPublicationAttempt GetStrategyPublication(int index) => _strategyPublications[index];
    internal ulong GetConfigurationPublication(int index) => _configurationPublications[index];
    internal bool ContainsConfiguration(ulong generation) => _configurationGenerations.Contains(generation);
    internal bool TryFindConfigurationCycle(ulong generation, out int cycleIndex) =>
        _configurationCycleIndices.TryGetValue(generation, out cycleIndex);

    internal bool TryFindCycle(in CaptureAttempt attempt, out int cycleIndex)
    {
        var key = new ServiceCycleReplayCycleKey(
            attempt.TraceServiceKey,
            attempt.Lifecycle,
            attempt.Configuration,
            attempt.Strategy,
            attempt.Capture,
            attempt.Cycle);
        return _cycleIndices.TryGetValue(key, out cycleIndex);
    }

    private static ulong FindInitialStrategy(StrategyPublicationAttempt[] publications)
    {
        for (var index = 0; index < publications.Length; index++)
            if (publications[index].IsInitial) return publications[index].Generation;
        return 0;
    }

    internal readonly struct CaptureAttempt
    {
        internal CaptureAttempt(ServiceCycleReplayCaptureAttempt value)
        {
            Kind = value.Kind;
            TraceServiceKey = value.TraceServiceKey;
            Lifecycle = value.Lifecycle;
            Configuration = value.Configuration;
            Strategy = value.Strategy;
            Capture = value.Capture;
            Cycle = value.Cycle;
            Code = value.Code;
            Wake = value.Wake;
        }

        internal CaptureAttempt(ServiceCycleSemanticEventKind kind, ServiceCycleSemanticPayload payload)
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

    internal readonly struct StartAttempt
    {
        internal StartAttempt(ServiceCycleReplayStartAttempt value)
        {
            Sequence = value.Sequence;
            Lifecycle = value.Lifecycle;
            Configuration = value.Configuration;
            Kind = value.Kind;
            Code = value.Code;
            Wake = value.Wake;
        }

        internal StartAttempt(ServiceCycleSemanticEvent attempted, ServiceCycleSemanticEvent terminal)
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

    internal readonly struct StrategyPublicationAttempt
    {
        internal StrategyPublicationAttempt(ServiceCycleReplayStrategyPublication value)
        {
            Generation = value.Generation;
            IsCaptureDerived = value.IsCaptureDerived;
            IsInitial = value.IsInitial;
        }

        internal StrategyPublicationAttempt(ulong generation, bool isCaptureDerived, bool isInitial)
        {
            Generation = generation;
            IsCaptureDerived = isCaptureDerived;
            IsInitial = isInitial;
        }

        internal ulong Generation { get; }
        internal bool IsCaptureDerived { get; }
        internal bool IsInitial { get; }
    }

    private struct MutableStartAttempt
    {
        private readonly ServiceCycleSemanticEvent _attempted;
        private ServiceCycleSemanticEvent _terminal;

        internal MutableStartAttempt(ServiceCycleSemanticEvent attempted)
        {
            _attempted = attempted;
            _terminal = default;
            HasTerminal = false;
        }

        internal bool HasTerminal { get; private set; }
        internal void SetTerminal(ServiceCycleSemanticEvent terminal)
        {
            _terminal = terminal;
            HasTerminal = true;
        }
        internal StartAttempt Freeze() => new(_attempted, _terminal);
    }

    private readonly struct StrategyPublicationDraft
    {
        internal StrategyPublicationDraft(
            ulong generation,
            ServiceCycleTraceEventId parent,
            long timestampTicks,
            bool beforeInitialLifecycle)
        {
            Generation = generation;
            Parent = parent;
            TimestampTicks = timestampTicks;
            BeforeInitialLifecycle = beforeInitialLifecycle;
        }
        internal ulong Generation { get; }
        internal ServiceCycleTraceEventId Parent { get; }
        internal long TimestampTicks { get; }
        internal bool BeforeInitialLifecycle { get; }
    }

    private readonly struct CapturePublicationIdentity : IEquatable<CapturePublicationIdentity>
    {
        internal CapturePublicationIdentity(
            ServiceCycleTraceEventId parent,
            ulong strategy,
            long timestampTicks)
        {
            Parent = parent;
            Strategy = strategy;
            TimestampTicks = timestampTicks;
        }
        private ServiceCycleTraceEventId Parent { get; }
        private ulong Strategy { get; }
        private long TimestampTicks { get; }
        public bool Equals(CapturePublicationIdentity other) =>
            Parent == other.Parent && Strategy == other.Strategy && TimestampTicks == other.TimestampTicks;
        public override bool Equals(object? obj) => obj is CapturePublicationIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Parent, Strategy, TimestampTicks);
    }
}
