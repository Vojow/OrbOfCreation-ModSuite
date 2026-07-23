using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplaySemanticJoiner
{
    internal static ServiceCycleReplayJoinResult Join(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplayRecordingSnapshot recording,
        ServiceCycleReplayArtifactFooter[] sourceFooters,
        ServiceCycleReplayArtifactRecord[] globalRecords) =>
        Join(semantic, recording, sourceFooters, globalRecords, null);

    internal static ServiceCycleReplayJoinResult Join(
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplayRecordingSnapshot recording,
        ServiceCycleReplayArtifactFooter[] sourceFooters,
        ServiceCycleReplayArtifactRecord[] globalRecords,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        var footerByCycle = new Dictionary<ServiceCycleReplayCycleKey, int>(sourceFooters.Length);
        var footerByCapture = new Dictionary<CaptureKey, int>(sourceFooters.Length);
        var recordLists = new List<ServiceCycleReplayArtifactRecord>[sourceFooters.Length];
        var eventLists = new List<int>[sourceFooters.Length];
        for (var index = 0; index < sourceFooters.Length; index++)
        {
            work?.Add();
            var key = sourceFooters[index].Context.Cycle;
            if (!footerByCycle.TryAdd(key, index))
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.DuplicateCycleFooter, index);
            var capture = new CaptureKey(key);
            if (!footerByCapture.TryGetValue(capture, out var existing) || index < existing)
                footerByCapture[capture] = index;
            recordLists[index] = new List<ServiceCycleReplayArtifactRecord>();
            eventLists[index] = new List<int>();
        }
        var semanticIndex = ServiceCycleReplaySemanticIndex.Build(semantic, work);
        var hasUnjoinedRecord = GroupRecords(globalRecords, footerByCycle, recordLists, work);
        GroupEvents(semantic, footerByCycle, footerByCapture, eventLists, work);
        FindFirstMissingFooter(
            semantic,
            footerByCycle,
            footerByCapture,
            semanticIndex,
            out var firstMissingFooterCycle,
            out var firstMissingFooterSemanticSequence,
            work);
        var footers = new ServiceCycleReplayArtifactFooter[sourceFooters.Length];
        var records = new ServiceCycleReplayArtifactRecord[sourceFooters.Length][];
        var semanticIndices = new int[sourceFooters.Length][];
        var eligibility = ServiceCycleReplayJoinEligibility.Initial(semantic, recording);
        for (var index = 0; index < sourceFooters.Length; index++)
        {
            work?.Add();
            records[index] = recordLists[index].ToArray();
            semanticIndices[index] = eventLists[index].ToArray();
            var join = hasUnjoinedRecord
                ? ServiceCycleReplayJoinValues.Simple(ServiceCycleReplaySemanticJoinCode.UnjoinedRecord)
                : ServiceCycleReplayCycleJoiner.Join(
                    semantic, sourceFooters[index], records[index], semanticIndices[index], semanticIndex);
            footers[index] = sourceFooters[index].WithJoin(join);
            if (eligibility == ServiceCycleReplayArtifactEligibilityCode.Complete)
            {
                var footer = footers[index];
                eligibility = ServiceCycleReplayJoinEligibility.ForFooter(in footer);
            }
        }
        if (hasUnjoinedRecord && eligibility == ServiceCycleReplayArtifactEligibilityCode.Complete)
            eligibility = ServiceCycleReplayArtifactEligibilityCode.RecordCoverageIncomplete;
        if (firstMissingFooterSemanticSequence != 0 &&
            eligibility == ServiceCycleReplayArtifactEligibilityCode.Complete)
            eligibility = ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete;
        return new ServiceCycleReplayJoinResult(
            footers,
            records,
            semanticIndices,
            eligibility,
            firstMissingFooterCycle,
            firstMissingFooterSemanticSequence,
            work);
    }

    private static bool GroupRecords(
        ServiceCycleReplayArtifactRecord[] globalRecords,
        Dictionary<ServiceCycleReplayCycleKey, int> footerByCycle,
        List<ServiceCycleReplayArtifactRecord>[] recordLists,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        var hasUnjoined = false;
        for (var index = 0; index < globalRecords.Length; index++)
        {
            work?.Add();
            var record = globalRecords[index];
            if (footerByCycle.TryGetValue(record.Cycle, out var footerIndex)) recordLists[footerIndex].Add(record);
            else hasUnjoined = true;
        }
        return hasUnjoined;
    }

    private static void GroupEvents(
        ServiceCycleTraceDocument semantic,
        Dictionary<ServiceCycleReplayCycleKey, int> footerByCycle,
        Dictionary<CaptureKey, int> footerByCapture,
        List<int>[] eventLists,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        for (var index = 0; index < semantic.Count; index++)
        {
            work?.Add();
            var item = semantic[index];
            if (TryFindFooter(item, footerByCycle, footerByCapture, out var footerIndex))
                eventLists[footerIndex].Add(index);
        }
    }

    private static bool TryFindFooter(
        ServiceCycleSemanticEvent item,
        Dictionary<ServiceCycleReplayCycleKey, int> footerByCycle,
        Dictionary<CaptureKey, int> footerByCapture,
        out int footerIndex)
    {
        if (ServiceCycleReplaySemanticMatch.HasFullCycleIdentity(item))
            return footerByCycle.TryGetValue(ServiceCycleReplaySemanticMatch.KeyFrom(item), out footerIndex);
        if (ServiceCycleReplaySemanticMatch.HasCaptureIdentity(item))
            return footerByCapture.TryGetValue(new CaptureKey(item), out footerIndex);
        footerIndex = -1;
        return false;
    }

    private static void FindFirstMissingFooter(
        ServiceCycleTraceDocument semantic,
        Dictionary<ServiceCycleReplayCycleKey, int> footerByCycle,
        Dictionary<CaptureKey, int> footerByCapture,
        ServiceCycleReplaySemanticIndex semanticIndex,
        out ServiceCycleReplayCycleKey cycle,
        out ulong semanticSequence,
        ServiceCycleReplayFormatWorkCounter? work)
    {
        cycle = default;
        semanticSequence = 0;
        for (var eventIndex = 0; eventIndex < semantic.Count; eventIndex++)
        {
            work?.Add();
            var item = semantic[eventIndex];
            if (item.Kind == ServiceCycleSemanticEventKind.StartAttempted)
            {
                if (CountDirectTerminals(
                        semanticIndex,
                        item.Id,
                        ServiceCycleSemanticEventKind.StartReady,
                        ServiceCycleSemanticEventKind.StartDeferred,
                        ServiceCycleSemanticEventKind.StartFaulted) == 1) continue;
                semanticSequence = item.Id.Sequence;
                return;
            }
            if ((item.Kind is ServiceCycleSemanticEventKind.StartReady or
                ServiceCycleSemanticEventKind.StartDeferred or
                ServiceCycleSemanticEventKind.StartFaulted) &&
                !semanticIndex.ParentIs(item, ServiceCycleSemanticEventKind.StartAttempted))
            {
                semanticSequence = item.Id.Sequence;
                return;
            }
            if (item.Kind == ServiceCycleSemanticEventKind.CaptureStarted &&
                !semanticIndex.ParentIs(item, ServiceCycleSemanticEventKind.StartReady))
            {
                semanticSequence = item.Id.Sequence;
                return;
            }
            if ((item.Kind is ServiceCycleSemanticEventKind.CaptureUnavailable or
                ServiceCycleSemanticEventKind.CaptureFaulted) &&
                !semanticIndex.ParentIs(item, ServiceCycleSemanticEventKind.CaptureStarted))
            {
                semanticSequence = item.Id.Sequence;
                return;
            }
            if (ServiceCycleReplaySemanticMatch.HasFullCycleIdentity(item))
            {
                var candidate = ServiceCycleReplaySemanticMatch.KeyFrom(item);
                if (footerByCycle.ContainsKey(candidate)) continue;
                cycle = candidate;
                semanticSequence = item.Id.Sequence;
                return;
            }
            if (!ServiceCycleReplaySemanticMatch.HasCaptureIdentity(item) ||
                TryFindFooter(item, footerByCycle, footerByCapture, out _)) continue;
            if (item.Kind is ServiceCycleSemanticEventKind.CaptureUnavailable or
                ServiceCycleSemanticEventKind.CaptureFaulted) continue;
            if (item.Kind == ServiceCycleSemanticEventKind.CaptureStarted &&
                CountDirectTerminals(
                    semanticIndex,
                    item.Id,
                    ServiceCycleSemanticEventKind.CaptureUnavailable,
                    ServiceCycleSemanticEventKind.CaptureFaulted) == 1) continue;
            semanticSequence = item.Id.Sequence;
            return;
        }
    }

    private static int CountDirectTerminals(
        ServiceCycleReplaySemanticIndex semanticIndex,
        ServiceCycleTraceEventId parent,
        ServiceCycleSemanticEventKind first,
        ServiceCycleSemanticEventKind second,
        ServiceCycleSemanticEventKind third = default)
    {
        var count = semanticIndex.CountDirectChildren(parent, first) +
            semanticIndex.CountDirectChildren(parent, second);
        return third == default ? count : count + semanticIndex.CountDirectChildren(parent, third);
    }

    private readonly struct CaptureKey : System.IEquatable<CaptureKey>
    {
        internal CaptureKey(ServiceCycleReplayCycleKey cycle)
        {
            Service = checked((ulong)cycle.TraceServiceKey);
            Lifecycle = cycle.Lifecycle;
            Configuration = cycle.Configuration;
            Capture = cycle.Capture;
            Cycle = cycle.Cycle;
        }

        internal CaptureKey(ServiceCycleSemanticEvent item)
        {
            Service = item.Payload.Service;
            Lifecycle = item.Payload.Lifecycle;
            Configuration = item.Payload.Configuration;
            Capture = item.Payload.Capture;
            Cycle = item.Payload.Cycle;
        }

        private ulong Service { get; }
        private ulong Lifecycle { get; }
        private ulong Configuration { get; }
        private ulong Capture { get; }
        private ulong Cycle { get; }
        public bool Equals(CaptureKey other) => Service == other.Service && Lifecycle == other.Lifecycle &&
            Configuration == other.Configuration && Capture == other.Capture && Cycle == other.Cycle;
        public override bool Equals(object? obj) => obj is CaptureKey other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(
            Service, Lifecycle, Configuration, Capture, Cycle);
    }
}
