using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

/// <summary>Stable scopes for replay evidence failures. Values must not be renumbered.</summary>
public enum ServiceCycleReplayFailureScope
{
    Record = 1,
    Container = 2,
    SemanticTrace = 3,
    Cycle = 4,
    Execution = 5,
}

/// <summary>
/// Locates a replay failure without inventing a detached-record identity for container, trace, cycle,
/// or execution failures.
/// </summary>
public readonly struct ServiceCycleReplayFailureLocation : IEquatable<ServiceCycleReplayFailureLocation>
{
    private ServiceCycleReplayFailureLocation(
        ServiceCycleReplayFailureScope scope,
        ServiceCycleReplayRecordIdentity record)
    {
        Scope = scope;
        Record = record;
    }

    public static ServiceCycleReplayFailureLocation AtRecord(ServiceCycleReplayRecordIdentity record)
    {
        if (!record.IsValid) throw new ArgumentException("A valid detached record is required.", nameof(record));
        return new ServiceCycleReplayFailureLocation(ServiceCycleReplayFailureScope.Record, record);
    }

    public static ServiceCycleReplayFailureLocation Container =>
        new(ServiceCycleReplayFailureScope.Container, default);
    public static ServiceCycleReplayFailureLocation SemanticTrace =>
        new(ServiceCycleReplayFailureScope.SemanticTrace, default);
    public static ServiceCycleReplayFailureLocation Cycle =>
        new(ServiceCycleReplayFailureScope.Cycle, default);
    public static ServiceCycleReplayFailureLocation Execution =>
        new(ServiceCycleReplayFailureScope.Execution, default);

    public ServiceCycleReplayFailureScope Scope { get; }
    public ServiceCycleReplayRecordIdentity Record { get; }
    public bool IsValid => Scope == ServiceCycleReplayFailureScope.Record
        ? Record.IsValid
        : Scope is >= ServiceCycleReplayFailureScope.Container and <= ServiceCycleReplayFailureScope.Execution &&
            !Record.IsValid;
    public bool Equals(ServiceCycleReplayFailureLocation other) => Scope == other.Scope && Record == other.Record;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayFailureLocation other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Scope, Record);
    public static bool operator ==(ServiceCycleReplayFailureLocation left, ServiceCycleReplayFailureLocation right) =>
        left.Equals(right);
    public static bool operator !=(ServiceCycleReplayFailureLocation left, ServiceCycleReplayFailureLocation right) =>
        !left.Equals(right);
}

/// <summary>Stable reasons why an exact replay payload is unavailable.</summary>
public enum ServiceCycleReplayCompletenessCode
{
    Complete = 1,
    ByteBudgetExhausted = 2,
    CodecContractRejected = 3,
    CodecFaulted = 4,
    RecordTypeRejected = 5,
    RequiredRecordMissing = 6,
    SemanticTraceIncomplete = 7,
    ContainerIncomplete = 8,
    CycleIncomplete = 9,
    ExecutionIncomplete = 10,
    RecordCapacityExhausted = 11,
}

public readonly struct ServiceCycleReplayCompleteness : IEquatable<ServiceCycleReplayCompleteness>
{
    private ServiceCycleReplayCompleteness(
        ServiceCycleReplayCompletenessCode code,
        ServiceCycleReplayFailureLocation failureLocation)
    {
        Code = code;
        FailureLocation = failureLocation;
    }

    public static ServiceCycleReplayCompleteness Complete =>
        new(ServiceCycleReplayCompletenessCode.Complete, default);

    public static ServiceCycleReplayCompleteness Incomplete(
        ServiceCycleReplayCompletenessCode code,
        ServiceCycleReplayFailureLocation failureLocation)
    {
        if (!IsIncompleteCode(code))
            throw new ArgumentOutOfRangeException(nameof(code));
        if (!failureLocation.IsValid)
            throw new ArgumentException("A valid failure location is required.", nameof(failureLocation));
        if (!ScopeMatches(code, failureLocation.Scope))
            throw new ArgumentException("The failure location does not match the completeness code.", nameof(failureLocation));
        return new ServiceCycleReplayCompleteness(code, failureLocation);
    }

    public ServiceCycleReplayCompletenessCode Code { get; }
    public ServiceCycleReplayFailureLocation FailureLocation { get; }
    public bool IsComplete => Code == ServiceCycleReplayCompletenessCode.Complete;
    public bool IsValid => IsComplete && !FailureLocation.IsValid ||
        IsIncompleteCode(Code) && FailureLocation.IsValid &&
            ScopeMatches(Code, FailureLocation.Scope);
    public bool Equals(ServiceCycleReplayCompleteness other) =>
        Code == other.Code && FailureLocation == other.FailureLocation;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayCompleteness other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Code, FailureLocation);
    public static bool operator ==(ServiceCycleReplayCompleteness left, ServiceCycleReplayCompleteness right) =>
        left.Equals(right);
    public static bool operator !=(ServiceCycleReplayCompleteness left, ServiceCycleReplayCompleteness right) =>
        !left.Equals(right);

    private static bool ScopeMatches(ServiceCycleReplayCompletenessCode code, ServiceCycleReplayFailureScope scope) =>
        code switch
        {
            >= ServiceCycleReplayCompletenessCode.ByteBudgetExhausted and
                <= ServiceCycleReplayCompletenessCode.RequiredRecordMissing =>
                scope == ServiceCycleReplayFailureScope.Record,
            ServiceCycleReplayCompletenessCode.RecordCapacityExhausted =>
                scope == ServiceCycleReplayFailureScope.Record,
            ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete =>
                scope == ServiceCycleReplayFailureScope.SemanticTrace,
            ServiceCycleReplayCompletenessCode.ContainerIncomplete =>
                scope == ServiceCycleReplayFailureScope.Container,
            ServiceCycleReplayCompletenessCode.CycleIncomplete =>
                scope == ServiceCycleReplayFailureScope.Cycle,
            ServiceCycleReplayCompletenessCode.ExecutionIncomplete =>
                scope == ServiceCycleReplayFailureScope.Execution,
            _ => false,
        };

    private static bool IsIncompleteCode(ServiceCycleReplayCompletenessCode code) =>
        code is >= ServiceCycleReplayCompletenessCode.ByteBudgetExhausted and
            <= ServiceCycleReplayCompletenessCode.ExecutionIncomplete or
            ServiceCycleReplayCompletenessCode.RecordCapacityExhausted;
}

/// <summary>Stable replay failure categories. No exception text crosses this boundary.</summary>
public enum ServiceCycleReplayFaultCode
{
    RecordTypeRejected = 1,
    CodecContractRejected = 2,
    CodecThrew = 3,
    DecodeRejected = 4,
    ComparerThrew = 5,
    ContainerCorrupt = 6,
    SemanticTraceRejected = 7,
    CycleContextRejected = 8,
    EvaluatorFaulted = 9,
    ExecutionFaulted = 10,
}

public readonly struct ServiceCycleReplayFault : IEquatable<ServiceCycleReplayFault>
{
    public ServiceCycleReplayFault(
        ServiceCycleReplayFaultCode code,
        ServiceCycleReplayFailureLocation location,
        int detailCode = 0)
    {
        if (code is < ServiceCycleReplayFaultCode.RecordTypeRejected or > ServiceCycleReplayFaultCode.ExecutionFaulted)
            throw new ArgumentOutOfRangeException(nameof(code));
        if (!location.IsValid) throw new ArgumentException("A valid failure location is required.", nameof(location));
        if (!ScopeMatches(code, location.Scope))
            throw new ArgumentException("The failure location does not match the fault code.", nameof(location));
        if (detailCode < 0) throw new ArgumentOutOfRangeException(nameof(detailCode));
        Code = code;
        Location = location;
        DetailCode = detailCode;
    }

    public ServiceCycleReplayFaultCode Code { get; }
    public ServiceCycleReplayFailureLocation Location { get; }
    public int DetailCode { get; }
    public bool IsValid =>
        Code is >= ServiceCycleReplayFaultCode.RecordTypeRejected and <= ServiceCycleReplayFaultCode.ExecutionFaulted &&
        Location.IsValid && ScopeMatches(Code, Location.Scope) && DetailCode >= 0;
    public bool Equals(ServiceCycleReplayFault other) =>
        Code == other.Code && Location == other.Location && DetailCode == other.DetailCode;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayFault other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Code, Location, DetailCode);
    public static bool operator ==(ServiceCycleReplayFault left, ServiceCycleReplayFault right) => left.Equals(right);
    public static bool operator !=(ServiceCycleReplayFault left, ServiceCycleReplayFault right) => !left.Equals(right);

    private static bool ScopeMatches(ServiceCycleReplayFaultCode code, ServiceCycleReplayFailureScope scope) => code switch
    {
        >= ServiceCycleReplayFaultCode.RecordTypeRejected and <= ServiceCycleReplayFaultCode.ComparerThrew =>
            scope == ServiceCycleReplayFailureScope.Record,
        ServiceCycleReplayFaultCode.ContainerCorrupt => scope == ServiceCycleReplayFailureScope.Container,
        ServiceCycleReplayFaultCode.SemanticTraceRejected => scope == ServiceCycleReplayFailureScope.SemanticTrace,
        ServiceCycleReplayFaultCode.CycleContextRejected or ServiceCycleReplayFaultCode.EvaluatorFaulted =>
            scope == ServiceCycleReplayFailureScope.Cycle,
        ServiceCycleReplayFaultCode.ExecutionFaulted => scope == ServiceCycleReplayFailureScope.Execution,
        _ => false,
    };
}

/// <summary>Stable semantic categories for replay divergence. Values must not be renumbered.</summary>
public enum ServiceCycleReplayMismatchCode
{
    CycleInput = 1,
    PreviousState = 2,
    NextState = 3,
    Action = 4,
    ActionCount = 5,
    WakePolicy = 6,
    NativeOutcome = 7,
    BatchReceipt = 8,
    SemanticEvent = 9,
}

public readonly struct ServiceCycleReplayMismatch : IEquatable<ServiceCycleReplayMismatch>
{
    public ServiceCycleReplayMismatch(
        ServiceCycleReplayMismatchCode code,
        ServiceCycleReplayRecordIdentity record,
        int fieldCode,
        int elementIndex = 0)
    {
        if (code is < ServiceCycleReplayMismatchCode.CycleInput or > ServiceCycleReplayMismatchCode.SemanticEvent)
            throw new ArgumentOutOfRangeException(nameof(code));
        if (fieldCode <= 0) throw new ArgumentOutOfRangeException(nameof(fieldCode));
        if (elementIndex < 0) throw new ArgumentOutOfRangeException(nameof(elementIndex));
        if (RequiresRecord(code) && !record.IsValid)
            throw new ArgumentException("This mismatch requires a valid detached record identity.", nameof(record));
        if (!RequiresRecord(code) && record.IsValid)
            throw new ArgumentException("This cycle-level mismatch cannot claim a detached record identity.", nameof(record));
        if (record.IsValid && !KindMatches(code, record.Kind))
            throw new ArgumentException("The record identity does not match the mismatch category.", nameof(record));
        Code = code;
        Record = record;
        FieldCode = fieldCode;
        ElementIndex = elementIndex;
    }

    public ServiceCycleReplayMismatchCode Code { get; }
    public ServiceCycleReplayRecordIdentity Record { get; }
    public int FieldCode { get; }
    public int ElementIndex { get; }
    public bool IsValid =>
        Code is >= ServiceCycleReplayMismatchCode.CycleInput and <= ServiceCycleReplayMismatchCode.SemanticEvent &&
        FieldCode > 0 && ElementIndex >= 0 && (RequiresRecord(Code) == Record.IsValid) &&
        (!Record.IsValid || KindMatches(Code, Record.Kind));

    public bool Equals(ServiceCycleReplayMismatch other) =>
        Code == other.Code && Record == other.Record && FieldCode == other.FieldCode &&
        ElementIndex == other.ElementIndex;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayMismatch other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Code, Record, FieldCode, ElementIndex);
    public static bool operator ==(ServiceCycleReplayMismatch left, ServiceCycleReplayMismatch right) => left.Equals(right);
    public static bool operator !=(ServiceCycleReplayMismatch left, ServiceCycleReplayMismatch right) => !left.Equals(right);

    private static bool RequiresRecord(ServiceCycleReplayMismatchCode code) =>
        code is >= ServiceCycleReplayMismatchCode.CycleInput and <= ServiceCycleReplayMismatchCode.Action ||
        code == ServiceCycleReplayMismatchCode.NativeOutcome;

    private static bool KindMatches(ServiceCycleReplayMismatchCode code, ServiceCycleReplayRecordKind kind) => code switch
    {
        ServiceCycleReplayMismatchCode.CycleInput => kind == ServiceCycleReplayRecordKind.CycleInput,
        ServiceCycleReplayMismatchCode.PreviousState => kind == ServiceCycleReplayRecordKind.PreviousState,
        ServiceCycleReplayMismatchCode.NextState => kind == ServiceCycleReplayRecordKind.NextState,
        ServiceCycleReplayMismatchCode.Action or ServiceCycleReplayMismatchCode.NativeOutcome =>
            kind == ServiceCycleReplayRecordKind.Action,
        _ => true,
    };
}
