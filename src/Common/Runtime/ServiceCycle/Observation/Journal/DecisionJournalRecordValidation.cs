using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal static class DecisionJournalRecordValidation
{
    internal static void Validate(in DecisionJournalRecord record)
    {
        if (record.Kind is < DecisionJournalRecordKind.DecisionSpan or > DecisionJournalRecordKind.Action)
            throw new ArgumentOutOfRangeException(nameof(record));
        if (record.FirstTimestampTicks < 0 || record.LastTimestampTicks < record.FirstTimestampTicks)
            throw new ArgumentOutOfRangeException(nameof(record));
        switch (record.Kind)
        {
            case DecisionJournalRecordKind.DecisionSpan:
                ValidateDecision(in record);
                return;
            case DecisionJournalRecordKind.Action:
                ValidateAction(in record);
                return;
            default:
                ValidateTransition(in record);
                return;
        }
    }

    private static void ValidateDecision(in DecisionJournalRecord record)
    {
        if (!record.Service.IsValid || record.Lifecycle == 0 || record.RepeatCount <= 0 ||
            (record.FirstCycle == 0) != (record.LastCycle == 0) || record.LastCycle < record.FirstCycle ||
            record.FirstCycle != 0 && record.LastCycle - record.FirstCycle != (ulong)(record.RepeatCount - 1) ||
            record.DecisionOutcomeKind == DecisionJournalDecisionOutcomeKind.None != (record.DecisionOutcomeCode == 0) ||
            record.Generation != 0 || record.TransitionCode != 0 || record.ActionOrdinal != 0 ||
            record.Attribution.IsValid || record.ActionOutcome.Value != 0)
        {
            throw new ArgumentException("The decision journal span is malformed.", nameof(record));
        }
        ValidateFault(in record);
    }

    private static void ValidateAction(in DecisionJournalRecord record)
    {
        if (!record.Service.IsValid || record.Lifecycle != 0 || record.FirstCycle == 0 ||
            record.LastCycle != record.FirstCycle || record.RepeatCount != 1 ||
            record.FirstTimestampTicks != record.LastTimestampTicks || record.ActionOrdinal == 0 ||
            !record.Attribution.IsValid || !record.ActionOutcome.IsValid ||
            record.DecisionOutcomeKind != DecisionJournalDecisionOutcomeKind.None ||
            record.DecisionOutcomeCode != 0 || record.FaultCategory != 0 || record.FaultCode != 0 ||
            record.FirstFaultOccurrence != 0 || record.LastFaultOccurrence != 0 ||
            record.Generation != 0 || record.TransitionCode != 0)
        {
            throw new ArgumentException("The decision journal action is malformed.", nameof(record));
        }
    }

    internal static bool IsSuiteWide(DecisionJournalRecordKind kind) =>
        kind is DecisionJournalRecordKind.ConfigurationChanged or DecisionJournalRecordKind.StrategyChanged or
            DecisionJournalRecordKind.EmergencyEntered or DecisionJournalRecordKind.EmergencyCleared;

    private static void ValidateTransition(in DecisionJournalRecord record)
    {
        var global = IsSuiteWide(record.Kind);
        var emergency = record.Kind is DecisionJournalRecordKind.EmergencyEntered or DecisionJournalRecordKind.EmergencyCleared;
        var expectedLifecycle = record.Kind is DecisionJournalRecordKind.LifecycleChanged or DecisionJournalRecordKind.WorldGateHeld
            ? record.Generation : 0;
        if (global == record.Service.IsValid || !emergency && record.Generation == 0 ||
            record.Lifecycle != expectedLifecycle || record.TransitionCode < 0 || record.RepeatCount != 1 ||
            record.FirstTimestampTicks != record.LastTimestampTicks || record.FirstCycle != 0 || record.LastCycle != 0 ||
            record.DecisionOutcomeKind != DecisionJournalDecisionOutcomeKind.None || record.DecisionOutcomeCode != 0 ||
            record.FaultCategory != 0 || record.FaultCode != 0 || record.FirstFaultOccurrence != 0 ||
            record.LastFaultOccurrence != 0 || record.ActionOrdinal != 0 || record.Attribution.IsValid ||
            record.ActionOutcome.Value != 0)
        {
            throw new ArgumentException("The decision journal transition is malformed.", nameof(record));
        }
    }

    private static void ValidateFault(in DecisionJournalRecord record)
    {
        if (record.FaultCategory == 0)
        {
            if (record.FaultCode != 0 || record.FirstFaultOccurrence != 0 || record.LastFaultOccurrence != 0)
                throw new ArgumentException("Fault payload requires a fault category.", nameof(record));
            return;
        }
        if (record.FaultCategory is < ServiceFaultCategory.Capture or > ServiceFaultCategory.Start ||
            record.FaultCode <= 0 || record.FirstFaultOccurrence <= 0 ||
            record.LastFaultOccurrence < record.FirstFaultOccurrence ||
            record.DecisionOutcomeKind != DecisionJournalDecisionOutcomeKind.Fault ||
            record.DecisionOutcomeCode != record.FaultCode)
        {
            throw new ArgumentException("The decision journal fault is malformed.", nameof(record));
        }
    }
}
