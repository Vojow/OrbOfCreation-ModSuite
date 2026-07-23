using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal sealed partial class ServiceCycleReplaySemanticIndex
{
    internal readonly struct IndexedMatch
    {
        internal IndexedMatch(int index, int count)
        {
            Index = index;
            Count = count;
        }

        internal int Index { get; }
        internal int Count { get; }
        internal IndexedMatch AddDuplicate() => new(Index, checked(Count + 1));
    }

    private readonly struct ParentKindKey : IEquatable<ParentKindKey>
    {
        internal ParentKindKey(ServiceCycleTraceEventId parent, ServiceCycleSemanticEventKind kind)
        {
            Parent = parent;
            Kind = kind;
        }

        private ServiceCycleTraceEventId Parent { get; }
        private ServiceCycleSemanticEventKind Kind { get; }
        public bool Equals(ParentKindKey other) => Parent == other.Parent && Kind == other.Kind;
        public override bool Equals(object? obj) => obj is ParentKindKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Parent, (int)Kind);
    }

    private readonly struct CycleBatchKey : IEquatable<CycleBatchKey>
    {
        internal CycleBatchKey(ServiceCycleReplayCycleKey cycle, ulong batch)
        {
            Cycle = cycle;
            Batch = batch;
        }

        private ServiceCycleReplayCycleKey Cycle { get; }
        private ulong Batch { get; }
        public bool Equals(CycleBatchKey other) => Cycle == other.Cycle && Batch == other.Batch;
        public override bool Equals(object? obj) => obj is CycleBatchKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Cycle, Batch);
    }

    private readonly struct CycleBatchActionKey : IEquatable<CycleBatchActionKey>
    {
        internal CycleBatchActionKey(ServiceCycleReplayCycleKey cycle, ulong batch, int action)
        {
            Cycle = cycle;
            Batch = batch;
            Action = action;
        }

        private ServiceCycleReplayCycleKey Cycle { get; }
        private ulong Batch { get; }
        private int Action { get; }
        public bool Equals(CycleBatchActionKey other) =>
            Cycle == other.Cycle && Batch == other.Batch && Action == other.Action;
        public override bool Equals(object? obj) => obj is CycleBatchActionKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Cycle, Batch, Action);
    }

    private readonly struct PublicationKey : IEquatable<PublicationKey>
    {
        internal PublicationKey(ulong service, ulong generation, bool configuration)
        {
            Service = service;
            Generation = generation;
            Configuration = configuration;
        }

        private ulong Service { get; }
        private ulong Generation { get; }
        private bool Configuration { get; }
        public bool Equals(PublicationKey other) =>
            Service == other.Service && Generation == other.Generation &&
            Configuration == other.Configuration;
        public override bool Equals(object? obj) => obj is PublicationKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Service, Generation, Configuration);
    }
}

/// <summary>Deterministic instrumentation for complexity tests; it is never used for runtime decisions.</summary>
internal sealed class ServiceCycleReplayFormatWorkCounter
{
    internal long Operations { get; private set; }
    internal void Add(long count = 1) => Operations = checked(Operations + count);
}
