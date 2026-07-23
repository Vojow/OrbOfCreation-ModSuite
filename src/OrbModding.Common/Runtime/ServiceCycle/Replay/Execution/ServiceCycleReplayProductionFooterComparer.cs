using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal static class ServiceCycleReplayProductionFooterComparer
{
    internal static ServiceCycleReplayMismatch? Compare(
        in ServiceCycleReplayArtifactFooter expected,
        in ServiceCycleReplayCycleFooter actual)
    {
        // Global record/footer sequences and their inclusive bounds are scheduler-dependent when
        // multiple workers publish concurrently. Their counts and all cycle-local evidence remain exact.
        if (!SameCycle(expected.Context.Cycle, actual.Context.Cycle))
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 4);
        if (expected.Disposition != actual.Disposition)
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 5);
        if (expected.HasReturnedWake != actual.HasReturnedWake)
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 6);
        if (expected.HasProjection != actual.HasProjection)
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 7);
        if (expected.ExpectedActionCount != actual.ExpectedActionCount)
            return Mismatch(ServiceCycleReplayMismatchCode.ActionCount, 1);
        if (expected.RetainedRecordCount != actual.RetainedRecordCount)
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 8);
        if (expected.Completeness != actual.Completeness)
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 9);
        if (expected.Context.DecisionAt != actual.Context.DecisionAt)
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 10);
        if (expected.ReturnedWake.Kind != actual.ReturnedWake.Kind)
            return Mismatch(ServiceCycleReplayMismatchCode.WakePolicy, 1);
        if (expected.ReturnedWake.Delay != actual.ReturnedWake.Delay)
            return Mismatch(ServiceCycleReplayMismatchCode.WakePolicy, 2);
        if (expected.ReturnedWake.DueTime != actual.ReturnedWake.DueTime)
            return Mismatch(ServiceCycleReplayMismatchCode.WakePolicy, 3);
        var expectedProjection = expected.Projection;
        var actualProjection = actual.Projection;
        var projection = CompareProjection(in expectedProjection, in actualProjection);
        if (projection.HasValue) return projection;
        var expectedReceipt = expected.Context.PreviousReceipt;
        var actualReceipt = actual.Context.PreviousReceipt;
        return CompareReceipt(in expectedReceipt, in actualReceipt);
    }

    private static ServiceCycleReplayMismatch? CompareProjection(
        in ServiceStateProjectionSnapshot expected,
        in ServiceStateProjectionSnapshot actual)
    {
        if (expected.Count != actual.Count)
            return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 11);
        for (var index = 0; index < expected.Count; index++)
        {
            var left = expected.GetEntry(index);
            var right = actual.GetEntry(index);
            if (left.Key != right.Key || left.Value.Kind != right.Value.Kind ||
                left.Value.Integer != right.Value.Integer ||
                System.BitConverter.DoubleToInt64Bits(left.Value.FloatingPoint) !=
                System.BitConverter.DoubleToInt64Bits(right.Value.FloatingPoint))
                return Mismatch(ServiceCycleReplayMismatchCode.SemanticEvent, 12, index);
        }
        return null;
    }

    private static ServiceCycleReplayMismatch? CompareReceipt(
        in ServiceCycleReplayArtifactReceipt expected,
        in ServiceCycleReplayReceipt actual)
    {
        if (expected.IsPresent != actual.IsPresent) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 1);
        if (!expected.IsPresent) return null;
        var element = Math.Max(0, expected.TerminalIndex);
        if (!SameCycle(expected.Cycle, actual.Cycle)) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 2, element);
        if (expected.Batch != actual.Batch) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 3, element);
        if (expected.Disposition != actual.Disposition) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 4, element);
        if (expected.ActionCount != actual.ActionCount) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 5, element);
        if (expected.CommittedCount != actual.CommittedCount) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 6, element);
        if (expected.TerminalIndex != actual.TerminalIndex) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 7, element);
        if (expected.UntouchedSuffixCount != actual.UntouchedSuffixCount) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 8, element);
        if (expected.ResultCode != actual.ResultCode) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 9, element);
        if (expected.HasTerminalAction != actual.HasTerminalAction) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 10, element);
        if (expected.NativeCallsAttempted != actual.NativeCallOutcome.NativeCallsAttempted) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 11, element);
        if (expected.MutationAttempts != actual.NativeCallOutcome.MutationAttempts) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 12, element);
        if (expected.MutationsCommitted != actual.NativeCallOutcome.MutationsCommitted) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 13, element);
        if (expected.CompletedAt != actual.CompletedAt) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 14, element);
        if (expected.EmergencyEpisode != actual.EmergencyStop.Episode.Value) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 15, element);
        if (expected.EmergencyTransition != actual.EmergencyStop.Transition.Value) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 16, element);
        if (expected.EmergencyReason != (int)actual.EmergencyStop.Reason) return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 17, element);
        if (!expected.HasTerminalAction) return null;
        var terminal = expected.TerminalAction;
        var native = actual.TerminalAction.NativeCallOutcome;
        if (terminal.Disposition != actual.TerminalAction.Disposition ||
            terminal.Code != actual.TerminalAction.Code.Value ||
            terminal.HasNativeEvidence != actual.TerminalAction.HasNativeEvidence ||
            terminal.NativeOutcomeCode != (actual.TerminalAction.HasNativeEvidence
                ? (int)actual.TerminalAction.NativeEvidence.Outcome + 1 : 0) ||
            terminal.NativeCallsAttempted != native.NativeCallsAttempted ||
            terminal.MutationAttempts != native.MutationAttempts ||
            terminal.MutationsCommitted != native.MutationsCommitted)
            return Mismatch(ServiceCycleReplayMismatchCode.BatchReceipt, 18, element);
        return null;
    }

    private static bool SameCycle(
        ServiceCycleReplayCycleKey expected,
        ServiceCycleReplayCycleKey actual) =>
        expected.Lifecycle == actual.Lifecycle &&
        expected.Configuration == actual.Configuration &&
        expected.Strategy == actual.Strategy &&
        expected.Capture == actual.Capture &&
        expected.Cycle == actual.Cycle;

    private static ServiceCycleReplayMismatch Mismatch(
        ServiceCycleReplayMismatchCode code,
        int field,
        int element = 0) => new(code, default, field, element);
}
