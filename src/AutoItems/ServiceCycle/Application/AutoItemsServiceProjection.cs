using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsServiceProjection
{
    internal const int CapturedKey = 10;
    internal const int RejectedProfilesKey = 11;
    internal const int EligibleRelicsKey = 12;
    internal const int EligibleScrollsKey = 13;
    internal const int PlannedActionsKey = 14;
    internal const int DecisionKindKey = 15;
    internal const int TemporaryItemsKey = 16;
    internal const int EligibleTemporaryItemsKey = 17;
    internal const int TemporaryQuarantineCauseKey = 18;
    internal const int TemporaryQuarantineGuidLowKey = 19;
    internal const int TemporaryQuarantineGuidHighKey = 20;

    internal static void Write(
        in AutoItemsCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(new ServiceProjectionKey(CapturedKey), Integer(decision.Captured));
        output.Add(new ServiceProjectionKey(RejectedProfilesKey), Integer(decision.RejectedProfiles));
        output.Add(new ServiceProjectionKey(EligibleRelicsKey), Integer(decision.EligibleRelics));
        output.Add(new ServiceProjectionKey(EligibleScrollsKey), Integer(decision.EligibleScrolls));
        output.Add(new ServiceProjectionKey(PlannedActionsKey), Integer(decision.PlannedActions));
        output.Add(new ServiceProjectionKey(DecisionKindKey), Integer((int)decision.Kind));
        output.Add(new ServiceProjectionKey(TemporaryItemsKey), Integer(decision.TemporaryItems));
        output.Add(
            new ServiceProjectionKey(EligibleTemporaryItemsKey),
            Integer(decision.EligibleTemporaryItems));
        output.Add(
            new ServiceProjectionKey(TemporaryQuarantineCauseKey),
            Integer((int)state.LastTemporaryQuarantineCause));
        Span<byte> itemBytes = stackalloc byte[16];
        if (!state.LastQuarantinedTemporaryItem.TryWriteBytes(itemBytes))
            throw new InvalidOperationException(
                "Auto Items could not project its exact quarantined temporary-item UUID.");
        output.Add(
            new ServiceProjectionKey(TemporaryQuarantineGuidLowKey),
            Integer(BinaryPrimitives.ReadInt64LittleEndian(itemBytes.Slice(0, 8))));
        output.Add(
            new ServiceProjectionKey(TemporaryQuarantineGuidHighKey),
            Integer(BinaryPrimitives.ReadInt64LittleEndian(itemBytes.Slice(8, 8))));
    }

    internal static bool TryReadDecision(
        in ServiceStateProjectionSnapshot projection,
        out AutoItemsDecisionKind kind,
        out Guid quarantinedItem,
        out AutoItemsTemporaryQuarantineCause quarantineCause)
    {
        var foundKind = false;
        var quarantineLow = 0L;
        var quarantineHigh = 0L;
        kind = AutoItemsDecisionKind.Disabled;
        quarantineCause = AutoItemsTemporaryQuarantineCause.None;
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Value.Kind != ServiceProjectionValueKind.Integer) continue;
            switch (entry.Key.Value)
            {
                case DecisionKindKey when entry.Value.Integer is
                    >= (int)AutoItemsDecisionKind.Disabled and
                    <= (int)AutoItemsDecisionKind.TemporaryItemQuarantined:
                    kind = (AutoItemsDecisionKind)entry.Value.Integer;
                    foundKind = true;
                    break;
                case TemporaryQuarantineCauseKey when entry.Value.Integer is
                    >= (int)AutoItemsTemporaryQuarantineCause.None and
                    <= (int)AutoItemsTemporaryQuarantineCause.MissingEngagementEvidence:
                    quarantineCause =
                        (AutoItemsTemporaryQuarantineCause)entry.Value.Integer;
                    break;
                case TemporaryQuarantineGuidLowKey:
                    quarantineLow = entry.Value.Integer;
                    break;
                case TemporaryQuarantineGuidHighKey:
                    quarantineHigh = entry.Value.Integer;
                    break;
            }
        }
        Span<byte> itemBytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(itemBytes.Slice(0, 8), quarantineLow);
        BinaryPrimitives.WriteInt64LittleEndian(itemBytes.Slice(8, 8), quarantineHigh);
        quarantinedItem = new Guid(itemBytes);
        return foundKind;
    }

    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}
