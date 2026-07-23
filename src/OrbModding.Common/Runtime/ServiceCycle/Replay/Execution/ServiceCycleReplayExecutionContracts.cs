using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// A copied wire record supplied by the validated artifact boundary. Execution never decodes a record
/// before the containing artifact has passed its complete structural and causal validation gate.
/// </summary>
public readonly struct ServiceCycleReplayEncodedRecord
{
    public ServiceCycleReplayEncodedRecord(
        ServiceCycleReplayRecordIdentity identity,
        ushort schemaVersion,
        ReadOnlyMemory<byte> payload)
    {
        if (!identity.IsValid) throw new ArgumentException("A valid record identity is required.", nameof(identity));
        if (schemaVersion == 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        Identity = identity;
        SchemaVersion = schemaVersion;
        Payload = payload;
    }

    public ServiceCycleReplayRecordIdentity Identity { get; }
    public ushort SchemaVersion { get; }
    public ReadOnlyMemory<byte> Payload { get; }
}

/// <summary>Stable execution-specific detail codes. Values are evidence and must not be renumbered.</summary>
public enum ServiceCycleReplayExecutionDetailCode
{
    ArtifactNotComplete = 1,
    RegistrationMissing = 2,
    RegistrationDuplicated = 3,
    RegistrationKeyGap = 4,
    RecordIdentityRejected = 5,
    RecordSchemaRejected = 6,
    RecordSizeRejected = 7,
    RecordCanonicalEncodingRejected = 8,
    HydrationRejected = 9,
    EvaluatorDidNotFinish = 10,
    ControlOrderRejected = 11,
    NativeScriptRejected = 12,
    SemanticEventCountRejected = 13,
    SemanticEventBytesRejected = 14,
    ProductionStateFactoryRejected = 15,
    DetachedRecordBytesRejected = 16,
    ConfigurationEvidenceMissing = 17,
    StrategyEvidenceMissing = 18,
    CodecDescriptorRejected = 19,
    CaptureEvidenceMissing = 20,
    ClockEvidenceRejected = 21,
    InPumpControlUnsupported = 22,
    LifecycleConstructionEvidenceUnsupported = 23,
    ProductionPreparationRejected = 24,
    ProductionRegistrationRejected = 25,
    ProductionPumpRejected = 26,
    ProductionComparisonRejected = 27,
    ProductionCleanupRejected = 28,
    DetachedPreparationRejected = 29,
    DetachedCleanupRejected = 30,
}

public readonly struct ServiceCycleReplayCycleFailure
{
    public ServiceCycleReplayCycleFailure(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayFault fault)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid replay cycle is required.", nameof(cycle));
        if (!fault.IsValid) throw new ArgumentException("A valid replay fault is required.", nameof(fault));
        Cycle = cycle;
        Fault = fault;
    }

    public ServiceCycleReplayCycleKey Cycle { get; }
    public ServiceCycleReplayFault Fault { get; }
    public bool IsValid => Cycle.IsValid && Fault.IsValid;
}

public readonly struct ServiceCycleReplayCycleMismatch
{
    public ServiceCycleReplayCycleMismatch(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayMismatch mismatch)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid replay cycle is required.", nameof(cycle));
        if (!mismatch.IsValid) throw new ArgumentException("A valid replay mismatch is required.", nameof(mismatch));
        Cycle = cycle;
        Mismatch = mismatch;
    }

    public ServiceCycleReplayCycleKey Cycle { get; }
    public ServiceCycleReplayMismatch Mismatch { get; }
    public bool IsValid => Cycle.IsValid && Mismatch.IsValid;
}

public readonly struct ServiceCycleReplayExecutionResult
{
    private ServiceCycleReplayExecutionResult(
        bool succeeded,
        int completedCycles,
        ServiceCycleReplayCycleFailure failure,
        ServiceCycleReplayCycleMismatch mismatch)
    {
        Succeeded = succeeded;
        CompletedCycles = completedCycles;
        Failure = failure;
        Mismatch = mismatch;
    }

    public bool Succeeded { get; }
    public int CompletedCycles { get; }
    public ServiceCycleReplayCycleFailure Failure { get; }
    public ServiceCycleReplayCycleMismatch Mismatch { get; }
    public bool IsValid => CompletedCycles >= 0 && (Succeeded
        ? !Failure.IsValid && !Mismatch.IsValid
        : Failure.IsValid != Mismatch.IsValid);

    public static ServiceCycleReplayExecutionResult Success(int completedCycles)
    {
        if (completedCycles < 0) throw new ArgumentOutOfRangeException(nameof(completedCycles));
        return new ServiceCycleReplayExecutionResult(true, completedCycles, default, default);
    }

    public static ServiceCycleReplayExecutionResult Faulted(
        int completedCycles,
        in ServiceCycleReplayCycleFailure failure)
    {
        if (completedCycles < 0) throw new ArgumentOutOfRangeException(nameof(completedCycles));
        if (!failure.IsValid) throw new ArgumentException("A valid failure is required.", nameof(failure));
        return new ServiceCycleReplayExecutionResult(false, completedCycles, failure, default);
    }

    public static ServiceCycleReplayExecutionResult Diverged(
        int completedCycles,
        in ServiceCycleReplayCycleMismatch mismatch)
    {
        if (completedCycles < 0) throw new ArgumentOutOfRangeException(nameof(completedCycles));
        if (!mismatch.IsValid) throw new ArgumentException("A valid mismatch is required.", nameof(mismatch));
        return new ServiceCycleReplayExecutionResult(false, completedCycles, default, mismatch);
    }
}

/// <summary>
/// Feature-owned reconstruction of values required by the isolated evaluator oracle. Production replay
/// uses the same input/config hydration but deliberately obtains state from the production state factory.
/// </summary>
public interface IServiceCycleReplayHydrator<TFrame, TConfig, TState, TCycleInputRecord, TStateRecord>
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
{
    void HydrateFrame(
        in TCycleInputRecord input,
        in ServiceCycleReplayContext context,
        ref TFrame frame);
    TConfig HydrateConfiguration(
        in TCycleInputRecord input,
        in ServiceCycleReplayContext context);
    TState HydratePreviousState(
        in TStateRecord previousState,
        in ServiceCycleReplayContext context);
    TCycleInputRecord RecreateCycleInputRecord(
        in TFrame frame,
        in TConfig config,
        in ServiceCycleReplayContext context);
}

public readonly struct ServiceCycleReplayDecodedCycle<TCycleInputRecord, TStateRecord, TActionRecord>
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly TActionRecord[] _actions;

    public ServiceCycleReplayDecodedCycle(
        ServiceCycleReplayContext context,
        TCycleInputRecord input,
        TStateRecord previousState,
        TStateRecord nextState,
        TActionRecord[] actions,
        WakePolicy wake,
        ServiceStateProjectionSnapshot projection,
        StatePublicationId statePublication,
        MonotonicTimestamp projectedAt)
    {
        if (!context.Cycle.IsValid) throw new ArgumentException("A valid replay context is required.", nameof(context));
        Context = context;
        Input = input;
        PreviousState = previousState;
        NextState = nextState;
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        _actions = actions.Length == 0 ? Array.Empty<TActionRecord>() : (TActionRecord[])actions.Clone();
        Wake = wake;
        Projection = projection;
        StatePublication = statePublication;
        ProjectedAt = projectedAt;
    }

    public ServiceCycleReplayContext Context { get; }
    public TCycleInputRecord Input { get; }
    public TStateRecord PreviousState { get; }
    public TStateRecord NextState { get; }
    public int ActionCount => _actions?.Length ?? 0;
    public TActionRecord GetAction(int index)
    {
        if ((uint)index >= (uint)ActionCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _actions[index];
    }
    public WakePolicy Wake { get; }
    public ServiceStateProjectionSnapshot Projection { get; }
    public StatePublicationId StatePublication { get; }
    public MonotonicTimestamp ProjectedAt { get; }
}
