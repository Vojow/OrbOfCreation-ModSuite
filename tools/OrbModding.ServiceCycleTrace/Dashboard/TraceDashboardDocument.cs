#if SERVICE_CYCLE_PROFILE
namespace OrbModding.ServiceCycleTrace.Dashboard;

internal sealed record TraceDashboardDocument(
    int SchemaVersion,
    TraceDashboardMetadata Metadata,
    TraceDashboardService[] Services,
    TraceDashboardPump[] Pumps,
    TraceDashboardCycle[] Cycles,
    TraceDashboardEvent[] Events,
    TraceDashboardDecision[] Decisions,
    TraceDashboardStageAggregate[] StageAggregates,
    TraceDashboardStageSample[] StageSamples);

/// <summary>
/// One registered service, as much of it as the capture can answer for.
/// </summary>
/// <param name="Name">
/// From the roster the capture recorded, which is the only source that actually knows. The rest is
/// inference kept for captures written before rosters existed: the service's own profile spans when it
/// owns a block, and its observed shape otherwise — a service whose every commit published rather than
/// mutated is the world collector, because <c>ServiceActionResult</c> refuses to build a native commit
/// without native evidence. A service with no roster entry, no spans, and no action in the window
/// cannot be named and says so.
/// </param>
internal sealed record TraceDashboardService(
    int Ordinal,
    ulong Service,
    string Name,
    string Role,
    int Cycles,
    bool Named);

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
    bool AllocationAvailable,
    string[] Notes);

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
    int Started,
    int Held,
    long LifecycleTransitions,
    string Temperature);

/// <summary>
/// One service cycle, split into the stages the wire can already answer for.
/// </summary>
/// <remarks>
/// <para>
/// Capture and dispatch run on the main thread inside a pump frame and name it. Handoff, derive and
/// project run off it: handoff is queue latency rather than CPU, and derive and project belong to no
/// frame at all, so they are drawn on their own time axis and never inside a frame's stack.
/// </para>
/// <para>
/// Derive is the evaluator's own work <em>and</em> the snapshot allocation together, because
/// <c>GameWorldFrameDeriver.Build</c> derives and allocates in one pass and there is no seam between
/// them to measure. The separable axis is per category, not maths against allocation.
/// </para>
/// </remarks>
internal sealed record TraceDashboardCycle(
    double OffsetMilliseconds,
    int Ordinal,
    ulong Service,
    ulong Lifecycle,
    ulong Cycle,
    long CaptureFrame,
    long DispatchFrame,
    string Temperature,
    double CaptureMilliseconds,
    double HandoffMilliseconds,
    double DeriveMilliseconds,
    double ProjectMilliseconds,
    double DispatchMilliseconds,
    double WorkerAtMilliseconds,
    double DispatchAtMilliseconds,
    int ActionCount,
    int Committed,
    int Skipped,
    int Failed,
    bool HasCapture,
    bool HasWorker);

internal sealed record TraceDashboardEvent(
    double OffsetMilliseconds,
    string Kind,
    string Lane,
    ulong Service,
    ulong Lifecycle,
    ulong World,
    ulong Capture,
    ulong Cycle,
    ulong Batch,
    ulong Action,
    long Frame,
    bool Framed,
    double DurationMilliseconds,
    int Code,
    int Disposition,
    int ActionCount,
    int CommittedCount,
    long NativeCallsAttempted,
    long MutationAttempts,
    long MutationsCommitted);

internal sealed record TraceDashboardDecision(
    string Kind,
    double StartMilliseconds,
    double EndMilliseconds,
    ulong Service,
    ulong FirstCycle,
    ulong LastCycle,
    long RepeatCount,
    string StartDecision,
    string CaptureDecision,
    string Terminal,
    int WorkerSamples,
    double WorkerAverageMilliseconds,
    double? WorkerMicrosecondsPerCapturedCandidate,
    double? WorkerMicrosecondsPerPlannedAction,
    int ActionOrdinal,
    string CandidateId,
    string NativeType,
    string ListId,
    string ViewId,
    string RouteStatus,
    string Outcome);

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
    uint InvocationArgumentArrays,
    uint RecordCopies);
#endif
