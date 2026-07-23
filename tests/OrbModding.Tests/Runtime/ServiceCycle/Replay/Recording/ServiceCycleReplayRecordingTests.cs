using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Recording;

public sealed class ServiceCycleReplayRecordingTests
{
    [Fact]
    public void EnabledAndDisabledRecordingPreserveExactGameplayResults()
    {
        var enabled = RunSuccessfulCycle(true, 5);
        var disabled = RunSuccessfulCycle(false, 5);

        Assert.Equal(enabled.ActionCount, disabled.ActionCount);
        Assert.Equal(enabled.WakePolicy, disabled.WakePolicy);
        Assert.Equal(
            enabled.Projection.Snapshot.GetEntry(0).Value.Integer,
            disabled.Projection.Snapshot.GetEntry(0).Value.Integer);
        Assert.Equal(enabled.ExecutedActionCount, disabled.ExecutedActionCount);
        Assert.Equal(enabled.ExecutedActionOrderHash, disabled.ExecutedActionOrderHash);
        Assert.Equal(enabled.ActionTerminals, disabled.ActionTerminals);
        AssertReceiptsEqual(in enabled.Receipt, in disabled.Receipt);
        AssertReceiptsEqual(in enabled.PreviousReceipt, in disabled.PreviousReceipt);
        Assert.Equal(enabled.RecordProductions, disabled.RecordProductions);
        Assert.True(enabled.Fence.RecordCount > 0);
        Assert.Equal(0, disabled.Fence.RecordCount);
    }

    [Fact]
    public void ClosingRecordingAdmissionPreservesTheCompletedPrefixWithoutPartialCycles()
    {
        var control = new ReplayControl { ActionCount = 1 };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        fixture.RunCycle();
        var first = fixture.Session.Snapshot;
        fixture.Session.CloseRecordingAdmission();
        fixture.DrainBatch();
        fixture.Clock.Advance(new MonotonicDuration(1));
        fixture.RunCycle();
        var closed = fixture.Session.Snapshot;

        Assert.True(fixture.Session.RecordingAdmissionClosed);
        Assert.True(closed.Completeness.IsComplete);
        Assert.Equal(first.HighWater, closed.HighWater);
    }

    [Fact]
    public void MiddleCodecThrowLatchesFirstFailureAndDoesNotFaultGameplay()
    {
        var control = new ReplayControl { ActionCount = 4, ThrowCodecAtCall = 4 };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var snapshot = fixture.RunCycle();

        Assert.False(snapshot.Fault.IsValid);
        Assert.Equal(4, snapshot.ActionCount);
        var recording = fixture.Session.Snapshot;
        Assert.Equal(ServiceCycleReplayCompletenessCode.CodecFaulted, recording.Completeness.Code);
        Assert.Equal(ServiceCycleReplayFaultCode.CodecThrew, recording.Fault.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.Action, recording.Completeness.FailureLocation.Record.Kind);
        Assert.Equal(1, recording.Completeness.FailureLocation.Record.Index);
        Assert.Equal(4, control.CodecCalls);
        Assert.True(fixture.Session.TryReadHighWaterFence(out var fence));
        var footer = fixture.Session.ReadFooter(0, in fence);
        Assert.Equal(ServiceCycleReplayCycleFooterDisposition.Provisional, footer.Disposition);
        Assert.Equal(4, footer.ExpectedActionCount);
        Assert.True(footer.HasProjection);
        Assert.False(footer.Completeness.IsComplete);
    }

    [Fact]
    public void InvalidCodecResultIsIsolatedFromGameplay()
    {
        var control = new ReplayControl { ActionCount = 2, InvalidCodecAtCall = 3 };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var snapshot = fixture.RunCycle();

        Assert.False(snapshot.Fault.IsValid);
        Assert.Equal(2, snapshot.ActionCount);
        Assert.Equal(
            ServiceCycleReplayCompletenessCode.CodecContractRejected,
            fixture.Session.Snapshot.Completeness.Code);
        Assert.Equal(
            ServiceCycleReplayCodecContractCode.EncodedLengthExceedsBound,
            (ServiceCycleReplayCodecContractCode)fixture.Session.Snapshot.Fault.DetailCode);
    }

    [Fact]
    public void CostlyCodecWorkHasSeparateEncodingMetricsAndPreservesEvaluatorEvidence()
    {
        var baselineControl = new ReplayControl { ActionCount = 2 };
        using var baselineFixture = new ReplayFixture(baselineControl, Bytes(256, 64, 8));
        var baseline = baselineFixture.RunCycle();
        Assert.True(baselineFixture.Session.TryReadHighWaterFence(out var baselineFence));
        var baselineFooter = baselineFixture.Session.ReadFooter(0, in baselineFence);

        var costlyControl = new ReplayControl
        {
            ActionCount = 2,
            CodecAllocationBytes = 4_096,
            CodecSpinIterations = 2_048,
        };
        using var costlyFixture = new ReplayFixture(costlyControl, Bytes(256, 64, 8));
        var costly = costlyFixture.RunCycle();
        Assert.True(costlyFixture.Session.TryReadHighWaterFence(out var costlyFence));
        var costlyFooter = costlyFixture.Session.ReadFooter(0, in costlyFence);

        Assert.Equal(baseline.ActionCount, costly.ActionCount);
        Assert.Equal(baseline.ActiveWake, costly.ActiveWake);
        Assert.Equal(
            baseline.Projection.Snapshot.GetEntry(0).Value.Integer,
            costly.Projection.Snapshot.GetEntry(0).Value.Integer);
        Assert.Equal(baseline.EvaluationTiming.Availability, costly.EvaluationTiming.Availability);
        Assert.Equal(baseline.EvaluationTiming.Fact.StartedAt, costly.EvaluationTiming.Fact.StartedAt);
        Assert.Equal(baseline.EvaluationTiming.Fact.CompletedAt, costly.EvaluationTiming.Fact.CompletedAt);
        Assert.True(costlyControl.CodecWorkTicks > 0);
        Assert.True(costlyFooter.EncodingDurationTicks >= costlyControl.CodecWorkTicks);
        Assert.Equal(5, costlyControl.CodecCalls);
        Assert.Equal(5L * costlyControl.CodecAllocationBytes, costlyControl.CodecAllocatedPayloadBytes);
        Assert.True(costlyFooter.EncodingAllocatedBytes >= costlyControl.CodecAllocatedPayloadBytes);
        Assert.True(baselineFooter.EncodingDurationTicks > 0);
    }

    [Theory]
    [InlineData(ReplayOomPhase.CycleInputProduction, ServiceCycleReplayCompletenessCode.RequiredRecordMissing,
        ServiceCycleReplayRecordKind.CycleInput, 0)]
    [InlineData(ReplayOomPhase.CycleInputCodec, ServiceCycleReplayCompletenessCode.CodecFaulted,
        ServiceCycleReplayRecordKind.CycleInput, 0)]
    [InlineData(ReplayOomPhase.PreviousStateProduction, ServiceCycleReplayCompletenessCode.RequiredRecordMissing,
        ServiceCycleReplayRecordKind.PreviousState, 0)]
    [InlineData(ReplayOomPhase.PreviousStateCodec, ServiceCycleReplayCompletenessCode.CodecFaulted,
        ServiceCycleReplayRecordKind.PreviousState, 0)]
    [InlineData(ReplayOomPhase.ActionCodec, ServiceCycleReplayCompletenessCode.CodecFaulted,
        ServiceCycleReplayRecordKind.Action, 0)]
    [InlineData(ReplayOomPhase.NextStateProduction, ServiceCycleReplayCompletenessCode.RequiredRecordMissing,
        ServiceCycleReplayRecordKind.NextState, 0)]
    [InlineData(ReplayOomPhase.NextStateCodec, ServiceCycleReplayCompletenessCode.CodecFaulted,
        ServiceCycleReplayRecordKind.NextState, 0)]
    public void ObservationalOutOfMemoryIsIsolatedAndFinalizesExactlyOneFooterPerCycle(
        ReplayOomPhase phase,
        ServiceCycleReplayCompletenessCode expectedCode,
        ServiceCycleReplayRecordKind expectedKind,
        int expectedIndex)
    {
        var control = new ReplayControl { ActionCount = 1, OutOfMemoryPhase = phase };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var snapshot = fixture.RunCycle();

        Assert.False(snapshot.Fault.IsValid);
        Assert.Equal(1, snapshot.ActionCount);
        Assert.Equal(WakePolicy.Immediate, snapshot.ActiveWake);
        Assert.Equal(1, snapshot.Projection.Snapshot.GetEntry(0).Value.Integer);
        var recording = fixture.Session.Snapshot;
        Assert.Equal(expectedCode, recording.Completeness.Code);
        Assert.Equal(expectedKind, recording.Completeness.FailureLocation.Record.Kind);
        Assert.Equal(expectedIndex, recording.Completeness.FailureLocation.Record.Index);
        if (expectedCode == ServiceCycleReplayCompletenessCode.CodecFaulted)
            Assert.Equal(ServiceCycleReplayFaultCode.CodecThrew, recording.Fault.Code);
        else
            Assert.False(recording.Fault.IsValid);
        Assert.True(fixture.Session.TryReadHighWaterFence(out var firstFence));
        Assert.Equal(1, firstFence.FooterCount);
        var firstFooter = fixture.Session.ReadFooter(0, in firstFence);
        Assert.Equal(expectedCode, firstFooter.Completeness.Code);
        Assert.Equal(recording.FirstIncompleteCycle, firstFooter.Context.Cycle);

        fixture.DrainBatch();
        var second = fixture.RunCycle();
        Assert.False(second.Fault.IsValid);
        Assert.Equal(1, second.ActionCount);
        Assert.True(fixture.Session.TryReadHighWaterFence(out var secondFence));
        Assert.Equal(2, secondFence.FooterCount);
        Assert.Equal(1, fixture.Session.ReadFooter(0, in secondFence).Sequence);
        Assert.Equal(2, fixture.Session.ReadFooter(1, in secondFence).Sequence);
    }

    [Fact]
    public void StackOverflowRemainsFatalAndRecorderDoesNotAttemptRecovery()
    {
        // A real stack overflow is process-fatal. A synthetic instance verifies the explicit no-recovery filter.
        var control = new ReplayControl { ActionCount = 1, StackOverflowCodecAtCall = 1 };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var snapshot = fixture.RunCycle();

        Assert.True(snapshot.Fault.IsValid);
        Assert.Equal(ServiceFaultCategory.Evaluation, snapshot.Fault.Category);
        Assert.True(fixture.Session.Snapshot.Completeness.IsComplete);
        Assert.Equal(0, fixture.Session.Snapshot.HighWater.FooterCount);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void TinyBudgetRejectsHugeBatchInConstantOwnerWorkAndExportsTypedIncompleteArtifact()
    {
        var control = new ReplayControl
        {
            ActionCount = 100_000,
            RejectAtIndex = 0,
        };
        using var fixture = new ReplayFixture(
            control,
            new ServiceCycleReplaySessionOptions(true, 2, 16, 4));
        var evaluationElapsed = Stopwatch.StartNew();

        var payloadCycle = RunHugePayloadCycle(fixture, control);
        var snapshot = payloadCycle.Snapshot;
        evaluationElapsed.Stop();

        Assert.Equal(100_000, snapshot.ActionCount);
        Assert.False(snapshot.Fault.IsValid);
        Assert.Equal(3, control.CodecCalls);
        Assert.Equal(
            ServiceCycleReplayCompletenessCode.ByteBudgetExhausted,
            fixture.Session.Snapshot.Completeness.Code);
        Assert.True(evaluationElapsed.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(fixture.Session.TryReadHighWaterFence(out var fence));
        Assert.Equal(2, fence.ByteCount);
        Assert.Equal(2, fence.RecordCount);

        var dispatch = fixture.Runner.TryExecuteOneNonBlocking(fixture.Clock.Now);

        Assert.True(dispatch.Attempted);
        Assert.True(dispatch.BatchTerminal);
        Assert.Equal(ServiceActionDisposition.Rejected, dispatch.Result.Disposition);
        Assert.Equal(CommonActionResultCodes.NativeRejected, dispatch.Result.Code);
        Assert.Equal(0, dispatch.Receipt.TerminalIndex);
        Assert.Equal(99_999, dispatch.Receipt.UntouchedSuffixCount);
        Assert.Equal(0, fixture.Runner.ProbeHandoff().CleanupRequestCount);
        Assert.True(SpinWait.SpinUntil(
            fixture.Runner.TryAdvancePendingMainOwnership,
            TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(
            () => !fixture.Runner.ProbeHandoff().CleanupPending,
            TimeSpan.FromSeconds(2)));
        var cleaned = fixture.Runner.ProbeHandoff();
        Assert.Equal(1, cleaned.CleanupRequestCount);
        Assert.Equal(1, cleaned.CleanupAcknowledgementCount);
        Assert.NotEqual(Environment.CurrentManagedThreadId, cleaned.LastCleanupThreadId);
        Assert.False(fixture.Runner.TryAdvancePendingMainOwnership());
        AssertPayloadCollected(payloadCycle.Payload);

        Assert.True(fixture.Session.TryReadSnapshot(out var recording));
        var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(0)];
        ServiceCycleTraceCodec.Encode(
            recording.TraceSession,
            default,
            ReadOnlySpan<ServiceCycleSemanticEvent>.Empty,
            semanticBytes);
        var encoded = ServiceCycleReplayArtifactCodec.Encode(
            semanticBytes,
            fixture.Session,
            in recording);
        Assert.True(encoded.Length <= ServiceCycleReplayArtifactCodec.GetMaximumEncodedLength(0, fixture.Session));
        var artifact = ServiceCycleReplayArtifactCodec.Decode(encoded);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.RecordingIncomplete, artifact.Eligibility);
        Assert.Equal(ServiceCycleReplayCompletenessCode.ByteBudgetExhausted, artifact.Completeness.Code);
        var plan = new ServiceCycleReplayProductionArtifactPlan(artifact);
        var rejected = ServiceCycleReplayProductionPreflight.Validate(
            plan,
            Array.Empty<IServiceCycleReplayExecutionRegistration?>());
        Assert.True(rejected.HasValue);
        Assert.False(rejected.Value.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete,
            rejected.Value.Failure.Fault.DetailCode);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Payload, ServiceRunnerSnapshot Snapshot) RunHugePayloadCycle(
        ReplayFixture fixture,
        ReplayControl control)
    {
        var payload = new ReplayPayload(1);
        var reference = new WeakReference(payload);
        control.ActionPayload = payload;
        var snapshot = fixture.RunCycle();
        control.ActionPayload = null;
        return (reference, snapshot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertPayloadCollected(WeakReference payload)
    {
        for (var attempt = 0; attempt < 3 && payload.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(payload.IsAlive);
    }

    [Fact]
    public void RecordHeaderAndFooterExhaustionLatchExactStableScopes()
    {
        var headerControl = new ReplayControl { ActionCount = 1 };
        using (var header = new ReplayFixture(
                   headerControl,
                   new ServiceCycleReplaySessionOptions(true, 64, 2, 4)))
        {
            header.RunCycle();
            Assert.Equal(
                ServiceCycleReplayCompletenessCode.RecordCapacityExhausted,
                header.Session.Snapshot.Completeness.Code);
            Assert.Equal(
                ServiceCycleReplayRecordKind.Action,
                header.Session.Snapshot.Completeness.FailureLocation.Record.Kind);
            Assert.True(header.Session.TryReadSnapshot(out var headerRecording));
            var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(0)];
            ServiceCycleTraceCodec.Encode(
                headerRecording.TraceSession,
                default,
                ReadOnlySpan<ServiceCycleSemanticEvent>.Empty,
                semanticBytes);
            var artifact = ServiceCycleReplayArtifactCodec.Decode(
                ServiceCycleReplayArtifactCodec.Encode(
                    semanticBytes,
                    header.Session,
                    in headerRecording));
            Assert.Equal(
                ServiceCycleReplayCompletenessCode.RecordCapacityExhausted,
                artifact.Completeness.Code);
        }

        var footerControl = new ReplayControl();
        using var footer = new ReplayFixture(
            footerControl,
            new ServiceCycleReplaySessionOptions(true, 64, 16, 1));
        footer.RunCycle();
        footer.Clock.Advance(new MonotonicDuration(1));
        footer.RunCycle();
        Assert.Equal(
            ServiceCycleReplayCompletenessCode.CycleIncomplete,
            footer.Session.Snapshot.Completeness.Code);
        Assert.Equal(
            ServiceCycleReplayFailureScope.Cycle,
            footer.Session.Snapshot.Completeness.FailureLocation.Scope);
    }

    [Fact]
    public void FailurePublicationInProgressCannotSealACompletePartialFooter()
    {
        using var fixture = new ReplayFixture(new ReplayControl(), Bytes(256, 64, 8));
        var failureState = typeof(ServiceCycleReplaySession).GetField(
            "_failureState",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        failureState.SetValue(fixture.Session, -1);

        var runner = fixture.RunCycle();

        Assert.True(runner.PreviousReceipt.IsPresent);
        failureState.SetValue(fixture.Session, 0);
        var identity = runner.PreviousReceipt.Cycle;
        var cycle = new ServiceCycleReplayCycleKey(1, in identity);
        var missing = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0);
        fixture.Session.MarkRequiredRecordMissing(in cycle, missing);
        Assert.True(fixture.Session.TryReadSnapshot(out var recording));
        var fence = recording.HighWater;
        var footer = fixture.Session.ReadFooter(0, in fence);
        Assert.False(footer.Completeness.IsComplete);
        Assert.Equal(ServiceCycleReplayCompletenessCode.CycleIncomplete, footer.Completeness.Code);
        Assert.Equal(0, footer.RetainedRecordCount);

        var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(0)];
        ServiceCycleTraceCodec.Encode(
            recording.TraceSession,
            default,
            ReadOnlySpan<ServiceCycleSemanticEvent>.Empty,
            semanticBytes);
        var artifact = ServiceCycleReplayArtifactCodec.Decode(
            ServiceCycleReplayArtifactCodec.Encode(
                semanticBytes,
                fixture.Session,
                in recording));
        Assert.False(artifact.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.RecordingIncomplete, artifact.Eligibility);
    }

    [Theory]
    [InlineData(true, ServiceCycleReplayCycleFooterDisposition.EvaluationAborted)]
    [InlineData(false, ServiceCycleReplayCycleFooterDisposition.ProjectionAborted)]
    public void FeatureFaultAbortsProvisionalTransactionBeforeOrdinaryRecovery(
        bool evaluation,
        ServiceCycleReplayCycleFooterDisposition expected)
    {
        var control = new ReplayControl
        {
            ActionCount = 2,
            ThrowEvaluation = evaluation,
            ThrowProjection = !evaluation,
        };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var snapshot = fixture.RunCycle();

        Assert.True(snapshot.Fault.IsValid);
        Assert.Equal(
            evaluation ? ServiceFaultCategory.Evaluation : ServiceFaultCategory.StateProjection,
            snapshot.Fault.Category);
        Assert.True(fixture.Session.TryReadHighWaterFence(out var fence));
        var recorded = fixture.Session.ReadFooter(0, in fence);
        Assert.Equal(expected, recorded.Disposition);
        Assert.False(recorded.HasProjection);
    }

    [Fact]
    public void SuccessfulFooterCarriesExactContextWakeProjectionAndNumericOrdinalKey()
    {
        var control = new ReplayControl
        {
            ActionCount = 1,
            ReturnedWake = WakePolicy.AfterBatch(new MonotonicDuration(19)),
        };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));
        fixture.RunCycle();

        Assert.True(fixture.Session.TryReadHighWaterFence(out var fence));
        var footer = fixture.Session.ReadFooter(0, in fence);
        Assert.Equal(1, footer.Context.Cycle.TraceServiceKey);
        Assert.Equal((ulong)1, footer.Context.Cycle.Lifecycle);
        Assert.Equal(control.ReturnedWake, footer.ReturnedWake);
        Assert.Equal(1L, footer.Projection.GetEntry(0).Value.Integer);
        Assert.Equal(1, footer.ExpectedActionCount);
        var header = fixture.Session.ReadRecordHeader(0, in fence);
        Assert.Equal(1, header.Cycle.TraceServiceKey);
    }

    [Fact]
    public void DefaultEvaluatorWakeIsRecordedAsTheConcreteRuntimeWake()
    {
        var control = new ReplayControl
        {
            ActionCount = 0,
            ReturnedWake = WakePolicy.Default,
        };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var snapshot = fixture.RunCycle();

        Assert.Equal(WakePolicy.Immediate, snapshot.ActiveWake);
        Assert.True(fixture.Session.TryReadHighWaterFence(out var fence));
        var footer = fixture.Session.ReadFooter(0, in fence);
        Assert.True(footer.HasReturnedWake);
        Assert.Equal(WakePolicy.Immediate, footer.ReturnedWake);
    }

    [Fact]
    public void InvalidEvaluatorWakeKeepsOrdinaryFaultCategoryAndDoesNotWedgeRecording()
    {
        var constructor = typeof(WakePolicy).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(WakePolicyKind), typeof(MonotonicDuration), typeof(MonotonicTimestamp) },
            modifiers: null)!;
        var invalidWake = (WakePolicy)constructor.Invoke(
            new object[] { (WakePolicyKind)99, default(MonotonicDuration), default(MonotonicTimestamp) });
        var control = new ReplayControl { ReturnedWake = invalidWake };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var failed = fixture.RunCycle();

        Assert.Equal(ServiceFaultCategory.ResponseValidation, failed.Fault.Category);
        Assert.True(fixture.Session.TryReadHighWaterFence(out var firstFence));
        Assert.Equal(1, firstFence.FooterCount);
        Assert.Equal(
            ServiceCycleReplayCycleFooterDisposition.EvaluationAborted,
            fixture.Session.ReadFooter(0, in firstFence).Disposition);

        control.ReturnedWake = WakePolicy.Immediate;
        fixture.Clock.AdvanceTo(failed.NextWakeDue);
        var recovered = fixture.RunCycle();

        Assert.False(recovered.Fault.IsValid);
        Assert.True(fixture.Session.TryReadHighWaterFence(out var recoveredFence));
        Assert.Equal(2, recoveredFence.FooterCount);
        Assert.Equal(
            ServiceCycleReplayCycleFooterDisposition.Provisional,
            fixture.Session.ReadFooter(1, in recoveredFence).Disposition);
    }

    [Fact]
    public void CaptureRecordFailureNeverFaultsGameplayCapture()
    {
        var control = new ReplayControl { ActionCount = 1, ThrowInputRecord = true };
        using var fixture = new ReplayFixture(control, Bytes(256, 64, 8));

        var snapshot = fixture.RunCycle();

        Assert.False(snapshot.Fault.IsValid);
        Assert.Equal(1, snapshot.ActionCount);
        Assert.Equal(
            ServiceCycleReplayCompletenessCode.RequiredRecordMissing,
            fixture.Session.Snapshot.Completeness.Code);
        Assert.Equal(0, control.CodecCalls);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void HighWaterFenceReadIsNonBlockingAndCoherent()
    {
        var control = new ReplayControl { ActionCount = 10_000 };
        using var fixture = new ReplayFixture(control, Bytes(200_000, 20_000, 4));
        Assert.True(fixture.Runner.TryStartCycle(fixture.Clock.Now).Queued);
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < 100_000; index++)
        {
            if (!fixture.Session.TryReadSnapshot(out var recording)) continue;
            Assert.InRange(recording.HighWater.ByteCount, 0, fixture.Session.ByteCapacity);
            Assert.InRange(recording.HighWater.RecordCount, 0, fixture.Session.RecordCapacity);
            if (recording.Completeness.IsComplete)
                Assert.False(recording.FirstIncompleteCycle.IsValid);
            else
                Assert.True(recording.FirstIncompleteCycle.IsValid);
        }
        timer.Stop();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        ServiceRunnerTestWait.ForPhase(fixture.Runner, ServiceHandoffPhase.ResponseReady);
    }

    [Fact]
    public void OfflineFooterWaitReturnsImmediatelyForExistingFooterWithoutPulsing()
    {
        var session = CreateSession(Bytes(32, 4, 2));
        var footer = CreateFooter(1);

        Assert.True(session.TryAppendFooter(in footer, out var sequence));
        Assert.True(session.WaitForFooterAfter(0, TimeSpan.Zero));
        Assert.False(session.WaitForFooterAfter(sequence, TimeSpan.Zero));
        Assert.Equal(0, session.OfflineFooterWakePulseCount);
    }

    [Fact]
    public async Task OfflineFooterWaitAppendRaceWakesWithCoherentFence()
    {
        var session = CreateSession(Bytes(32, 4, 2));
        var waiter = Task.Run(() => session.WaitForFooterAfter(0, TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(
            () => session.OfflineFooterWaiterCount == 1,
            TimeSpan.FromSeconds(1)));
        var footer = CreateFooter(1);

        Assert.True(session.TryAppendFooter(in footer, out var sequence));
        Assert.True(await waiter);
        Assert.True(session.TryReadHighWaterFence(out var fence));
        Assert.Equal(sequence, fence.FooterSequence);
        Assert.Equal(1, fence.FooterCount);
        Assert.Equal(sequence, session.ReadFooter(0, in fence).Sequence);
        Assert.Equal(1, session.OfflineFooterWakePulseCount);
        Assert.Equal(0, session.OfflineFooterWaiterCount);
    }

    [Fact]
    public async Task OfflineFooterWaitCannotLoseAnAppendRacingWaiterRegistration()
    {
        var session = CreateSession(Bytes(32, 4, 32));
        for (var index = 0; index < 32; index++)
        {
            var observedSequence = index;
            var waiter = Task.Run(() =>
                session.WaitForFooterAfter(observedSequence, TimeSpan.FromSeconds(2)));
            var footer = CreateFooter((ulong)index + 1);
            Assert.True(session.TryAppendFooter(in footer, out var appendedSequence));
            Assert.Equal(observedSequence + 1, appendedSequence);
            Assert.True(await waiter);
        }
    }

    [Fact]
    public async Task OfflineFooterWaitPulseWakesAllCurrentWaiters()
    {
        var session = CreateSession(Bytes(32, 4, 2));
        var waiters = new[]
        {
            Task.Run(() => session.WaitForFooterAfter(0, TimeSpan.FromSeconds(2))),
            Task.Run(() => session.WaitForFooterAfter(0, TimeSpan.FromSeconds(2))),
            Task.Run(() => session.WaitForFooterAfter(0, TimeSpan.FromSeconds(2))),
        };
        Assert.True(SpinWait.SpinUntil(
            () => session.OfflineFooterWaiterCount == waiters.Length,
            TimeSpan.FromSeconds(1)));
        var footer = CreateFooter(1);

        Assert.True(session.TryAppendFooter(in footer, out _));
        var results = await Task.WhenAll(waiters);
        Assert.All(results, Assert.True);
        Assert.Equal(1, session.OfflineFooterWakePulseCount);
        Assert.Equal(0, session.OfflineFooterWaiterCount);
    }

    [Fact]
    public void OfflineFooterWaitHonorsTimeoutAndDisabledOrFullTerminalStates()
    {
        var timeoutSession = CreateSession(Bytes(32, 4, 2));
        Assert.False(timeoutSession.WaitForFooterAfter(0, TimeSpan.FromMilliseconds(20)));
        Assert.Equal(0, timeoutSession.OfflineFooterWaiterCount);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            timeoutSession.WaitForFooterAfter(0, Timeout.InfiniteTimeSpan));

        var disabled = CreateSession(new ServiceCycleReplaySessionOptions(false, 0, 0, 0));
        Assert.False(disabled.WaitForFooterAfter(0, TimeSpan.FromSeconds(2)));

        var full = CreateSession(Bytes(32, 4, 1));
        var footer = CreateFooter(1);
        Assert.True(full.TryAppendFooter(in footer, out var terminalSequence));
        var timer = Stopwatch.StartNew();
        Assert.False(full.WaitForFooterAfter(terminalSequence, TimeSpan.FromSeconds(2)));
        timer.Stop();
        Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void MissingAndMismatchedCaptureBridgeInputsLatchRequiredRecordWithoutThrowing()
    {
        var session = CreateSession(Bytes(64, 16, 4));
        var bridge = new ServiceCycleReplayInputBridge<ReplayInputRecord>(session, 1);
        bridge.BindTraceServiceKey(1);
        bridge.MarkFrameReady();
        var expectedIdentity = CycleIdentity(1);
        var expectedContext = new ServiceCycleContext(expectedIdentity, default, new MonotonicTimestamp(10));

        Assert.False(bridge.TryTake(in expectedContext, out _, out _));
        Assert.Equal(
            ServiceCycleReplayCompletenessCode.RequiredRecordMissing,
            session.Snapshot.Completeness.Code);

        var mismatchSession = CreateSession(Bytes(64, 16, 4));
        var mismatch = new ServiceCycleReplayInputBridge<ReplayInputRecord>(mismatchSession, 1);
        mismatch.BindTraceServiceKey(1);
        var actualIdentity = CycleIdentity(2);
        var actualKey = new ServiceCycleReplayCycleKey(1, in actualIdentity);
        var input = new ReplayInputRecord(1, 2, 3);
        mismatch.Publish(in actualKey, in input);
        Assert.False(mismatch.TryTake(in expectedContext, out _, out _));
        Assert.Equal(expectedIdentity.Cycle.Value, mismatchSession.Snapshot.FirstIncompleteCycle.Cycle);
    }

    [Fact]
    public void FailedFrameConstructionRollsBackOrdinaryAndReplayRegistration()
    {
        var clock = new ThreadSafeTestClock(10);
        using var registry = new ServiceCycleRegistry(1, clock);
        var control = new ReplayControl { FrameFactoryFailures = 1 };
        var session = CreateSession(Bytes(64, 16, 4));

        Assert.Throws<InvalidOperationException>(() => registry.RegisterReplay(
            new ReplayDefinition(control),
            new ReplayConfig(1),
            session,
            new LifecycleGeneration(1)));
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, registry.OrdinalCount);

        using var registration = registry.RegisterReplay(
            new ReplayDefinition(control),
            new ReplayConfig(1),
            session,
            new LifecycleGeneration(1));
        Assert.Equal(0, registration.Ordinal);
    }

    [Fact]
    public void CurrentAndRetiringWorkersEncodeConcurrentlyWithIndependentScratch()
    {
        ReplayCodecBarrier.Reset(2);
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var control = new ReplayControl { BlockCodec = true };
        var session = CreateSession(Bytes(512, 64, 8));
        using var registration = registry.RegisterReplay(
            new ReplayDefinition(control),
            new ReplayConfig(1),
            session,
            new LifecycleGeneration(1));
        var retiring = registration.Runner;
        Assert.True(retiring.TryStartCycle(clock.Now).Queued);
        Assert.True(ReplayCodecBarrier.WaitForEntrants(1));

        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(2)));
        registry.ReconcileLifecycle(clock.Now);
        var current = registration.Runner;
        Assert.NotSame(retiring, current);
        Assert.True(current.TryStartCycle(clock.Now).Queued);
        Assert.True(ReplayCodecBarrier.WaitForEntrants(2));
        Assert.True(ReplayCodecBarrier.MaximumConcurrent >= 2);

        ReplayCodecBarrier.Release();
        ServiceRunnerTestWait.ForPhase(current, ServiceHandoffPhase.ResponseReady);
        Assert.True(current.TryAcquireResponse());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryLedgerStillRejectsActualWorkerAndFrameAliases(bool workerAlias)
    {
        ReplayCodecBarrier.Reset(1);
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var control = new ReplayControl
        {
            BlockCodec = true,
            ReuseWorker = workerAlias,
            ReuseFrame = !workerAlias,
        };
        var definition = new ReplayDefinition(control);
        var session = CreateSession(Bytes(512, 64, 8));
        using var registration = registry.RegisterReplay(
            definition,
            new ReplayConfig(1),
            session,
            new LifecycleGeneration(1));
        var retiring = registration.Runner;
        Assert.True(retiring.TryStartCycle(clock.Now).Queued);
        Assert.True(ReplayCodecBarrier.WaitForEntrants(1));
        try
        {
            Assert.True(registry.RequestLifecycle(new LifecycleGeneration(2)));
            registry.ReconcileLifecycle(clock.Now);
            var lifecycle = registry.GetSlot(0).LifecycleSnapshot;
            Assert.True(lifecycle.ConstructionFault.IsValid);
            Assert.Equal(ServiceFaultCategory.LifecycleConstruction, lifecycle.ConstructionFault.Category);
            Assert.Equal(2, control.WorkerFactoryCalls);
        }
        finally
        {
            ReplayCodecBarrier.Release();
        }
    }

    [Fact]
    public void UncapturedLiveCandidateStillRejectsSharedCodecAliasesDuringLifecycleReplacement()
    {
        ReplayFrameReleaseBarrier.Reset();
        try
        {
            var clock = new ThreadSafeTestClock(100);
            using var registry = new ServiceCycleRegistry(1, clock);
            var control = new ReplayControl
            {
                ReuseCodecs = true,
                CodecSchemaVersion = 1,
                BlockFrameRelease = true,
            };
            var session = CreateSession(Bytes(512, 64, 8));
            using var registration = registry.RegisterReplay(
                new ReplayDefinition(control),
                new ReplayConfig(1),
                session,
                new LifecycleGeneration(1));
            Assert.True(session.TryReadCodecManifest(1, out var originalManifest));

            Assert.True(registry.RequestLifecycle(new LifecycleGeneration(2)));
            registry.ReconcileLifecycle(clock.Now);

            var lifecycle = registry.GetSlot(0).LifecycleSnapshot;
            Assert.True(lifecycle.ConstructionFault.IsValid);
            Assert.Equal(ServiceFaultCategory.LifecycleConstruction, lifecycle.ConstructionFault.Category);
            Assert.Equal(2, control.WorkerFactoryCalls);
            Assert.True(session.TryReadCodecManifest(1, out var retainedManifest));
            Assert.Equal(originalManifest, retainedManifest);
            Assert.Equal((ushort)1, retainedManifest.CycleInput.SchemaVersion);
            Assert.True(session.TryReadSnapshot(out var recording));
            Assert.Equal(1, recording.CodecManifests.Count);
        }
        finally
        {
            ReplayFrameReleaseBarrier.Release();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WarmSuccessfulDisabledAndExhaustedWorkerCyclesAllocateNothing(int mode)
    {
        var control = new ReplayControl { ActionCount = 64 };
        var options = mode switch
        {
            0 => Bytes(512, 256, 8),
            1 => new ServiceCycleReplaySessionOptions(false, 0, 0, 0),
            _ => new ServiceCycleReplaySessionOptions(true, 2, 16, 8),
        };
        using var fixture = new ReplayFixture(control, options, measureWorkerAllocations: true);
        fixture.RunCycle();
        fixture.DrainBatch();

        var warmed = fixture.RunCycle();

        Assert.Equal(0, warmed.WorkerCycleAllocatedBytes);
        Assert.Equal(64, warmed.ActionCount);
    }

    [Fact]
    public void FrozenCodecManifestRejectsReplacementDescriptorDrift()
    {
        ReplayCodecBarrier.Reset(1);
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var control = new ReplayControl { BlockCodec = true, CodecSchemaVersion = 1 };
        var session = CreateSession(Bytes(512, 64, 8));
        using var registration = registry.RegisterReplay(
            new ReplayDefinition(control),
            new ReplayConfig(1),
            session,
            new LifecycleGeneration(1));
        var retiring = registration.Runner;
        Assert.True(retiring.TryStartCycle(clock.Now).Queued);
        Assert.True(ReplayCodecBarrier.WaitForEntrants(1));
        try
        {
            control.CodecSchemaVersion = 2;
            Assert.True(registry.RequestLifecycle(new LifecycleGeneration(2)));
            registry.ReconcileLifecycle(clock.Now);
            var lifecycle = registry.GetSlot(0).LifecycleSnapshot;
            Assert.True(lifecycle.ConstructionFault.IsValid);
            Assert.Equal(ServiceFaultCategory.LifecycleConstruction, lifecycle.ConstructionFault.Category);
            Assert.True(session.TryReadCodecManifest(1, out var manifest));
            Assert.Equal((ushort)1, manifest.CycleInput.SchemaVersion);
        }
        finally
        {
            ReplayCodecBarrier.Release();
        }
    }

    [Fact]
    public void RecordingSnapshotFencesSparseManifestPublicationWithoutExposingLaterBindings()
    {
        var session = new ServiceCycleReplaySession(
            new ServiceCycleTraceSessionId(73),
            new ServiceCycleReplaySessionOptions(false, 0, 0, 0, serviceCapacity: 2));
        var descriptor = new ServiceCycleReplayCodecDescriptor(7, 11);
        session.BindCodecManifest(2, new object(), descriptor, descriptor, descriptor);

        Assert.True(session.TryReadSnapshot(out var captured));
        Assert.Equal((ulong)73, captured.TraceSession.Value);
        Assert.Equal(1, captured.CodecManifests.Count);
        Assert.True(session.TryReadCodecManifestAt(0, captured.CodecManifests, out var first));
        Assert.Equal(2, first.TraceServiceKey);

        session.BindCodecManifest(1, new object(), descriptor, descriptor, descriptor);

        Assert.False(session.TryReadCodecManifestAt(1, captured.CodecManifests, out _));
        Assert.True(session.TryReadSnapshot(out var later));
        Assert.Equal(2, later.CodecManifests.Count);
        Assert.True(later.CodecManifests.Publication > captured.CodecManifests.Publication);
        Assert.True(session.TryReadCodecManifestAt(1, later.CodecManifests, out var second));
        Assert.Equal(1, second.TraceServiceKey);

        Assert.True(session.TryReadSnapshot(out _));
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
        {
            if (!session.TryReadSnapshot(out var repeated) ||
                !session.TryReadCodecManifestAt(0, repeated.CodecManifests, out _))
            {
                throw new InvalidOperationException("Stable replay snapshot unexpectedly became unavailable.");
            }
        }
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, allocatedAfter - allocatedBefore);
    }

    [Fact]
    public void GlobalInterleavingMakesFooterSequencesBoundsRatherThanContiguousOwnership()
    {
        var session = CreateSession(Bytes(64, 16, 4));
        var descriptor = new ServiceCycleReplayCodecDescriptor(1, 1);
        var scratch = new byte[] { 1 };
        var firstIdentity = CycleIdentity(1);
        var secondIdentity = CycleIdentity(2);
        var first = new ServiceCycleReplayCycleKey(1, in firstIdentity);
        var second = new ServiceCycleReplayCycleKey(1, in secondIdentity);
        Assert.True(session.TryAppendRecord(
            in first,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0),
            in descriptor,
            scratch,
            1,
            out var firstSequence));
        Assert.True(session.TryAppendRecord(
            in second,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0),
            in descriptor,
            scratch,
            1,
            out _));
        Assert.True(session.TryAppendRecord(
            in first,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.PreviousState, 0),
            in descriptor,
            scratch,
            1,
            out var lastSequence));
        var cycleContext = new ServiceCycleContext(firstIdentity, default, new MonotonicTimestamp(11));
        var replayContext = new ServiceCycleReplayContext(1, in cycleContext);
        var footer = new ServiceCycleReplayCycleFooter(
            0,
            replayContext,
            ServiceCycleReplayCycleFooterDisposition.Provisional,
            WakePolicy.Immediate,
            true,
            default,
            true,
            0,
            firstSequence,
            lastSequence,
            2,
            ServiceCycleReplayCompleteness.Complete,
            1,
            Stopwatch.Frequency,
            0);
        Assert.True(session.TryAppendFooter(in footer, out _));

        Assert.True(session.TryReadHighWaterFence(out var fence));
        var stored = session.ReadFooter(0, in fence);
        Assert.Equal(2, stored.RetainedRecordCount);
        Assert.True(stored.LastRecordSequence - stored.FirstRecordSequence + 1 > stored.RetainedRecordCount);
    }

    private static (int ActionCount, WakePolicy WakePolicy,
        ServiceProjectionPublication Projection, int ExecutedActionCount, int ExecutedActionOrderHash,
        (int Index, ServiceActionDisposition Disposition, int Code, NativeMutationOutcome NativeOutcome,
            int NativeCalls, int MutationAttempts, int MutationsCommitted)[] ActionTerminals,
        BatchReceipt Receipt, BatchReceipt PreviousReceipt, int RecordProductions,
        ServiceCycleReplayHighWaterFence Fence) RunSuccessfulCycle(bool enabled, int actionCount)
    {
        var control = new ReplayControl { ActionCount = actionCount };
        using var fixture = new ReplayFixture(
            control,
            enabled ? Bytes(256, 64, 8) : new ServiceCycleReplaySessionOptions(false, 0, 0, 0));
        var snapshot = fixture.RunCycle();
        var terminals = new (
            int Index,
            ServiceActionDisposition Disposition,
            int Code,
            NativeMutationOutcome NativeOutcome,
            int NativeCalls,
            int MutationAttempts,
            int MutationsCommitted)[actionCount];
        var receipt = default(BatchReceipt);
        for (var index = 0; index < actionCount; index++)
        {
            var dispatch = fixture.Runner.TryExecuteOne(fixture.Clock.Now);
            Assert.True(dispatch.Attempted);
            var result = dispatch.Result;
            var native = result.NativeEvidence;
            var calls = result.NativeCallOutcome;
            terminals[index] = (
                dispatch.ActionFact.Context.ActionIndex,
                result.Disposition,
                result.Code.Value,
                native.Outcome,
                calls.NativeCallsAttempted,
                calls.MutationAttempts,
                calls.MutationsCommitted);
            if (dispatch.BatchTerminal) receipt = dispatch.Receipt;
        }
        var terminalSnapshot = fixture.Runner.Snapshot;
        fixture.Session.TryReadHighWaterFence(out var fence);
        return (
            snapshot.ActionCount,
            snapshot.ActiveWake,
            snapshot.Projection,
            control.ExecutedActionCount,
            control.ExecutedActionOrderHash,
            terminals,
            receipt,
            terminalSnapshot.PreviousReceipt,
            control.RecordProductions,
            fence);
    }

    private static void AssertReceiptsEqual(in BatchReceipt expected, in BatchReceipt actual)
    {
        Assert.Equal(expected.Cycle, actual.Cycle);
        Assert.Equal(expected.Batch, actual.Batch);
        Assert.Equal(expected.Disposition, actual.Disposition);
        Assert.Equal(expected.ActionCount, actual.ActionCount);
        Assert.Equal(expected.CommittedCount, actual.CommittedCount);
        Assert.Equal(expected.TerminalIndex, actual.TerminalIndex);
        Assert.Equal(expected.UntouchedSuffixCount, actual.UntouchedSuffixCount);
        Assert.Equal(expected.ResultCode, actual.ResultCode);
        Assert.Equal(expected.HasTerminalAction, actual.HasTerminalAction);
        Assert.Equal(expected.NativeCallOutcome.NativeCallsAttempted, actual.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(expected.NativeCallOutcome.MutationAttempts, actual.NativeCallOutcome.MutationAttempts);
        Assert.Equal(expected.NativeCallOutcome.MutationsCommitted, actual.NativeCallOutcome.MutationsCommitted);
        Assert.Equal(expected.CompletedAt, actual.CompletedAt);
        Assert.Equal(expected.HasEmergencyStopContext, actual.HasEmergencyStopContext);
    }

    private static ServiceCycleReplaySessionOptions Bytes(int bytes, int records, int footers) =>
        new(true, bytes, records, footers);

    private static ServiceCycleReplaySession CreateSession(ServiceCycleReplaySessionOptions options) =>
        new(new ServiceCycleTraceSessionId(77), options);

    private static ServiceCycleIdentity CycleIdentity(ulong cycle) => new(
        new ServiceId("test.bridge"),
        new LifecycleGeneration(1),
        new ConfigGeneration(1),
        new StrategyGeneration(1),
        new CaptureSequence(cycle),
        new CycleId(cycle));

    private static ServiceCycleReplayCycleFooter CreateFooter(ulong cycle)
    {
        var identity = CycleIdentity(cycle);
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp((long)cycle));
        return new ServiceCycleReplayCycleFooter(
            0,
            new ServiceCycleReplayContext(1, in context),
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
            Stopwatch.Frequency,
            0);
    }
}

internal sealed class ReplayFixture : IDisposable
{
    private readonly ServiceCycleRegistry _registry;
    private readonly ServiceCycleReplayRegistration<ReplayFrame, ReplayConfig, ReplayState, ReplayAction> _registration;

    internal ReplayFixture(
        ReplayControl control,
        ServiceCycleReplaySessionOptions options,
        bool measureWorkerAllocations = false)
    {
        Clock = new ThreadSafeTestClock(100);
        Session = new ServiceCycleReplaySession(new ServiceCycleTraceSessionId(77), options);
        _registry = new ServiceCycleRegistry(1, Clock, measureWorkerAllocations);
        _registration = _registry.RegisterReplay(
            new ReplayDefinition(control),
            new ReplayConfig(7),
            Session,
            new LifecycleGeneration(1));
    }

    internal ThreadSafeTestClock Clock { get; }
    internal ServiceCycleReplaySession Session { get; }
    internal ServiceRunner<ReplayFrame, ReplayConfig, ReplayState, ReplayAction> Runner => _registration.Runner;

    internal ServiceRunnerSnapshot RunCycle()
    {
        Assert.True(Runner.TryStartCycle(Clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(Runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(Runner.TryAcquireResponse());
        return Runner.Snapshot;
    }

    internal void DrainBatch()
    {
        var actionCount = Runner.Snapshot.ActionCount;
        for (var index = 0; index < actionCount; index++)
            Runner.TryExecuteOne(Clock.Now);
    }

    public void Dispose()
    {
        _registration.Dispose();
        _registry.Dispose();
    }
}

internal sealed class ReplayDefinition : IServiceCycleReplayDefinition<
    ReplayFrame, ReplayConfig, ReplayState, ReplayAction, ReplayInputRecord, ReplayStateRecord, ReplayActionRecord>
{
    private readonly ReplayControl _control;
    private readonly ReplayFrame _sharedFrame = new();
    private readonly ReplayCodec<ReplayInputRecord> _sharedInputCodec;
    private readonly ReplayCodec<ReplayStateRecord> _sharedStateCodec;
    private readonly ReplayCodec<ReplayActionRecord> _sharedActionCodec;
    private ReplayWorker? _sharedWorker;

    internal ReplayDefinition(ReplayControl control)
    {
        _control = control;
        _sharedInputCodec = new ReplayCodec<ReplayInputRecord>(control);
        _sharedStateCodec = new ReplayCodec<ReplayStateRecord>(control);
        _sharedActionCodec = new ReplayCodec<ReplayActionRecord>(control);
    }
    public ServiceId ServiceId => new("test.replay-recording");
    public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(new MonotonicDuration(1), new MonotonicDuration(8));
    public ReplayFrame CreateFrame()
    {
        if (Interlocked.Exchange(ref _control.FrameFactoryFailures, 0) > 0)
            throw new InvalidOperationException("frame factory");
        return _control.ReuseFrame ? _sharedFrame : new ReplayFrame();
    }
    public ServiceCycleReplayWorker<ReplayFrame, ReplayConfig, ReplayState, ReplayAction,
        ReplayInputRecord, ReplayStateRecord, ReplayActionRecord> CreateWorkerDefinition()
    {
        Interlocked.Increment(ref _control.WorkerFactoryCalls);
        if (!_control.ReuseWorker)
        {
            return _control.ReuseCodecs
                ? new ReplayWorker(
                    _control,
                    _sharedInputCodec,
                    _sharedStateCodec,
                    _sharedActionCodec)
                : new ReplayWorker(_control);
        }
        return _sharedWorker ??= new ReplayWorker(_control);
    }
    public ServiceStartDecision ShouldStart(in ReplayConfig config, in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    public ServiceCaptureResult Capture(
        ref ReplayFrame frame,
        in ReplayConfig config,
        in ServiceCaptureContext context)
    {
        frame.Value = config.Value * 10;
        return ServiceCaptureResult.Captured(new StrategyGeneration(3), CommonServiceDecisionCodes.Captured);
    }
    public ReplayInputRecord CreateCycleInputRecord(
        in ReplayFrame frame,
        in ReplayConfig config,
        in ServiceCaptureContext context,
        in ServiceCaptureResult capture)
    {
        Interlocked.Increment(ref _control.RecordProductions);
        if (_control.ThrowInputRecord) throw new InvalidOperationException("input record");
        if (_control.OutOfMemoryPhase == ReplayOomPhase.CycleInputProduction)
            throw new OutOfMemoryException("input record");
        return new ReplayInputRecord(frame.Value, config.Value, capture.StrategyGeneration.Value);
    }
    public ServiceActionResult TryExecute(
        in ReplayAction action,
        in ReplayConfig config,
        in ServiceActionContext context)
    {
        _control.ExecutedActionCount++;
        _control.ExecutedActionOrderHash = unchecked(_control.ExecutedActionOrderHash * 31 + action.Value + 1);
        if (action.Value == _control.RejectAtIndex)
            return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        return ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)));
    }
}

internal sealed class ReplayWorker : ServiceCycleReplayWorker<
    ReplayFrame, ReplayConfig, ReplayState, ReplayAction, ReplayInputRecord, ReplayStateRecord, ReplayActionRecord>
{
    private readonly ReplayControl _control;

    internal ReplayWorker(ReplayControl control)
        : base(new ReplayCodec<ReplayInputRecord>(control),
            new ReplayCodec<ReplayStateRecord>(control),
            new ReplayCodec<ReplayActionRecord>(control)) => _control = control;

    internal ReplayWorker(
        ReplayControl control,
        ReplayCodec<ReplayInputRecord> inputCodec,
        ReplayCodec<ReplayStateRecord> stateCodec,
        ReplayCodec<ReplayActionRecord> actionCodec)
        : base(inputCodec, stateCodec, actionCodec) => _control = control;

    protected override ReplayState CreateStateCore(LifecycleGeneration lifecycle) => new();
    protected override void ReleaseStateCore(ref ReplayState state) => state = null!;
    protected override void ReleaseFrameCore(ref ReplayFrame frame)
    {
        if (_control.BlockFrameRelease) ReplayFrameReleaseBarrier.Wait();
        frame = null!;
    }
    protected override ReplayStateRecord CreateStateRecordCore(in ReplayState state)
    {
        Interlocked.Increment(ref _control.RecordProductions);
        if ((_control.OutOfMemoryPhase == ReplayOomPhase.PreviousStateProduction && state.Evaluations == 0) ||
            (_control.OutOfMemoryPhase == ReplayOomPhase.NextStateProduction && state.Evaluations != 0))
        {
            throw new OutOfMemoryException("state record");
        }
        return new ReplayStateRecord(state.Evaluations);
    }
    protected override WakePolicy EvaluateCore(
        in ReplayFrame frame,
        in ReplayConfig config,
        in ServiceCycleContext context,
        ref ReplayState state,
        ServiceCycleReplayActionWriter<ReplayAction, ReplayActionRecord> actions)
    {
        state.Evaluations++;
        for (var index = 0; index < _control.ActionCount; index++)
        {
            var action = new ReplayAction(index, _control.ActionPayload);
            var record = new ReplayActionRecord(index);
            Interlocked.Increment(ref _control.RecordProductions);
            actions.Add(in action, in record);
        }
        if (_control.ThrowEvaluation) throw new InvalidOperationException("evaluation");
        return _control.ReturnedWake;
    }
    protected override void ProjectStateCore(
        in ReplayState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output)
    {
        output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Evaluations));
        if (_control.ThrowProjection) throw new InvalidOperationException("projection");
    }
}

internal sealed class ReplayCodec<TRecord> : IServiceCycleReplayCodec<TRecord>
    where TRecord : struct, IServiceCycleReplayRecord
{
    private readonly ReplayControl _control;
    internal ReplayCodec(ReplayControl control) => _control = control;
    public ServiceCycleReplayCodecDescriptor Descriptor => new(
        _control.CodecSchemaVersion == 0 ? 1 : _control.CodecSchemaVersion,
        8);
    public int Encode(in TRecord record, Span<byte> destination)
    {
        if (_control.BlockCodec) ReplayCodecBarrier.Enter();
        if (_control.CodecAllocationBytes > 0 || _control.CodecSpinIterations > 0)
        {
            var workStarted = Stopwatch.GetTimestamp();
            if (_control.CodecAllocationBytes > 0)
            {
                var allocation = new byte[_control.CodecAllocationBytes];
                allocation[0] = 1;
                Interlocked.Add(ref _control.CodecAllocatedPayloadBytes, allocation.Length);
                GC.KeepAlive(allocation);
            }
            if (_control.CodecSpinIterations > 0) Thread.SpinWait(_control.CodecSpinIterations);
            Interlocked.Add(ref _control.CodecWorkTicks, Stopwatch.GetTimestamp() - workStarted);
        }
        var call = Interlocked.Increment(ref _control.CodecCalls);
        if (call == _control.OutOfMemoryPhase.CodecCall()) throw new OutOfMemoryException("codec");
        if (call == _control.StackOverflowCodecAtCall) throw new StackOverflowException("codec");
        if (call == _control.ThrowCodecAtCall) throw new InvalidOperationException("codec");
        if (call == _control.InvalidCodecAtCall) return Descriptor.MaximumEncodedBytes + 1;
        destination[0] = unchecked((byte)call);
        return 1;
    }
    public TRecord Decode(ReadOnlySpan<byte> source) => default;
}

internal sealed class ReplayControl
{
    internal int ActionCount;
    internal int RejectAtIndex = -1;
    internal ReplayPayload? ActionPayload;
    internal int ExecutedActionCount;
    internal int ExecutedActionOrderHash;
    internal int ThrowCodecAtCall;
    internal int InvalidCodecAtCall;
    internal ReplayOomPhase OutOfMemoryPhase;
    internal int StackOverflowCodecAtCall;
    internal int CodecCalls;
    internal int CodecAllocationBytes;
    internal int CodecSpinIterations;
    internal long CodecAllocatedPayloadBytes;
    internal long CodecWorkTicks;
    internal int RecordProductions;
    internal bool ThrowInputRecord;
    internal bool ThrowEvaluation;
    internal bool ThrowProjection;
    internal bool BlockCodec;
    internal bool ReuseWorker;
    internal bool ReuseCodecs;
    internal bool ReuseFrame;
    internal int FrameFactoryFailures;
    internal int WorkerFactoryCalls;
    internal int CodecSchemaVersion = 1;
    internal bool BlockFrameRelease;
    internal WakePolicy ReturnedWake = WakePolicy.Immediate;
}

public enum ReplayOomPhase
{
    None = 0,
    CycleInputProduction = 1,
    CycleInputCodec = 2,
    PreviousStateProduction = 3,
    PreviousStateCodec = 4,
    ActionCodec = 5,
    NextStateProduction = 6,
    NextStateCodec = 7,
}

internal static class ReplayOomPhaseExtensions
{
    internal static int CodecCall(this ReplayOomPhase phase) => phase switch
    {
        ReplayOomPhase.CycleInputCodec => 1,
        ReplayOomPhase.PreviousStateCodec => 2,
        ReplayOomPhase.ActionCodec => 3,
        ReplayOomPhase.NextStateCodec => 4,
        _ => 0,
    };
}

internal static class ReplayFrameReleaseBarrier
{
    private static readonly ManualResetEventSlim Gate = new(false);

    internal static void Reset() => Gate.Reset();
    internal static void Wait() => Gate.Wait();
    internal static void Release() => Gate.Set();
}

internal static class ReplayCodecBarrier
{
    private static readonly ManualResetEventSlim ReleaseGate = new(false);
    private static int _entrants;
    private static int _concurrent;
    private static int _maximumConcurrent;

    internal static int MaximumConcurrent => Volatile.Read(ref _maximumConcurrent);

    internal static void Reset(int expectedEntrants)
    {
        ReleaseGate.Reset();
        Volatile.Write(ref _entrants, 0);
        Volatile.Write(ref _concurrent, 0);
        Volatile.Write(ref _maximumConcurrent, 0);
    }

    internal static void Enter()
    {
        Interlocked.Increment(ref _entrants);
        var concurrent = Interlocked.Increment(ref _concurrent);
        while (true)
        {
            var maximum = Volatile.Read(ref _maximumConcurrent);
            if (maximum >= concurrent ||
                Interlocked.CompareExchange(ref _maximumConcurrent, concurrent, maximum) == maximum)
                break;
        }
        ReleaseGate.Wait();
        Interlocked.Decrement(ref _concurrent);
    }

    internal static bool WaitForEntrants(int count) => SpinWait.SpinUntil(
        () => Volatile.Read(ref _entrants) >= count,
        TimeSpan.FromSeconds(2));

    internal static void Release() => ReleaseGate.Set();
}

internal sealed class ReplayFrame { internal int Value; }
internal readonly struct ReplayConfig
{
    internal ReplayConfig(int value) => Value = value;
    internal int Value { get; }
}
internal sealed class ReplayState { internal int Evaluations; }
internal readonly struct ReplayAction
{
    internal ReplayAction(int value, ReplayPayload? payload = null)
    {
        Value = value;
        Payload = payload;
    }
    internal int Value { get; }
    internal ReplayPayload? Payload { get; }
}
internal sealed class ReplayPayload
{
    internal ReplayPayload(int value) => Value = value;
    internal readonly int Value;
}
internal readonly struct ReplayInputRecord : IServiceCycleReplayRecord
{
    internal ReplayInputRecord(int frame, int config, ulong strategy)
    {
        Frame = frame;
        Config = config;
        Strategy = strategy;
    }
    internal int Frame { get; }
    internal int Config { get; }
    internal ulong Strategy { get; }
}
internal readonly struct ReplayStateRecord : IServiceCycleReplayRecord
{
    internal ReplayStateRecord(int value) => Value = value;
    internal int Value { get; }
}
internal readonly struct ReplayActionRecord : IServiceCycleReplayRecord
{
    internal ReplayActionRecord(int value) => Value = value;
    internal int Value { get; }
}
