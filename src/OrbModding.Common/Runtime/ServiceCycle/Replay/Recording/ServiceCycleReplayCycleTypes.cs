using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

/// <summary>Stable numeric identity for one recorded service cycle. No service string is retained.</summary>
public readonly struct ServiceCycleReplayCycleKey : IEquatable<ServiceCycleReplayCycleKey>
{
    internal ServiceCycleReplayCycleKey(int traceServiceKey, in ServiceCycleIdentity identity)
    {
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        if (!identity.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(identity));
        TraceServiceKey = traceServiceKey;
        Lifecycle = identity.Lifecycle.Value;
        Configuration = identity.Config.Value;
        Strategy = identity.Strategy.Value;
        Capture = identity.Capture.Value;
        Cycle = identity.Cycle.Value;
    }

    internal ServiceCycleReplayCycleKey(
        int traceServiceKey,
        ulong lifecycle,
        ulong configuration,
        ulong strategy,
        ulong capture,
        ulong cycle)
    {
        TraceServiceKey = traceServiceKey;
        Lifecycle = lifecycle;
        Configuration = configuration;
        Strategy = strategy;
        Capture = capture;
        Cycle = cycle;
    }

    public int TraceServiceKey { get; }
    public ulong Lifecycle { get; }
    public ulong Configuration { get; }
    public ulong Strategy { get; }
    public ulong Capture { get; }
    public ulong Cycle { get; }
    public bool IsValid => TraceServiceKey > 0 && Lifecycle != 0 && Configuration != 0 &&
        Strategy != 0 && Capture != 0 && Cycle != 0;

    public bool Equals(ServiceCycleReplayCycleKey other) =>
        TraceServiceKey == other.TraceServiceKey && Lifecycle == other.Lifecycle &&
        Configuration == other.Configuration && Strategy == other.Strategy &&
        Capture == other.Capture && Cycle == other.Cycle;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayCycleKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        TraceServiceKey, Lifecycle, Configuration, Strategy, Capture, Cycle);
    public static bool operator ==(ServiceCycleReplayCycleKey left, ServiceCycleReplayCycleKey right) => left.Equals(right);
    public static bool operator !=(ServiceCycleReplayCycleKey left, ServiceCycleReplayCycleKey right) => !left.Equals(right);
}

/// <summary>Reference-free copy of the exact previous receipt consumed by an evaluation.</summary>
public readonly struct ServiceCycleReplayReceipt
{
    internal ServiceCycleReplayReceipt(int traceServiceKey, in BatchReceipt receipt)
    {
        IsPresent = receipt.IsPresent;
        var receiptCycle = receipt.Cycle;
        Cycle = receipt.IsPresent
            ? new ServiceCycleReplayCycleKey(traceServiceKey, in receiptCycle)
            : default;
        Batch = receipt.Batch.Value;
        Disposition = receipt.Disposition;
        ActionCount = receipt.ActionCount;
        CommittedCount = receipt.CommittedCount;
        TerminalIndex = receipt.TerminalIndex;
        UntouchedSuffixCount = receipt.UntouchedSuffixCount;
        ResultCode = receipt.ResultCode.Value;
        TerminalAction = receipt.TerminalAction;
        HasTerminalAction = receipt.HasTerminalAction;
        NativeCallOutcome = receipt.NativeCallOutcome;
        CompletedAt = receipt.CompletedAt.Ticks;
        EmergencyStop = receipt.EmergencyStop;
    }

    public bool IsPresent { get; }
    public ServiceCycleReplayCycleKey Cycle { get; }
    public ulong Batch { get; }
    public BatchTerminalDisposition Disposition { get; }
    public int ActionCount { get; }
    public int CommittedCount { get; }
    public int TerminalIndex { get; }
    public int UntouchedSuffixCount { get; }
    public int ResultCode { get; }
    public ServiceActionResult TerminalAction { get; }
    public bool HasTerminalAction { get; }
    public ServiceNativeCallTotals NativeCallOutcome { get; }
    public long CompletedAt { get; }
    public EmergencyStopContext EmergencyStop { get; }
}

public readonly struct ServiceCycleReplayContext
{
    internal ServiceCycleReplayContext(int traceServiceKey, in ServiceCycleContext context)
    {
        var identity = context.Identity;
        var previousReceipt = context.PreviousReceipt;
        Cycle = new ServiceCycleReplayCycleKey(traceServiceKey, in identity);
        PreviousReceipt = new ServiceCycleReplayReceipt(traceServiceKey, in previousReceipt);
        DecisionAt = context.DecisionAt.Ticks;
    }

    public ServiceCycleReplayCycleKey Cycle { get; }
    public ServiceCycleReplayReceipt PreviousReceipt { get; }
    public long DecisionAt { get; }
}
