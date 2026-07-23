#if SERVICE_CYCLE_PROFILE
namespace OrbModding.ServiceCycleTrace.Dashboard;

internal sealed record TraceDashboardDocument(
    int SchemaVersion,
    TraceDashboardMetadata Metadata,
    TraceDashboardPump[] Pumps,
    TraceDashboardEvent[] Events,
    TraceDashboardDecision[] Decisions,
    TraceDashboardStageAggregate[] StageAggregates,
    TraceDashboardStageSample[] StageSamples);

internal sealed record TraceDashboardMetadata(
    string FullTraceSession,
    string FullTraceState,
    string FullTraceReason,
    string ProfileSession,
    string ProfileState,
    string ProfileReason,
    string JournalRun,
    double WindowMilliseconds,
    ulong FullTraceRecords,
    ulong ProfileRecords,
    int JournalRecords,
    bool SemanticTraceActive,
    bool AllocationAvailable);

internal sealed record TraceDashboardPump(
    double OffsetMilliseconds,
    long Frame,
    bool Accepted,
    double TotalMilliseconds,
    double ResponseMilliseconds,
    double CaptureMilliseconds,
    double ActionMilliseconds,
    int Responses,
    int Captures,
    int Actions,
    long LifecycleTransitions);

internal sealed record TraceDashboardEvent(
    double OffsetMilliseconds,
    string Kind,
    string Lane,
    ulong Service,
    ulong Lifecycle,
    ulong Capture,
    ulong Cycle,
    ulong Batch,
    ulong Action,
    long Frame,
    double DurationMilliseconds,
    int Code,
    int Disposition,
    int ActionCount,
    int CommittedCount,
    long NativeCallsAttempted,
    long MutationAttempts,
    long MutationsCommitted);

internal sealed record TraceDashboardDecision(
    double StartMilliseconds,
    double EndMilliseconds,
    ulong Service,
    ulong FirstCycle,
    ulong LastCycle,
    long RepeatCount,
    string StartDecision,
    string CaptureDecision,
    string Wake,
    string Terminal,
    int ActionCount,
    long CommittedActions,
    long NativeCallsAttempted,
    long MutationAttempts,
    long MutationsCommitted,
    TraceDashboardProjectionEntry[] Projection);

internal sealed record TraceDashboardProjectionEntry(
    int Key,
    string Name,
    string Kind,
    string Value);

internal sealed record TraceDashboardStageAggregate(
    int StageCode,
    string Stage,
    int Service,
    string Temperature,
    ulong Count,
    double TotalMicroseconds,
    double AverageMicroseconds,
    double MinimumMicroseconds,
    double MaximumMicroseconds,
    double? AllocationPerCall,
    TraceDashboardOperations Operations);

internal sealed record TraceDashboardStageSample(
    double OffsetMilliseconds,
    int StageCode,
    string Stage,
    int Service,
    ulong Cycle,
    ulong Frame,
    string Temperature,
    double ElapsedMicroseconds,
    long? AllocatedBytes,
    TraceDashboardOperations Operations);

internal sealed record TraceDashboardOperations(
    uint ReflectedFieldReads,
    uint ReflectedMethodCalls,
    uint StableIdReads,
    uint ListEntries,
    uint SelectedPairs,
    uint ReadyPairs,
    uint InvocationArgumentArrays,
    uint RecordCopies);
#endif
