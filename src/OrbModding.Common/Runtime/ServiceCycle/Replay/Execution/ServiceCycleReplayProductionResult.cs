using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using ArtifactCodecRole = OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>Stable production-phase failures and exact detached-record comparison.</summary>
internal static class ServiceCycleReplayProductionResult
{
    internal static ServiceCycleReplayExecutionResult CompareDetached(
        ServiceCycleReplayArtifactDocument artifact,
        ServiceCycleReplaySession actual,
        ServiceCycleReplayTraceMap traceMap,
        ServiceCycleReplayCycleKey location,
        int completedCycles)
    {
        for (var runtimeKey = 1; runtimeKey <= traceMap.Count; runtimeKey++)
        {
            traceMap.TryArtifactKey(runtimeKey, out var artifactKey);
            if (!CodecManifestMatches(artifact, actual, artifactKey, runtimeKey))
                return Fault(
                    CycleForService(artifact, artifactKey, location),
                    ServiceCycleReplayExecutionDetailCode.CodecDescriptorRejected);
        }
        if (!actual.TryReadHighWaterFence(out var fence) ||
            fence.RecordCount != artifact.Recording.HighWater.RecordCount ||
            fence.FooterCount != artifact.Recording.HighWater.FooterCount)
            return Mismatch(location, completedCycles, 1);
        var buffer = Array.Empty<byte>();
        var expectedRecords = new Dictionary<
            (ServiceCycleReplayCycleKey Cycle, ServiceCycleReplayRecordIdentity Identity),
            ServiceCycleReplayArtifactRecord>(fence.RecordCount);
        var expectedFooters = new Dictionary<
            ServiceCycleReplayCycleKey,
            ServiceCycleReplayArtifactFooter>(fence.FooterCount);
        for (var cycleIndex = 0; cycleIndex < artifact.CycleCount; cycleIndex++)
        {
            var cycle = artifact.GetCycle(cycleIndex);
            expectedFooters.Add(cycle.Key, cycle.Footer);
            for (var recordIndex = 0; recordIndex < cycle.RecordCount; recordIndex++)
            {
                var record = cycle.GetRecord(recordIndex);
                expectedRecords.Add((record.Cycle, record.Identity), record);
            }
        }
        // Worker commits from different services may interleave differently between otherwise exact runs.
        // Match by stable cycle/record identity, never by nondeterministic global publication sequence.
        for (var index = 0; index < fence.RecordCount; index++)
        {
            var header = actual.ReadRecordHeader(index, in fence);
            var runtimeCycle = header.Cycle;
            var artifactCycle = traceMap.ToArtifact(in runtimeCycle);
            if (!expectedRecords.Remove((artifactCycle, header.Identity), out var expected))
                return Mismatch(artifactCycle, completedCycles, 2, index);
            if (header.SchemaVersion != expected.SchemaVersion)
                return RecordMismatch(artifactCycle, header.Identity, completedCycles, 1);
            if (header.ByteLength != expected.PayloadView.Length)
                return RecordMismatch(artifactCycle, header.Identity, completedCycles, 2);
            if (buffer.Length < header.ByteLength) buffer = new byte[header.ByteLength];
            actual.CopyBytes(header.ByteOffset, buffer.AsSpan(0, header.ByteLength), in fence);
            if (!buffer.AsSpan(0, header.ByteLength).SequenceEqual(expected.PayloadView.Span))
                return RecordMismatch(artifactCycle, header.Identity, completedCycles, 3);
        }
        for (var index = 0; index < fence.FooterCount; index++)
        {
            var actualFooter = actual.ReadFooter(index, in fence);
            var runtimeCycle = actualFooter.Context.Cycle;
            var artifactCycle = traceMap.ToArtifact(in runtimeCycle);
            if (!expectedFooters.Remove(artifactCycle, out var expectedFooter))
                return Mismatch(artifactCycle, completedCycles, 4, index);
            var footerMismatch = ServiceCycleReplayProductionFooterComparer.Compare(
                in expectedFooter, in actualFooter);
            if (footerMismatch.HasValue)
            {
                var located = new ServiceCycleReplayCycleMismatch(expectedFooter.Context.Cycle,
                    footerMismatch.Value);
                return ServiceCycleReplayExecutionResult.Diverged(completedCycles, in located);
            }
        }
        foreach (var pair in expectedRecords)
            return RecordMismatch(pair.Key.Cycle, pair.Key.Identity, completedCycles, 4);
        foreach (var pair in expectedFooters)
            return Mismatch(pair.Key, completedCycles, 5);
        return ServiceCycleReplayExecutionResult.Success(completedCycles);
    }

    private static bool CodecManifestMatches(
        ServiceCycleReplayArtifactDocument artifact,
        ServiceCycleReplaySession actual,
        int artifactTraceServiceKey,
        int runtimeTraceServiceKey)
    {
        if (!actual.TryReadCodecManifest(runtimeTraceServiceKey, out var manifest)) return false;
        var matched = 0;
        for (var index = 0; index < artifact.CodecCount; index++)
        {
            var entry = artifact.GetCodec(index);
            if (entry.TraceServiceKey != artifactTraceServiceKey) continue;
            var descriptor = entry.Role switch
            {
                ArtifactCodecRole.CycleInput => manifest.CycleInput,
                ArtifactCodecRole.State => manifest.State,
                ArtifactCodecRole.Action => manifest.Action,
                _ => default,
            };
            if (descriptor != entry.Descriptor) return false;
            matched++;
        }
        return matched == 3;
    }

    private static ServiceCycleReplayCycleKey CycleForService(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey,
        ServiceCycleReplayCycleKey fallback)
    {
        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var cycle = artifact.GetCycle(index).Key;
            if (cycle.TraceServiceKey == traceServiceKey) return cycle;
        }
        return fallback;
    }

    internal static ServiceCycleReplayExecutionResult Fault(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayExecutionDetailCode detail)
    {
        var fault = new ServiceCycleReplayFault(
            ServiceCycleReplayFaultCode.ExecutionFaulted,
            ServiceCycleReplayFailureLocation.Execution,
            (int)detail);
        var failure = new ServiceCycleReplayCycleFailure(cycle, fault);
        return ServiceCycleReplayExecutionResult.Faulted(0, in failure);
    }

    private static ServiceCycleReplayExecutionResult Mismatch(
        ServiceCycleReplayCycleKey cycle,
        int completed,
        int field,
        int element = 0)
    {
        var mismatch = new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.SemanticEvent,
            default,
            field,
            element);
        var located = new ServiceCycleReplayCycleMismatch(cycle, mismatch);
        return ServiceCycleReplayExecutionResult.Diverged(completed, in located);
    }

    private static ServiceCycleReplayExecutionResult RecordMismatch(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordIdentity identity,
        int completed,
        int field)
    {
        var code = identity.Kind switch
        {
            ServiceCycleReplayRecordKind.CycleInput => ServiceCycleReplayMismatchCode.CycleInput,
            ServiceCycleReplayRecordKind.PreviousState => ServiceCycleReplayMismatchCode.PreviousState,
            ServiceCycleReplayRecordKind.NextState => ServiceCycleReplayMismatchCode.NextState,
            ServiceCycleReplayRecordKind.Action => ServiceCycleReplayMismatchCode.Action,
            _ => throw new InvalidOperationException("Replay produced an unknown record identity."),
        };
        var mismatch = new ServiceCycleReplayMismatch(code, identity, field, identity.Index);
        var located = new ServiceCycleReplayCycleMismatch(cycle, mismatch);
        return ServiceCycleReplayExecutionResult.Diverged(completed, in located);
    }
}
