using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayMutationEvidenceTests
{
    [Fact]
    public void SemanticComparerReportsNativeOutcomeMutationAsFieldThirtyNine()
    {
        var artifact = ServiceCycleReplayProductionScenarioFixture.Capture(
            1,
            ProductionReplayScenario.ActionFaulted).Artifact;
        var expected = artifact.SemanticTrace;
        var events = expected.Events.ToArray();
        for (var index = 0; index < events.Length; index++)
        {
            var item = events[index];
            if (item.Kind != ServiceCycleSemanticEventKind.ActionFaulted) continue;
            var payload = WithNativeOutcome(
                item.Payload,
                checked((int)NativeMutationOutcome.AfterCaptureFailed + 1));
            events[index] = new ServiceCycleSemanticEvent(item.Id, item.Parent, item.Kind, in payload);
            break;
        }
        var actual = new ServiceCycleTraceDocument(
            expected.SchemaVersion,
            expected.Session,
            expected.Dropped,
            expected.ServiceCapacity,
            events);

        var mismatch = ServiceCycleReplaySemanticComparer.Compare(
            artifact.GetCycle(0).Key,
            expected,
            actual);

        Assert.True(mismatch.HasValue);
        Assert.Equal(ServiceCycleReplayMismatchCode.SemanticEvent, mismatch.Value.Mismatch.Code);
        Assert.NotEqual(ServiceCycleReplayMismatchCode.NativeOutcome, mismatch.Value.Mismatch.Code);
        Assert.Equal(39, mismatch.Value.Mismatch.FieldCode);
        Assert.Equal(0, mismatch.Value.Mismatch.ElementIndex);
    }

    [Fact]
    public void FooterComparerReportsTerminalReceiptCompletionMutationAsBatchReceiptFieldFourteen()
    {
        var previousCycle = Cycle(1);
        var expectedReceipt = BatchReceipt.Completed(
            previousCycle,
            new BatchId(1),
            0,
            default,
            new MonotonicTimestamp(10));
        var actualReceipt = BatchReceipt.Completed(
            previousCycle,
            new BatchId(1),
            0,
            default,
            new MonotonicTimestamp(11));
        var expectedFooter = Footer(Cycle(2), in expectedReceipt);
        var expectedArtifactFooter = ServiceCycleReplayFooterConverter.Convert(in expectedFooter);
        var actualFooter = Footer(Cycle(2), in actualReceipt);

        var mismatch = ServiceCycleReplayProductionFooterComparer.Compare(
            in expectedArtifactFooter,
            in actualFooter);

        Assert.True(mismatch.HasValue);
        Assert.Equal(ServiceCycleReplayMismatchCode.BatchReceipt, mismatch.Value.Code);
        Assert.Equal(14, mismatch.Value.FieldCode);
        Assert.Equal(0, mismatch.Value.ElementIndex);
    }

    [Fact]
    public void FooterComparerReportsPriorTerminalNativeOutcomeAsBatchReceiptFieldEighteen()
    {
        var call = new NativeMutationCallOutcome(1, 1, 0);
        var expectedAction = ServiceActionResult.Faulted(
            CommonActionResultCodes.AdapterFault,
            ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.ExecutionThrew, call));
        var actualAction = ServiceActionResult.Faulted(
            CommonActionResultCodes.AdapterFault,
            ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.PostconditionFailed, call));
        var previousCycle = Cycle(1);
        var expectedReceipt = BatchReceipt.Terminated(
            previousCycle,
            new BatchId(1),
            1,
            0,
            0,
            expectedAction,
            call,
            new MonotonicTimestamp(10));
        var actualReceipt = BatchReceipt.Terminated(
            previousCycle,
            new BatchId(1),
            1,
            0,
            0,
            actualAction,
            call,
            new MonotonicTimestamp(10));
        var expectedFooter = Footer(Cycle(2), in expectedReceipt);
        var expectedArtifactFooter = ServiceCycleReplayFooterConverter.Convert(in expectedFooter);
        var actualFooter = Footer(Cycle(2), in actualReceipt);

        var mismatch = ServiceCycleReplayProductionFooterComparer.Compare(
            in expectedArtifactFooter,
            in actualFooter);

        Assert.True(mismatch.HasValue);
        Assert.Equal(ServiceCycleReplayMismatchCode.BatchReceipt, mismatch.Value.Code);
        Assert.NotEqual(ServiceCycleReplayMismatchCode.NativeOutcome, mismatch.Value.Code);
        Assert.Equal(18, mismatch.Value.FieldCode);
        Assert.Equal(0, mismatch.Value.ElementIndex);
    }

    private static ServiceCycleReplayCycleFooter Footer(
        ServiceCycleIdentity identity,
        in BatchReceipt previousReceipt)
    {
        var ordinary = new ServiceCycleContext(
            identity,
            previousReceipt,
            new MonotonicTimestamp(20));
        var context = new ServiceCycleReplayContext(1, in ordinary);
        return new ServiceCycleReplayCycleFooter(
            1,
            context,
            ServiceCycleReplayCycleFooterDisposition.Provisional,
            WakePolicy.Immediate,
            true,
            default,
            false,
            0,
            0,
            0,
            0,
            ServiceCycleReplayCompleteness.Complete,
            0,
            1,
            0);
    }

    private static ServiceCycleIdentity Cycle(ulong cycle) => new(
        new ServiceId("test.replay-mutation-evidence"),
        new LifecycleGeneration(1),
        new ConfigGeneration(1),
        new StrategyGeneration(1),
        new CaptureSequence(cycle),
        new CycleId(cycle));

    private static ServiceCycleSemanticPayload WithNativeOutcome(
        ServiceCycleSemanticPayload value,
        int nativeOutcome) => new(
        value.Fields,
        value.Service,
        value.Lifecycle,
        value.Configuration,
        value.Strategy,
        value.Capture,
        value.Cycle,
        value.Batch,
        value.Action,
        value.StatePublication,
        value.TimestampTicks,
        value.DurationTicks,
        value.DeadlineTicks,
        value.FrameIdentity,
        value.Fingerprint,
        value.Code,
        value.Disposition,
        value.ActionIndex,
        value.ActionCount,
        value.CommittedCount,
        value.UntouchedSuffixCount,
        value.OccurrenceCount,
        value.NativeCallsAttempted,
        value.MutationAttempts,
        value.MutationsCommitted,
        value.ResponsesAcquired,
        value.ActionsAttempted,
        value.CapturesAttempted,
        value.EmergencyBatchesRejected,
        value.LifecycleTransitions,
        value.ResponseDurationTicks,
        value.ActionDurationTicks,
        value.CaptureDurationTicks,
        value.TotalDurationTicks,
        nativeOutcome);
}
