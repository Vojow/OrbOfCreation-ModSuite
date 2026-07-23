using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public enum ServiceCyclePhase
{
    Waiting = 0,
    Capturing = 1,
    Evaluating = 2,
    Executing = 3,
}

public enum ServiceCaptureDisposition
{
    Captured = 1,
    Unavailable = 2,
}

public readonly struct ServiceDecisionCode : IEquatable<ServiceDecisionCode>
{
    public const int FirstFeatureCode = 1024;

    public ServiceDecisionCode(int value)
    {
        if (value < FirstFeatureCode)
            throw new ArgumentOutOfRangeException(nameof(value), "Feature decision codes must use the feature-reserved range.");
        Value = value;
    }

    private ServiceDecisionCode(int value, bool reserved) => Value = value;

    public int Value { get; }
    public bool IsValid => Value is >= 1 and <= 5 || Value >= FirstFeatureCode;
    internal bool IsFeatureCode => Value >= FirstFeatureCode;
    internal static ServiceDecisionCode Reserved(int value) => new(value, true);
    public bool Equals(ServiceDecisionCode other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ServiceDecisionCode other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(ServiceDecisionCode left, ServiceDecisionCode right) => left.Equals(right);
    public static bool operator !=(ServiceDecisionCode left, ServiceDecisionCode right) => !left.Equals(right);
}

public static class CommonServiceDecisionCodes
{
    public static ServiceDecisionCode Ready => ServiceDecisionCode.Reserved(1);
    public static ServiceDecisionCode Captured => ServiceDecisionCode.Reserved(2);
    public static ServiceDecisionCode NotReady => ServiceDecisionCode.Reserved(3);
    public static ServiceDecisionCode CaptureUnavailable => ServiceDecisionCode.Reserved(4);
    public static ServiceDecisionCode TransientContention => ServiceDecisionCode.Reserved(5);
}

public readonly struct ServiceStartDecision
{
    private ServiceStartDecision(bool shouldStart, ServiceDecisionCode code, WakePolicy wakePolicy)
    {
        ShouldStart = shouldStart;
        Code = code;
        WakePolicy = wakePolicy;
    }

    public bool ShouldStart { get; }
    public ServiceDecisionCode Code { get; }
    public WakePolicy WakePolicy { get; }
    public bool IsValid => Code.IsValid &&
        (ShouldStart
            ? IsAllowedCode(Code, CommonServiceDecisionCodes.Ready) && WakePolicy == WakePolicy.Immediate
            : IsAllowedCode(Code, CommonServiceDecisionCodes.NotReady) && IsRetryShape(WakePolicy));

    public static ServiceStartDecision Ready(ServiceDecisionCode code) =>
        Create(true, code, WakePolicy.Immediate);

    public static ServiceStartDecision Wait(ServiceDecisionCode code, WakePolicy retry) =>
        CreateWait(code, retry);

    public static ServiceStartDecision WaitUntil(
        ServiceDecisionCode code,
        MonotonicTimestamp dueTime,
        MonotonicTimestamp now)
    {
        ValidateCode(code, CommonServiceDecisionCodes.NotReady, nameof(code));
        if (dueTime <= now) throw new ArgumentOutOfRangeException(nameof(dueTime), "Retry deadline must be in the future.");
        return new ServiceStartDecision(false, code, WakePolicy.At(dueTime));
    }

    private static ServiceStartDecision Create(bool shouldStart, ServiceDecisionCode code, WakePolicy wakePolicy)
    {
        if (!code.IsValid) throw new ArgumentException("A stable start-decision code is required.", nameof(code));
        if (shouldStart)
        {
            ValidateCode(code, CommonServiceDecisionCodes.Ready, nameof(code));
            return new ServiceStartDecision(true, code, WakePolicy.Immediate);
        }
        return CreateWait(code, wakePolicy);
    }

    private static ServiceStartDecision CreateWait(ServiceDecisionCode code, WakePolicy wakePolicy)
    {
        ValidateCode(code, CommonServiceDecisionCodes.NotReady, nameof(code));
        if (!IsPositiveDelay(wakePolicy))
            throw new ArgumentException("A waiting decision requires an explicit non-immediate retry policy.", nameof(wakePolicy));
        return new ServiceStartDecision(false, code, wakePolicy);
    }

    internal static bool IsRetryShape(WakePolicy wakePolicy) =>
        wakePolicy.Kind == WakePolicyKind.At || IsPositiveDelay(wakePolicy);

    internal static bool IsPositiveDelay(WakePolicy wakePolicy) =>
        wakePolicy.Kind == WakePolicyKind.AfterDecision && wakePolicy.Delay.Ticks > 0;

    internal static bool IsAllowedCode(ServiceDecisionCode code, ServiceDecisionCode expectedCommon) =>
        code.IsFeatureCode || code == expectedCommon;

    internal static void ValidateCode(
        ServiceDecisionCode code,
        ServiceDecisionCode expectedCommon,
        string parameterName)
    {
        if (!code.IsValid || !IsAllowedCode(code, expectedCommon))
            throw new ArgumentException("Decision code does not belong to this outcome.", parameterName);
    }
}

public readonly struct ServiceCaptureResult
{
    private ServiceCaptureResult(
        ServiceCaptureDisposition disposition,
        StrategyGeneration strategyGeneration,
        ServiceDecisionCode code,
        WakePolicy wakePolicy)
    {
        Disposition = disposition;
        StrategyGeneration = strategyGeneration;
        Code = code;
        WakePolicy = wakePolicy;
    }

    public ServiceCaptureDisposition Disposition { get; }
    public StrategyGeneration StrategyGeneration { get; }
    public ServiceDecisionCode Code { get; }
    public WakePolicy WakePolicy { get; }
    public bool IsCaptured => Disposition == ServiceCaptureDisposition.Captured;
    public bool IsValid => Disposition switch
    {
        ServiceCaptureDisposition.Captured =>
            StrategyGeneration.Value != 0 &&
            ServiceStartDecision.IsAllowedCode(Code, CommonServiceDecisionCodes.Captured) &&
            WakePolicy == WakePolicy.Immediate,
        ServiceCaptureDisposition.Unavailable =>
            StrategyGeneration.Value == 0 &&
            ServiceStartDecision.IsAllowedCode(Code, CommonServiceDecisionCodes.CaptureUnavailable) &&
            ServiceStartDecision.IsRetryShape(WakePolicy),
        _ => false,
    };

    public static ServiceCaptureResult Captured(
        StrategyGeneration strategyGeneration,
        ServiceDecisionCode code)
    {
        if (strategyGeneration.Value == 0)
            throw new ArgumentException("A captured frame requires its exact strategy generation.", nameof(strategyGeneration));
        ServiceStartDecision.ValidateCode(code, CommonServiceDecisionCodes.Captured, nameof(code));
        return new ServiceCaptureResult(ServiceCaptureDisposition.Captured, strategyGeneration, code, WakePolicy.Immediate);
    }

    public static ServiceCaptureResult Unavailable(ServiceDecisionCode code, WakePolicy retry)
    {
        ServiceStartDecision.ValidateCode(code, CommonServiceDecisionCodes.CaptureUnavailable, nameof(code));
        if (!ServiceStartDecision.IsPositiveDelay(retry))
            throw new ArgumentException("Unavailable capture requires an explicit non-immediate retry policy.", nameof(retry));
        return new ServiceCaptureResult(ServiceCaptureDisposition.Unavailable, default, code, retry);
    }

    public static ServiceCaptureResult UnavailableUntil(
        ServiceDecisionCode code,
        MonotonicTimestamp dueTime,
        MonotonicTimestamp now)
    {
        ServiceStartDecision.ValidateCode(code, CommonServiceDecisionCodes.CaptureUnavailable, nameof(code));
        if (dueTime <= now) throw new ArgumentOutOfRangeException(nameof(dueTime), "Retry deadline must be in the future.");
        return new ServiceCaptureResult(ServiceCaptureDisposition.Unavailable, default, code, WakePolicy.At(dueTime));
    }
}
