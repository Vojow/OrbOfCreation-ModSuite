using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal static class DecisionJournalRecordValidation
{
    internal static void Validate(in DecisionJournalRecord record)
    {
        if (record.Kind is < DecisionJournalRecordKind.DecisionSpan or
            > DecisionJournalRecordKind.WorldGateHeld)
            throw new ArgumentOutOfRangeException(nameof(record));
        if (record.FirstTimestampTicks < 0 || record.LastTimestampTicks < record.FirstTimestampTicks)
            throw new ArgumentOutOfRangeException(nameof(record));
        if (record.RepeatCount <= 0) throw new ArgumentOutOfRangeException(nameof(record));

        if (record.Kind == DecisionJournalRecordKind.DecisionSpan)
        {
            ValidateDecision(in record);
            return;
        }
        ValidateTransition(in record);
    }

    private static void ValidateDecision(in DecisionJournalRecord record)
    {
        if (!record.Service.IsValid || record.Lifecycle == 0 || record.Configuration == 0 ||
            (record.FirstCycle == 0) != (record.LastCycle == 0) ||
            record.LastCycle < record.FirstCycle ||
            record.TransitionCode != 0)
        {
            throw new ArgumentException("The decision journal identity is malformed.", nameof(record));
        }
        if (record.FirstCycle != 0 &&
            !RangeMatchesRepeat(record.FirstCycle, record.LastCycle, record.RepeatCount))
        {
            throw new ArgumentException("The journal cycle range is malformed.", nameof(record));
        }
        ValidateDecisionCode(record.StartDecisionCode, nameof(record));
        ValidateDecisionCode(record.CaptureDecisionCode, nameof(record));
        if (record.FirstCycle == 0 && record.CaptureDecisionCode != 0)
            throw new ArgumentException("A capture decision requires a cycle identity.", nameof(record));
        if (record.FirstCycle != 0)
        {
            if (record.CaptureDecisionCode == CommonServiceDecisionCodes.Captured.Value && record.Strategy == 0)
                throw new ArgumentException("A captured decision requires its strategy generation.", nameof(record));
            if (record.CaptureDecisionCode == CommonServiceDecisionCodes.CaptureUnavailable.Value &&
                record.Strategy != 0)
            {
                throw new ArgumentException("An unavailable capture cannot claim a strategy generation.", nameof(record));
            }
            // No capture decision is the ordinary shape, which has no capture at all — that record
            // still names the strategy generation its cycle ran against. Without one, the only thing
            // it can be is a capture that faulted before a cycle existed.
            if (record.CaptureDecisionCode == 0 && record.Strategy == 0 &&
                record.FaultCategory != ServiceFaultCategory.Capture)
            {
                throw new ArgumentException(
                    "A cycle with neither a strategy generation nor a capture decision must be a capture fault.",
                    nameof(record));
            }
        }
        if (!record.HasWake && record.Wake != default || record.HasWake && !record.Wake.IsValid)
            throw new ArgumentException("The decision journal wake is malformed.", nameof(record));
        if (!record.HasProjection && record.Projection.Count != 0)
            throw new ArgumentException("Projection payload requires its presence flag.", nameof(record));
        if (record.FaultCategory == 0)
        {
            if (record.FaultCode != 0 || record.FirstFaultOccurrence != 0 || record.LastFaultOccurrence != 0)
                throw new ArgumentException("Fault payload requires a fault category.", nameof(record));
        }
        else if (record.FaultCategory is < ServiceFaultCategory.Capture or
                 > ServiceFaultCategory.Start ||
                 !IsFaultCode(record.FaultCode) ||
                 record.FirstFaultOccurrence <= 0 ||
                 record.LastFaultOccurrence < record.FirstFaultOccurrence)
        {
            throw new ArgumentException("The decision journal fault is malformed.", nameof(record));
        }

        if (record.TerminalDisposition == 0)
        {
            if (record.TerminalResultCode != 0 || record.ActionCount != 0 ||
                record.CommittedActions != 0 || record.PublishedActions != 0 ||
                record.NativeCallsAttempted != 0 ||
                record.MutationAttempts != 0 || record.MutationsCommitted != 0)
            {
                throw new ArgumentException("Terminal totals require a terminal disposition.", nameof(record));
            }
            return;
        }
        ValidateTerminal(in record);
    }

    private static void ValidateTerminal(in DecisionJournalRecord record)
    {
        var maximumCommittedActions = SaturatingMultiply(record.ActionCount, record.RepeatCount);
        if (record.FirstCycle == 0 || record.Strategy == 0 ||
            record.TerminalDisposition is < BatchTerminalDisposition.Completed or
            > BatchTerminalDisposition.Orphaned ||
            record.ActionCount < 0 || record.CommittedActions < 0 ||
            record.CommittedActions > maximumCommittedActions ||
            record.PublishedActions < 0 ||
            record.PublishedActions > record.CommittedActions ||
            record.NativeCallsAttempted < 0 || record.MutationAttempts < 0 ||
            record.MutationsCommitted < 0 ||
            record.MutationsCommitted > record.MutationAttempts ||
            record.MutationAttempts > record.NativeCallsAttempted)
        {
            throw new ArgumentException("The decision journal terminal is malformed.", nameof(record));
        }

        // Only an action that could call the game owes native evidence. A publication commits by
        // handing over a snapshot, so it is subtracted from both expectations before they are made;
        // a span whose every action published owes no evidence at all and must carry none.
        var nativeActions = maximumCommittedActions - record.PublishedActions;
        var nativeCommitted = record.CommittedActions - record.PublishedActions;
        var coherent = record.TerminalDisposition switch
        {
            BatchTerminalDisposition.Completed =>
                record.TerminalResultCode == CommonActionResultCodes.Committed.Value &&
                (nativeActions == 0
                    ? IsZeroNativeTotals(in record)
                    : record.MutationAttempts >= nativeActions &&
                        record.MutationsCommitted >= nativeCommitted &&
                        (nativeCommitted != 0 || record.MutationsCommitted == 0)),
            BatchTerminalDisposition.Rejected =>
                record.ActionCount > 0 &&
                IsRejectedCode(record.TerminalResultCode) &&
                record.CommittedActions <= SaturatingMultiply(record.ActionCount - 1, record.RepeatCount) &&
                record.MutationsCommitted >= nativeCommitted &&
                (nativeCommitted != 0 || record.MutationsCommitted == 0),
            BatchTerminalDisposition.Faulted =>
                record.ActionCount > 0 &&
                IsFaultedCode(record.TerminalResultCode) &&
                record.CommittedActions <= SaturatingMultiply(record.ActionCount - 1, record.RepeatCount) &&
                record.MutationsCommitted >= nativeCommitted &&
                (nativeCommitted != 0 || record.MutationsCommitted == 0),
            BatchTerminalDisposition.Orphaned =>
                record.TerminalResultCode == CommonActionResultCodes.LifecycleReplaced.Value &&
                record.MutationsCommitted >= nativeCommitted &&
                (nativeCommitted != 0 || record.MutationsCommitted == 0),
            _ => false,
        };
        if (!coherent)
            throw new ArgumentException("The decision journal terminal is malformed.", nameof(record));
    }

    /// <summary>
    /// Whether a transition names the suite rather than a service. Configuration and strategy do:
    /// the suite publishes one of each and every service reads it, so a change is one record.
    /// </summary>
    internal static bool IsSuiteWide(DecisionJournalRecordKind kind) =>
        kind is DecisionJournalRecordKind.ConfigurationChanged or
            DecisionJournalRecordKind.StrategyChanged or
            DecisionJournalRecordKind.EmergencyEntered or
            DecisionJournalRecordKind.EmergencyCleared;

    private static void ValidateTransition(in DecisionJournalRecord record)
    {
        var global = IsSuiteWide(record.Kind);
        var expectedGeneration = record.Kind switch
        {
            DecisionJournalRecordKind.ConfigurationChanged => record.Configuration,
            DecisionJournalRecordKind.StrategyChanged => record.Strategy,
            DecisionJournalRecordKind.LifecycleChanged or
                DecisionJournalRecordKind.WorldGateHeld => record.Lifecycle,
            _ => 0UL,
        };
        var emergency = record.Kind is DecisionJournalRecordKind.EmergencyEntered or
            DecisionJournalRecordKind.EmergencyCleared;
        if (global == record.Service.IsValid || !emergency && expectedGeneration == 0 ||
            record.Lifecycle != (record.Kind is DecisionJournalRecordKind.LifecycleChanged or
                DecisionJournalRecordKind.WorldGateHeld ? expectedGeneration : 0) ||
            record.Configuration != (record.Kind == DecisionJournalRecordKind.ConfigurationChanged ? expectedGeneration : 0) ||
            record.Strategy != (record.Kind == DecisionJournalRecordKind.StrategyChanged ? expectedGeneration : 0) ||
            record.TransitionCode < 0 ||
            record.RepeatCount != 1 || record.FirstTimestampTicks != record.LastTimestampTicks ||
            record.FirstCycle != 0 || record.LastCycle != 0 ||
            record.StartDecisionCode != 0 || record.CaptureDecisionCode != 0 ||
            record.HasWake || record.Wake != default || record.HasProjection ||
            record.Projection.Count != 0 || record.FaultCategory != 0 ||
            record.FaultCode != 0 || record.FirstFaultOccurrence != 0 ||
            record.LastFaultOccurrence != 0 || record.TerminalDisposition != 0 ||
            record.TerminalResultCode != 0 || record.ActionCount != 0 ||
            record.CommittedActions != 0 || record.PublishedActions != 0 ||
            record.NativeCallsAttempted != 0 ||
            record.MutationAttempts != 0 || record.MutationsCommitted != 0)
        {
            throw new ArgumentException("The decision journal transition is malformed.", nameof(record));
        }
    }

    private static void ValidateDecisionCode(int code, string parameterName)
    {
        if (code != 0 && code is not (>= 1 and <= 5) && code < ServiceDecisionCode.FirstFeatureCode)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static bool IsFaultCode(int code) =>
        code == CommonActionResultCodes.AdapterFault.Value ||
        code >= ServiceActionResultCode.FirstFeatureCode;

    private static bool IsRejectedCode(int code) =>
        code is >= 2 and <= 6 || code >= ServiceActionResultCode.FirstFeatureCode;

    private static bool IsFaultedCode(int code) =>
        code == CommonActionResultCodes.AdapterFault.Value ||
        code >= ServiceActionResultCode.FirstFeatureCode;

    private static bool RangeMatchesRepeat(ulong first, ulong last, long repeatCount) =>
        last - first == (ulong)(repeatCount - 1);

    private static bool IsZeroNativeTotals(in DecisionJournalRecord record) =>
        record.NativeCallsAttempted == 0 &&
        record.MutationAttempts == 0 &&
        record.MutationsCommitted == 0;

    private static long SaturatingMultiply(int value, long count) =>
        value == 0 || count == 0 ? 0 : count > long.MaxValue / value
            ? long.MaxValue
            : value * count;
}
