using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayFooterEncoder
{
    internal static void WriteAll(Span<byte> destination, ServiceCycleReplayArtifactFooter[] footers)
    {
        for (var index = 0; index < footers.Length; index++)
            Write(destination.Slice(index * ServiceCycleReplayArtifactFormat.CycleFooterBytes,
                ServiceCycleReplayArtifactFormat.CycleFooterBytes), in footers[index]);
    }

    private static void Write(Span<byte> row, in ServiceCycleReplayArtifactFooter footer)
    {
        ServiceCycleReplayBinary.I64(row, 0, footer.Sequence);
        var cycle = footer.Context.Cycle;
        ServiceCycleReplayBinary.WriteCycleKey(row, 8, in cycle);
        ServiceCycleReplayBinary.I32(row, 56, (int)footer.Disposition);
        ServiceCycleReplayBinary.U32(row, 60,
            (footer.HasReturnedWake ? 1u : 0u) | (footer.HasProjection ? 2u : 0u));
        ServiceCycleReplayBinary.I32(row, 64, footer.ExpectedActionCount);
        ServiceCycleReplayBinary.I32(row, 68, footer.RetainedRecordCount);
        ServiceCycleReplayBinary.I64(row, 72, footer.FirstRecordSequence);
        ServiceCycleReplayBinary.I64(row, 80, footer.LastRecordSequence);
        ServiceCycleReplayCompletenessEncoder.Write(row, 88, footer.Completeness);
        ServiceCycleReplayBinary.I64(row, 104, footer.Context.DecisionAt);
        ServiceCycleReplayBinary.I64(row, 112, footer.EncodingDurationTicks);
        ServiceCycleReplayBinary.I64(row, 120, footer.EncodingTimestampFrequency);
        ServiceCycleReplayBinary.I64(row, 128, footer.EncodingAllocatedBytes);
        ServiceCycleReplayBinary.I32(row, 136, (int)footer.ReturnedWake.Kind);
        ServiceCycleReplayBinary.I32(row, 140, footer.Projection.Count);
        ServiceCycleReplayBinary.I64(row, 144, footer.ReturnedWake.Delay.Ticks);
        ServiceCycleReplayBinary.I64(row, 152, footer.ReturnedWake.DueTime.Ticks);
        var receipt = footer.Context.PreviousReceipt;
        ServiceCycleReplayReceiptEncoder.Write(row, in receipt);
        var join = footer.Join;
        ServiceCycleReplayBinary.I32(row, 332, (int)join.Code);
        ServiceCycleReplayBinary.I32(row, 336, (int)join.CycleTerminalKind);
        ServiceCycleReplayBinary.I32(row, 340, (int)join.EvaluationTerminalKind);
        ServiceCycleReplayBinary.U64(row, 344, join.ProjectionFingerprint);
        ServiceCycleReplayBinary.U64(row, 352, join.StatePublication);
        ServiceCycleReplayBinary.U64(row, 360, join.Batch);
        ServiceCycleReplayBinary.U64(row, 368, join.TerminalEventSequence);
        WriteProjection(row, footer.Projection);
    }

    private static void WriteProjection(Span<byte> row, ServiceStateProjectionSnapshot snapshot)
    {
        for (var index = 0; index < ServiceStateProjectionSnapshot.MaximumEntryCount; index++)
        {
            var projection = row.Slice(384 + index * ServiceCycleReplayArtifactFormat.ProjectionEntryBytes,
                ServiceCycleReplayArtifactFormat.ProjectionEntryBytes);
            if (index >= snapshot.Count) continue;
            var entry = snapshot.GetEntry(index);
            ServiceCycleReplayBinary.I32(projection, 0, entry.Key.Value);
            ServiceCycleReplayBinary.I32(projection, 4, (int)entry.Value.Kind);
            ServiceCycleReplayBinary.I64(projection, 8, entry.Value.Integer);
            ServiceCycleReplayBinary.I64(projection, 16,
                BitConverter.DoubleToInt64Bits(entry.Value.FloatingPoint));
        }
    }
}
