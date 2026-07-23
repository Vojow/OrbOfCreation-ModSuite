using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal sealed partial class ServiceCycleReplayProductionArtifactPlan
{
    private static List<T>[] NewLists<T>(int count)
    {
        var values = new List<T>[count];
        for (var index = 0; index < count; index++) values[index] = new List<T>();
        return values;
    }

    private static HashSet<T>[] NewSets<T>(int count) where T : notnull
    {
        var values = new HashSet<T>[count];
        for (var index = 0; index < count; index++) values[index] = new HashSet<T>();
        return values;
    }

    private static T[][] Freeze<T>(List<T>[] values)
    {
        var result = new T[values.Length][];
        for (var index = 0; index < values.Length; index++) result[index] = values[index].ToArray();
        return result;
    }

    private static T[][] Freeze<T>(HashSet<T>[] values) where T : notnull
    {
        var result = new T[values.Length][];
        for (var index = 0; index < values.Length; index++)
            result[index] = new List<T>(values[index]).ToArray();
        return result;
    }

    private static Dictionary<WorkerKey, MonotonicTimestamp[]> Freeze(
        Dictionary<WorkerKey, List<MonotonicTimestamp>> values)
    {
        var result = new Dictionary<WorkerKey, MonotonicTimestamp[]>(values.Count);
        foreach (var pair in values) result.Add(pair.Key, pair.Value.ToArray());
        return result;
    }

    private readonly struct WorkerKey : IEquatable<WorkerKey>
    {
        internal WorkerKey(int service, ulong lifecycle)
        {
            Service = service;
            Lifecycle = lifecycle;
        }

        private int Service { get; }
        private ulong Lifecycle { get; }

        public bool Equals(WorkerKey other) =>
            Service == other.Service && Lifecycle == other.Lifecycle;

        public override bool Equals(object? obj) => obj is WorkerKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Service, Lifecycle);
    }

    internal readonly struct CapturePublicationIdentity : IEquatable<CapturePublicationIdentity>
    {
        internal CapturePublicationIdentity(
            ServiceCycleTraceEventId parent,
            ulong strategy,
            long timestamp)
        {
            Parent = parent;
            Strategy = strategy;
            Timestamp = timestamp;
        }

        private ServiceCycleTraceEventId Parent { get; }
        private ulong Strategy { get; }
        private long Timestamp { get; }

        public bool Equals(CapturePublicationIdentity other) =>
            Parent == other.Parent && Strategy == other.Strategy && Timestamp == other.Timestamp;

        public override bool Equals(object? obj) =>
            obj is CapturePublicationIdentity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Parent, Strategy, Timestamp);
    }
}
