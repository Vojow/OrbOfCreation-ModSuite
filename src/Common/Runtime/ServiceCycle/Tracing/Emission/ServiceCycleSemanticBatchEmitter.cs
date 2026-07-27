using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Builds batch and native-action payloads while preserving attempt and emergency ancestry.</summary>
internal sealed class ServiceCycleSemanticBatchEmitter
{
    private readonly ServiceCycleSemanticCausalWriter _writer;
    private readonly ServiceCycleSemanticFrameCursor _frame;
    private readonly bool _enabled;

    internal ServiceCycleSemanticBatchEmitter(
        ServiceCycleSemanticCausalWriter writer,
        ServiceCycleSemanticFrameCursor frame,
        bool enabled)
    {
        _writer = writer;
        _frame = frame;
        _enabled = enabled;
    }

    internal void BatchPublished(
        int ordinal,
        in ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        MonotonicTimestamp observedAt)
    {
        if (!_enabled) return;
        if (!batch.IsValid) throw new ArgumentException("A valid batch identity is required.", nameof(batch));
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.BatchFact(
            in traceCycle,
            batch.Value,
            0,
            0,
            actionCount,
            0,
            -1,
            0,
            0,
            0,
            0,
            observedAt.Ticks);
        _writer.AppendService(ordinal, ServiceCycleSemanticEventKind.BatchPublished, in payload);
    }

    internal void ActionAttempted(int ordinal, in ServiceActionContext context)
    {
        if (!_enabled) return;
        var cycle = context.Cycle;
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var payload = ServiceCycleSemanticPayload.ActionFact(
            in traceCycle,
            context.Batch.Value,
            context.Action.Value,
            context.ActionIndex,
            0,
            0,
            null,
            0,
            0,
            0,
            context.AttemptedAt.Ticks,
            0,
            _frame.Frame);
        _writer.AppendActionAttempted(ordinal, in context, in payload);
    }

    internal void ActionCompleted(
        int ordinal,
        in ServiceActionContext context,
        in ServiceActionResult result,
        MonotonicTimestamp completedAt,
        MonotonicDuration duration) =>
        ActionCompleted(ordinal, in context, in result, completedAt, duration, default);

    internal void ActionRejectedForEmergency(
        int ordinal,
        in ServiceActionContext context,
        in ServiceActionResult result,
        in EmergencyStopContext emergency,
        MonotonicTimestamp completedAt,
        MonotonicDuration duration)
    {
        if (!_enabled) return;
        if (!emergency.IsValid)
            throw new ArgumentException("A valid emergency context is required.", nameof(emergency));
        if (result.Disposition != ServiceActionDisposition.Rejected ||
            result.Code != CommonActionResultCodes.EmergencyStop)
        {
            throw new ArgumentException("An emergency rejection result is required.", nameof(result));
        }
        if (_writer.TryResolveEmergency(ordinal, in emergency, out var emergencyEntry))
            ActionCompleted(ordinal, in context, in result, completedAt, duration, emergencyEntry);
        else
            ActionCompleted(
                ordinal,
                in context,
                in result,
                completedAt,
                duration,
                default,
                parentless: true);
    }

    internal void BatchTerminal(int ordinal, in BatchReceipt receipt)
    {
        if (!_enabled) return;
        if (!receipt.IsPresent)
            throw new ArgumentException("A terminal batch receipt is required.", nameof(receipt));
        var cycle = receipt.Cycle;
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var totals = receipt.NativeCallOutcome;
        var kind = receipt.Disposition switch
        {
            BatchTerminalDisposition.Completed => ServiceCycleSemanticEventKind.BatchCompleted,
            BatchTerminalDisposition.Rejected or BatchTerminalDisposition.Faulted =>
                ServiceCycleSemanticEventKind.BatchAborted,
            BatchTerminalDisposition.Orphaned => ServiceCycleSemanticEventKind.BatchOrphaned,
            _ => throw new ArgumentOutOfRangeException(nameof(receipt)),
        };
        var payload = ServiceCycleSemanticPayload.BatchFact(
            in traceCycle,
            receipt.Batch.Value,
            (int)receipt.Disposition,
            receipt.ResultCode.Value,
            receipt.ActionCount,
            receipt.CommittedCount,
            receipt.TerminalIndex,
            receipt.UntouchedSuffixCount,
            totals.NativeCallsAttempted,
            totals.MutationAttempts,
            totals.MutationsCommitted,
            receipt.CompletedAt.Ticks,
            receipt.PublishedCount);
        var emergency = receipt.EmergencyStop;
        if (receipt.HasEmergencyStopContext)
        {
            if (_writer.TryResolveEmergency(ordinal, in emergency, out var emergencyEntry))
                _writer.AppendService(ordinal, kind, in payload, emergencyEntry);
            else
                _writer.AppendServiceRoot(ordinal, kind, in payload);
            _writer.ClearRetainedEmergency(ordinal);
            return;
        }
        _writer.AppendService(ordinal, kind, in payload);
        _writer.ClearRetainedEmergency(ordinal);
    }

    private void ActionCompleted(
        int ordinal,
        in ServiceActionContext context,
        in ServiceActionResult result,
        MonotonicTimestamp completedAt,
        MonotonicDuration duration,
        ServiceCycleTraceEventId explicitParent,
        bool parentless = false)
    {
        if (!_enabled) return;
        if (!result.IsValid) throw new ArgumentException("A valid action result is required.", nameof(result));
        var cycle = context.Cycle;
        var traceCycle = _writer.TraceCycle(ordinal, in cycle);
        var calls = result.NativeCallOutcome;
        var kind = result.Disposition switch
        {
            ServiceActionDisposition.Committed => ServiceCycleSemanticEventKind.ActionCommitted,
            ServiceActionDisposition.Skipped => ServiceCycleSemanticEventKind.ActionSkipped,
            ServiceActionDisposition.Rejected => ServiceCycleSemanticEventKind.ActionRejected,
            ServiceActionDisposition.Faulted => ServiceCycleSemanticEventKind.ActionFaulted,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        NativeMutationOutcome? nativeOutcome = result.HasNativeEvidence
            ? result.NativeEvidence.Outcome
            : null;
        var payload = ServiceCycleSemanticPayload.ActionFact(
            in traceCycle,
            context.Batch.Value,
            context.Action.Value,
            context.ActionIndex,
            (int)result.Disposition,
            result.Code.Value,
            nativeOutcome,
            calls.NativeCallsAttempted,
            calls.MutationAttempts,
            calls.MutationsCommitted,
            completedAt.Ticks,
            duration.Ticks,
            _frame.Frame);
        if (explicitParent.IsValid)
            _writer.AppendService(ordinal, kind, in payload, explicitParent);
        else if (parentless)
            _writer.AppendServiceRoot(ordinal, kind, in payload);
        else
            _writer.AppendActionTerminal(ordinal, in context, kind, in payload);
    }
}
