using System;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public enum ServiceActionDisposition
{
    Committed = 1,
    Rejected = 2,
    Faulted = 3,
}

public readonly struct ServiceActionResultCode : IEquatable<ServiceActionResultCode>
{
    public const int FirstFeatureCode = 1024;

    public ServiceActionResultCode(int value)
    {
        if (value < FirstFeatureCode)
            throw new ArgumentOutOfRangeException(nameof(value), "Feature action-result codes must use the feature-reserved range.");
        Value = value;
    }

    private ServiceActionResultCode(int value, bool reserved) => Value = value;

    public int Value { get; }
    public bool IsValid => Value is >= 1 and <= 7 || Value >= FirstFeatureCode;
    internal bool IsFeatureCode => Value >= FirstFeatureCode;
    internal static ServiceActionResultCode Reserved(int value) => new(value, true);
    public bool Equals(ServiceActionResultCode other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ServiceActionResultCode other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => Value.ToString();
    public static bool operator ==(ServiceActionResultCode left, ServiceActionResultCode right) => left.Equals(right);
    public static bool operator !=(ServiceActionResultCode left, ServiceActionResultCode right) => !left.Equals(right);
}

public static class CommonActionResultCodes
{
    public static ServiceActionResultCode Committed => ServiceActionResultCode.Reserved(1);
    public static ServiceActionResultCode EmergencyStop => ServiceActionResultCode.Reserved(2);
    public static ServiceActionResultCode LifecycleReplaced => ServiceActionResultCode.Reserved(3);
    public static ServiceActionResultCode ServiceDisabled => ServiceActionResultCode.Reserved(4);
    public static ServiceActionResultCode NativeRejected => ServiceActionResultCode.Reserved(5);
    public static ServiceActionResultCode PolicyRejected => ServiceActionResultCode.Reserved(6);
    public static ServiceActionResultCode AdapterFault => ServiceActionResultCode.Reserved(7);
}

internal static class NativeMutationCallOutcomeValidation
{
    internal static void Validate(in NativeMutationCallOutcome outcome, string parameterName)
    {
        if (outcome.NativeCallsAttempted < 0 ||
            outcome.MutationAttempts < 0 ||
            outcome.MutationsCommitted < 0 ||
            outcome.MutationAttempts > outcome.NativeCallsAttempted ||
            outcome.MutationsCommitted > outcome.MutationAttempts)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

}

/// <summary>
/// Batch-wide native-call evidence. Per-action evidence remains the game's compact integer shape;
/// accumulation uses 64-bit counters so bookkeeping cannot overflow after a native mutation committed.
/// </summary>
public readonly struct ServiceNativeCallTotals
{
    public ServiceNativeCallTotals(long nativeCallsAttempted, long mutationAttempts, long mutationsCommitted)
    {
        if (nativeCallsAttempted < 0 || mutationAttempts < 0 || mutationsCommitted < 0 ||
            mutationAttempts > nativeCallsAttempted || mutationsCommitted > mutationAttempts)
            throw new ArgumentOutOfRangeException(nameof(nativeCallsAttempted));
        NativeCallsAttempted = nativeCallsAttempted;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
    }

    public long NativeCallsAttempted { get; }
    public long MutationAttempts { get; }
    public long MutationsCommitted { get; }

    public static implicit operator ServiceNativeCallTotals(NativeMutationCallOutcome outcome) =>
        From(in outcome);

    internal static ServiceNativeCallTotals From(in NativeMutationCallOutcome outcome)
    {
        NativeMutationCallOutcomeValidation.Validate(in outcome, nameof(outcome));
        return new ServiceNativeCallTotals(
            outcome.NativeCallsAttempted,
            outcome.MutationAttempts,
            outcome.MutationsCommitted);
    }

    internal static ServiceNativeCallTotals Add(
        in ServiceNativeCallTotals left,
        in NativeMutationCallOutcome right)
    {
        NativeMutationCallOutcomeValidation.Validate(in right, nameof(right));
        return new ServiceNativeCallTotals(
            checked(left.NativeCallsAttempted + right.NativeCallsAttempted),
            checked(left.MutationAttempts + right.MutationAttempts),
            checked(left.MutationsCommitted + right.MutationsCommitted));
    }
}

public readonly struct ServiceNativeMutationEvidence
{
    private ServiceNativeMutationEvidence(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome)
    {
        Outcome = outcome;
        CallOutcome = callOutcome;
    }

    public NativeMutationOutcome Outcome { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public bool IsValid => IsCoherent(Outcome, CallOutcome);

    public static ServiceNativeMutationEvidence Observed(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome)
    {
        if (!IsCoherent(outcome, callOutcome))
            throw new ArgumentException("Native outcome and call counts are not coherent.", nameof(callOutcome));
        return new ServiceNativeMutationEvidence(outcome, callOutcome);
    }

    private static bool IsCoherent(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome)
    {
        if (callOutcome.NativeCallsAttempted < 0 ||
            callOutcome.MutationAttempts < 0 ||
            callOutcome.MutationsCommitted < 0 ||
            callOutcome.MutationAttempts > callOutcome.NativeCallsAttempted ||
            callOutcome.MutationsCommitted > callOutcome.MutationAttempts)
        {
            return false;
        }

        return outcome switch
        {
            NativeMutationOutcome.Verified =>
                callOutcome.MutationAttempts > 0 &&
                callOutcome.MutationsCommitted == callOutcome.MutationAttempts,
            NativeMutationOutcome.BeforeCaptureFailed =>
                callOutcome.NativeCallsAttempted == 0 &&
                callOutcome.MutationAttempts == 0 &&
                callOutcome.MutationsCommitted == 0,
            NativeMutationOutcome.ExecutionThrew or
            NativeMutationOutcome.AfterCaptureFailed or
            NativeMutationOutcome.PostconditionFailed =>
                callOutcome.MutationAttempts > 0 &&
                callOutcome.MutationsCommitted == 0,
            _ => false,
        };
    }
}

public readonly struct ServiceActionResult
{
    private ServiceActionResult(
        ServiceActionDisposition disposition,
        ServiceActionResultCode code,
        bool hasNativeEvidence,
        ServiceNativeMutationEvidence nativeEvidence)
    {
        Disposition = disposition;
        Code = code;
        HasNativeEvidence = hasNativeEvidence;
        NativeEvidence = nativeEvidence;
    }

    public ServiceActionDisposition Disposition { get; }
    public ServiceActionResultCode Code { get; }
    public bool HasNativeEvidence { get; }
    public ServiceNativeMutationEvidence NativeEvidence { get; }
    public NativeMutationCallOutcome NativeCallOutcome =>
        HasNativeEvidence ? NativeEvidence.CallOutcome : default;
    public bool IsValid => Code.IsValid && IsAllowedCode(Code, Disposition) && Disposition switch
    {
        ServiceActionDisposition.Committed =>
            HasNativeEvidence && NativeEvidence.IsValid && NativeEvidence.Outcome == NativeMutationOutcome.Verified,
        ServiceActionDisposition.Rejected => !HasNativeEvidence,
        ServiceActionDisposition.Faulted =>
            !HasNativeEvidence || NativeEvidence.IsValid && NativeEvidence.Outcome != NativeMutationOutcome.Verified,
        _ => false,
    };

    public static ServiceActionResult Committed(
        ServiceActionResultCode code,
        ServiceNativeMutationEvidence nativeEvidence) =>
        Create(ServiceActionDisposition.Committed, code, true, nativeEvidence);
    public static ServiceActionResult Rejected(ServiceActionResultCode code) =>
        Create(ServiceActionDisposition.Rejected, code, false, default);
    public static ServiceActionResult Faulted(ServiceActionResultCode code) =>
        Create(ServiceActionDisposition.Faulted, code, false, default);
    public static ServiceActionResult Faulted(
        ServiceActionResultCode code,
        ServiceNativeMutationEvidence nativeEvidence) =>
        Create(ServiceActionDisposition.Faulted, code, true, nativeEvidence);

    private static ServiceActionResult Create(
        ServiceActionDisposition disposition,
        ServiceActionResultCode code,
        bool hasNativeEvidence,
        ServiceNativeMutationEvidence nativeEvidence)
    {
        if (!code.IsValid) throw new ArgumentException("A stable result code is required.", nameof(code));
        if (!IsAllowedCode(code, disposition))
            throw new ArgumentException("Result code does not belong to this action disposition.", nameof(code));
        if (hasNativeEvidence && !nativeEvidence.IsValid)
            throw new ArgumentException("Observed native evidence must be coherent.", nameof(nativeEvidence));
        switch (disposition)
        {
            case ServiceActionDisposition.Committed:
                if (!hasNativeEvidence || nativeEvidence.Outcome != NativeMutationOutcome.Verified)
                    throw new ArgumentException("A committed action requires observed verified native evidence.", nameof(nativeEvidence));
                break;
            case ServiceActionDisposition.Rejected:
                if (hasNativeEvidence)
                    throw new ArgumentException("A rejected action cannot carry native mutation evidence.", nameof(nativeEvidence));
                break;
            case ServiceActionDisposition.Faulted:
                if (hasNativeEvidence && nativeEvidence.Outcome == NativeMutationOutcome.Verified)
                    throw new ArgumentException("Rejected or faulted actions cannot carry verified native evidence.", nameof(nativeEvidence));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        return new ServiceActionResult(disposition, code, hasNativeEvidence, nativeEvidence);
    }

    private static bool IsAllowedCode(
        ServiceActionResultCode code,
        ServiceActionDisposition disposition)
    {
        if (code.IsFeatureCode) return true;

        return disposition switch
        {
            ServiceActionDisposition.Committed => code == CommonActionResultCodes.Committed,
            ServiceActionDisposition.Rejected =>
                code == CommonActionResultCodes.EmergencyStop ||
                code == CommonActionResultCodes.LifecycleReplaced ||
                code == CommonActionResultCodes.ServiceDisabled ||
                code == CommonActionResultCodes.NativeRejected ||
                code == CommonActionResultCodes.PolicyRejected,
            ServiceActionDisposition.Faulted => code == CommonActionResultCodes.AdapterFault,
            _ => false,
        };
    }

    public static ServiceNativeCallTotals AddNativeOutcomes(
        in ServiceNativeCallTotals left,
        in NativeMutationCallOutcome right) =>
        ServiceNativeCallTotals.Add(in left, in right);
}
