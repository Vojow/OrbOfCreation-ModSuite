using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal enum DecisionJournalRecordKind
{
    DecisionSpan = 1,
    ConfigurationChanged = 2,
    StrategyChanged = 3,
    LifecycleChanged = 4,
    EmergencyEntered = 5,
    EmergencyCleared = 6,
}

internal readonly struct DecisionJournalRecord
{
    internal DecisionJournalRecord(
        DecisionJournalRecordKind kind,
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong configuration,
        ulong strategy,
        ulong firstCapture,
        ulong lastCapture,
        ulong firstCycle,
        ulong lastCycle,
        long firstTimestampTicks,
        long lastTimestampTicks,
        long repeatCount,
        int startDecisionCode,
        int captureDecisionCode,
        bool hasWake,
        WakePolicy wake,
        bool hasProjection,
        in ServiceStateProjectionSnapshot projection,
        ServiceFaultCategory faultCategory,
        int faultCode,
        int firstFaultOccurrence,
        int lastFaultOccurrence,
        BatchTerminalDisposition terminalDisposition,
        int terminalResultCode,
        int actionCount,
        long committedActions,
        long nativeCallsAttempted,
        long mutationAttempts,
        long mutationsCommitted,
        int transitionCode)
    {
        Kind = kind;
        Service = service;
        Lifecycle = lifecycle;
        Configuration = configuration;
        Strategy = strategy;
        FirstCapture = firstCapture;
        LastCapture = lastCapture;
        FirstCycle = firstCycle;
        LastCycle = lastCycle;
        FirstTimestampTicks = firstTimestampTicks;
        LastTimestampTicks = lastTimestampTicks;
        RepeatCount = repeatCount;
        StartDecisionCode = startDecisionCode;
        CaptureDecisionCode = captureDecisionCode;
        HasWake = hasWake;
        Wake = wake;
        HasProjection = hasProjection;
        Projection = projection;
        FaultCategory = faultCategory;
        FaultCode = faultCode;
        FirstFaultOccurrence = firstFaultOccurrence;
        LastFaultOccurrence = lastFaultOccurrence;
        TerminalDisposition = terminalDisposition;
        TerminalResultCode = terminalResultCode;
        ActionCount = actionCount;
        CommittedActions = committedActions;
        NativeCallsAttempted = nativeCallsAttempted;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
        TransitionCode = transitionCode;
    }

    internal DecisionJournalRecordKind Kind { get; }
    internal ServiceCycleTraceServiceId Service { get; }
    internal ulong Lifecycle { get; }
    internal ulong Configuration { get; }
    internal ulong Strategy { get; }
    internal ulong FirstCapture { get; }
    internal ulong LastCapture { get; }
    internal ulong FirstCycle { get; }
    internal ulong LastCycle { get; }
    internal long FirstTimestampTicks { get; }
    internal long LastTimestampTicks { get; }
    internal long RepeatCount { get; }
    internal int StartDecisionCode { get; }
    internal int CaptureDecisionCode { get; }
    internal bool HasWake { get; }
    internal WakePolicy Wake { get; }
    internal bool HasProjection { get; }
    internal ServiceStateProjectionSnapshot Projection { get; }
    internal ServiceFaultCategory FaultCategory { get; }
    internal int FaultCode { get; }
    internal int FirstFaultOccurrence { get; }
    internal int LastFaultOccurrence { get; }
    internal BatchTerminalDisposition TerminalDisposition { get; }
    internal int TerminalResultCode { get; }
    internal int ActionCount { get; }
    internal long CommittedActions { get; }
    internal long NativeCallsAttempted { get; }
    internal long MutationAttempts { get; }
    internal long MutationsCommitted { get; }
    internal int TransitionCode { get; }

    internal static DecisionJournalRecord Decision(in DecisionJournalObservation observation)
    {
        var terminal = observation.Terminal;
        var native = terminal.NativeCallOutcome;
        var fault = observation.Fault;
        var record = new DecisionJournalRecord(
            DecisionJournalRecordKind.DecisionSpan,
            observation.Service,
            observation.Lifecycle,
            observation.Configuration,
            observation.Strategy,
            observation.Capture,
            observation.Capture,
            observation.Cycle,
            observation.Cycle,
            observation.FirstObservedAt.Ticks,
            observation.LastObservedAt.Ticks,
            1,
            observation.StartDecisionCode,
            observation.CaptureDecisionCode,
            observation.HasWake,
            observation.Wake,
            observation.HasProjection,
            observation.Projection,
            fault.Category,
            fault.Code.Value,
            fault.OccurrenceCount,
            fault.OccurrenceCount,
            terminal.Disposition,
            terminal.ResultCode.Value,
            terminal.ActionCount,
            terminal.CommittedCount,
            native.NativeCallsAttempted,
            native.MutationAttempts,
            native.MutationsCommitted,
            0);
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    internal static DecisionJournalRecord Transition(
        DecisionJournalRecordKind kind,
        ServiceCycleTraceServiceId service,
        ulong generation,
        MonotonicTimestamp observedAt,
        int code = 0)
    {
        if (kind is DecisionJournalRecordKind.DecisionSpan)
            throw new ArgumentOutOfRangeException(nameof(kind));
        var global = kind is DecisionJournalRecordKind.EmergencyEntered or
            DecisionJournalRecordKind.EmergencyCleared;
        if (global != !service.IsValid)
            throw new ArgumentException("Global transitions must not carry a service identity.", nameof(service));
        if (!global && generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));

        var record = new DecisionJournalRecord(
            kind, service,
            kind == DecisionJournalRecordKind.LifecycleChanged ? generation : 0,
            kind == DecisionJournalRecordKind.ConfigurationChanged ? generation : 0,
            kind == DecisionJournalRecordKind.StrategyChanged ? generation : 0,
            0, 0, 0, 0,
            observedAt.Ticks, observedAt.Ticks, 1,
            0, 0, false, default, false, default,
            default, 0, 0, 0, default, 0, 0,
            0, 0, 0, 0, code);
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    internal bool CanCoalesceWith(in DecisionJournalRecord next) =>
        Kind == DecisionJournalRecordKind.DecisionSpan &&
        next.Kind == Kind &&
        Service == next.Service &&
        Lifecycle == next.Lifecycle &&
        Configuration == next.Configuration &&
        Strategy == next.Strategy &&
        StartDecisionCode == next.StartDecisionCode &&
        CaptureDecisionCode == next.CaptureDecisionCode &&
        HasWake == next.HasWake &&
        (!HasWake || Wake == next.Wake) &&
        HasProjection == next.HasProjection &&
        (!HasProjection || DecisionJournalProjection.Equals(Projection, next.Projection)) &&
        FaultCategory == next.FaultCategory &&
        FaultCode == next.FaultCode &&
        next.LastFaultOccurrence >= LastFaultOccurrence &&
        TerminalDisposition == next.TerminalDisposition &&
        TerminalResultCode == next.TerminalResultCode &&
        ActionCount == next.ActionCount &&
        next.FirstTimestampTicks >= LastTimestampTicks &&
        SequenceFollows(LastCapture, next.FirstCapture) &&
        SequenceFollows(LastCycle, next.FirstCycle);

    internal DecisionJournalRecord Coalesce(in DecisionJournalRecord next)
    {
        if (!CanCoalesceWith(in next))
            throw new ArgumentException("The journal decisions are not equivalent and consecutive.", nameof(next));
        var record = new DecisionJournalRecord(
            Kind, Service, Lifecycle, Configuration, Strategy,
            FirstCapture, next.LastCapture, FirstCycle, next.LastCycle,
            FirstTimestampTicks, next.LastTimestampTicks,
            SaturatingAdd(RepeatCount, next.RepeatCount),
            StartDecisionCode, CaptureDecisionCode, HasWake, Wake,
            HasProjection, Projection,
            FaultCategory, FaultCode, FirstFaultOccurrence, next.LastFaultOccurrence,
            TerminalDisposition, TerminalResultCode, ActionCount,
            SaturatingAdd(CommittedActions, next.CommittedActions),
            SaturatingAdd(NativeCallsAttempted, next.NativeCallsAttempted),
            SaturatingAdd(MutationAttempts, next.MutationAttempts),
            SaturatingAdd(MutationsCommitted, next.MutationsCommitted),
            0);
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    private static bool SequenceFollows(ulong previous, ulong next) =>
        previous == 0 ? next == 0 : previous != ulong.MaxValue && next == previous + 1;

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;
}
