using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayRecordJoinValidator
{
    internal static ServiceCycleReplaySemanticJoinCode Validate(
        in ServiceCycleReplayArtifactFooter footer,
        ServiceCycleReplayArtifactRecord[] records)
    {
        if (records.Length != footer.RetainedRecordCount)
            return ServiceCycleReplaySemanticJoinCode.RecordBoundsMismatch;
        if (records.Length == 0)
            return footer.FirstRecordSequence == 0 && footer.LastRecordSequence == 0
                ? ServiceCycleReplaySemanticJoinCode.RequiredRecordMissing
                : ServiceCycleReplaySemanticJoinCode.RecordBoundsMismatch;
        if (records[0].Sequence != footer.FirstRecordSequence ||
            records[^1].Sequence != footer.LastRecordSequence)
            return ServiceCycleReplaySemanticJoinCode.RecordBoundsMismatch;
        var requiredNonActionRecords = footer.Disposition ==
            ServiceCycleReplayCycleFooterDisposition.EvaluationAborted ? 2 : 3;
        if (records.Length < requiredNonActionRecords ||
            footer.ExpectedActionCount > records.Length - requiredNonActionRecords)
            return ServiceCycleReplaySemanticJoinCode.RecordBoundsMismatch;
        var inputs = 0;
        var previous = 0;
        var next = 0;
        var actionSeen = footer.ExpectedActionCount == 0 ? Array.Empty<bool>() : new bool[footer.ExpectedActionCount];
        for (var index = 0; index < records.Length; index++)
        {
            var identity = records[index].Identity;
            switch (identity.Kind)
            {
                case ServiceCycleReplayRecordKind.CycleInput: inputs++; break;
                case ServiceCycleReplayRecordKind.PreviousState: previous++; break;
                case ServiceCycleReplayRecordKind.NextState: next++; break;
                case ServiceCycleReplayRecordKind.Action:
                    if ((uint)identity.Index >= (uint)actionSeen.Length)
                        return ServiceCycleReplaySemanticJoinCode.ActionRecordGap;
                    if (actionSeen[identity.Index])
                        return ServiceCycleReplaySemanticJoinCode.RequiredRecordDuplicate;
                    actionSeen[identity.Index] = true;
                    break;
            }
        }
        if (inputs > 1 || previous > 1 || next > 1)
            return ServiceCycleReplaySemanticJoinCode.RequiredRecordDuplicate;
        var requiredNext = footer.Disposition ==
            ServiceCycleReplayCycleFooterDisposition.EvaluationAborted ? 0 : 1;
        if (inputs != 1 || previous != 1 || next != requiredNext)
            return ServiceCycleReplaySemanticJoinCode.RequiredRecordMissing;
        for (var index = 0; index < actionSeen.Length; index++)
            if (!actionSeen[index]) return ServiceCycleReplaySemanticJoinCode.ActionRecordGap;
        return ServiceCycleReplaySemanticJoinCode.Complete;
    }
}
