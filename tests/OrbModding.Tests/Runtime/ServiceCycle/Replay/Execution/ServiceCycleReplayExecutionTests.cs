using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayExecutionTests
{
    [Fact]
    public void DecoderRequiresExactSchemaBoundAndCanonicalBytes()
    {
        var identity = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0);
        var codec = new InputCodec();
        var bytes = new byte[codec.Descriptor.MaximumEncodedBytes];
        var record = new InputRecord(7, 9, 11);
        var length = codec.Encode(in record, bytes);
        var encoded = new ServiceCycleReplayEncodedRecord(identity, 1, bytes.AsMemory(0, length));

        var decoded = ServiceCycleReplayRecordDecoder.Decode(in encoded, identity, codec);

        Assert.True(decoded.Succeeded);
        Assert.Equal(7, decoded.Record.Frame);
        Assert.Equal(11UL, decoded.Record.Strategy);
        var wrongSchema = new ServiceCycleReplayEncodedRecord(identity, 2, bytes.AsMemory(0, length));
        var rejected = ServiceCycleReplayRecordDecoder.Decode(in wrongSchema, identity, codec);
        Assert.False(rejected.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.DecodeRejected, rejected.Fault.Code);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.RecordSchemaRejected,
            rejected.Fault.DetailCode);
    }

    [Fact]
    public void DecoderCatchesFeatureDecodeAndRejectsNonCanonicalEncoding()
    {
        var identity = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0);
        var throwing = new InputCodec { ThrowDecode = true };
        var encoded = new ServiceCycleReplayEncodedRecord(
            identity,
            1,
            new byte[throwing.Descriptor.MaximumEncodedBytes]);
        var faulted = ServiceCycleReplayRecordDecoder.Decode(in encoded, identity, throwing);
        Assert.Equal(ServiceCycleReplayFaultCode.DecodeRejected, faulted.Fault.Code);

        var nonCanonicalBytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(nonCanonicalBytes, 7);
        BinaryPrimitives.WriteInt32LittleEndian(nonCanonicalBytes.AsSpan(4), 9);
        nonCanonicalBytes[15] ^= 0x40;
        var nonCanonical = new ServiceCycleReplayEncodedRecord(identity, 1, nonCanonicalBytes);
        var rejected = ServiceCycleReplayRecordDecoder.Decode(in nonCanonical, identity, new NonCanonicalInputCodec());
        Assert.False(rejected.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.RecordCanonicalEncodingRejected,
            rejected.Fault.DetailCode);
    }

    [Fact]
    public void DecoderReusesCanonicalScratchAcrossOneDecodePass()
    {
        var identity = new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0);
        var codec = new InputCodec();
        var bytes = new byte[codec.Descriptor.MaximumEncodedBytes];
        var record = new InputRecord(7, 9, 11);
        var length = codec.Encode(in record, bytes);
        var encoded = new ServiceCycleReplayEncodedRecord(identity, 1, bytes.AsMemory(0, length));
        var scratch = new ServiceCycleReplayRecordDecodeScratch();

        Assert.True(ServiceCycleReplayRecordDecoder.Decode(in encoded, identity, codec, scratch).Succeeded);
        Assert.True(ServiceCycleReplayRecordDecoder.Decode(in encoded, identity, codec, scratch).Succeeded);

        Assert.Equal(1, scratch.AllocationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void OracleRunsExactProductionPortForNoActionAndLargeBatch(int actionCount)
    {
        var evaluator = new Evaluator(actionCount);
        var oracle = CreateOracle(evaluator);
        var expected = Cycle(actionCount);

        var result = oracle.Verify(in expected);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.CompletedCycles);
    }

    [Fact]
    public void OracleReportsStableFirstActionStateWakeAndProjectionDivergence()
    {
        var actions = Cycle(3, actionOverrideIndex: 1, actionOverrideValue: 99);
        var actionResult = CreateOracle(new Evaluator(3)).Verify(in actions);
        Assert.Equal(ServiceCycleReplayMismatchCode.Action, actionResult.Mismatch.Mismatch.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.Action, actionResult.Mismatch.Mismatch.Record.Kind);
        Assert.Equal(1, actionResult.Mismatch.Mismatch.Record.Index);
        Assert.Equal(1, actionResult.Mismatch.Mismatch.FieldCode);

        var state = Cycle(0, nextState: 99);
        var stateResult = CreateOracle(new Evaluator(0)).Verify(in state);
        Assert.Equal(ServiceCycleReplayMismatchCode.NextState, stateResult.Mismatch.Mismatch.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.NextState, stateResult.Mismatch.Mismatch.Record.Kind);
        Assert.Equal(1, stateResult.Mismatch.Mismatch.FieldCode);

        var wake = Cycle(0, wake: WakePolicy.AfterBatch(new MonotonicDuration(8)));
        var wakeResult = CreateOracle(new Evaluator(0)).Verify(in wake);
        Assert.Equal(ServiceCycleReplayMismatchCode.WakePolicy, wakeResult.Mismatch.Mismatch.Code);

        var projection = Cycle(0, projectedState: 99);
        var projectionResult = CreateOracle(new Evaluator(0)).Verify(in projection);
        Assert.Equal(ServiceCycleReplayMismatchCode.SemanticEvent, projectionResult.Mismatch.Mismatch.Code);
        Assert.Equal(4, projectionResult.Mismatch.Mismatch.FieldCode);

        var overflow = CreateOracle(new Evaluator(4)).Verify(Cycle(3));
        Assert.False(overflow.Succeeded);
        Assert.False(overflow.Failure.IsValid);
        Assert.Equal(ServiceCycleReplayMismatchCode.ActionCount, overflow.Mismatch.Mismatch.Code);
        Assert.Equal(2, overflow.Mismatch.Mismatch.FieldCode);
    }

    [Fact]
    public void OracleContainsEvaluatorAndComparerFaults()
    {
        var evaluator = new Evaluator(1) { ThrowEvaluation = true };
        var evaluation = CreateOracle(evaluator).Verify(Cycle(1));
        Assert.Equal(ServiceCycleReplayFaultCode.EvaluatorFaulted, evaluation.Failure.Fault.Code);

        var comparer = new ValueComparer<ActionRecord> { Throw = true };
        var oracle = new ServiceCycleReplayEvaluatorOracle<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(
            Service,
            WakePolicy.Immediate,
            new Evaluator(1),
            new Hydrator(),
            new InputComparer(),
            new ValueComparer<StateRecord>(),
            comparer);
        var compared = oracle.Verify(Cycle(1));
        Assert.Equal(ServiceCycleReplayFaultCode.ComparerThrew, compared.Failure.Fault.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.Action, compared.Failure.Fault.Location.Record.Kind);
    }

    [Fact]
    public void VirtualClockRejectsRegressionAndCatalogRejectsRegistrationGaps()
    {
        var clock = new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10));
        clock.AdvanceTo(new MonotonicTimestamp(12));
        Assert.Equal(12, clock.Now.Ticks);
        Assert.Throws<InvalidOperationException>(() => clock.AdvanceTo(new MonotonicTimestamp(11)));

        var catalog = new ServiceCycleReplayExecutionCatalog(2);
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(2, new Factory());
        catalog.Register(registration);
        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void CatalogSnapshotsCompositionBeforeFirstExecution()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var catalog = new ServiceCycleReplayExecutionCatalog(2);
        catalog.Register(new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, new Factory()));

        var result = catalog.VerifyEvaluators(captured.Artifact);

        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.True(catalog.IsSealed);
        Assert.Throws<InvalidOperationException>(() => catalog.Register(
            new ServiceCycleReplayExecutionRegistration<
                Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(2, new Factory())));
        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void ValidatedCycleAndTypedPlanDoNotExposeMutableBackingArrays()
    {
        var actionBuffer = new[] { new ActionRecord(7) };
        var cycle = CycleWithActions(actionBuffer);
        actionBuffer[0] = new ActionRecord(99);

        Assert.Equal(7, cycle.GetAction(0).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => cycle.GetAction(1));
        Assert.DoesNotContain(
            typeof(ServiceCycleReplayDecodedCycle<InputRecord, StateRecord, ActionRecord>).GetProperties(),
            property => property.PropertyType.IsArray);
        Assert.DoesNotContain(
            typeof(ServiceCycleReplayTypedArtifactResult<InputRecord, StateRecord, ActionRecord>).GetProperties(),
            property => property.PropertyType.IsArray);
    }

    [Fact]
    public void ProductionReplayRegeneratesExactSemanticAndDetachedEvidence()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3);
        var factory = new Factory(3);

        var result = RunProduction(captured, factory);

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.Equal(3, captured.NativeCallCount);
        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(1, result.CompletedCycles);
    }

    [Fact]
    public void ProductionReplayConsumesArtifactWrittenByBackgroundExporter()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            3,
            backgroundExport: true);

        var result = RunProduction(captured, new Factory(3));

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(1, result.CompletedCycles);
    }

    [Fact]
    public void ProductionReplayDrainsLargeSuccessfulBatchThroughRealPump()
    {
        const int actionCount = 1_000;
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(actionCount);

        var result = RunProduction(captured, new Factory(actionCount));

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.Equal(actionCount, captured.NativeCallCount);
        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(1, result.CompletedCycles);
    }

    [Theory]
    [InlineData((int)ProductionReplayScenario.FirstNativeRejected, 1)]
    [InlineData((int)ProductionReplayScenario.MiddleNativeRejected, 2)]
    public void ProductionReplayRegeneratesFirstAndMiddleNativeRejection(
        int scenarioValue,
        int expectedNativeCalls)
    {
        var scenario = (ProductionReplayScenario)scenarioValue;
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            3,
            scenario,
            varyingClock: true);

        var result = RunProduction(captured, new Factory(3));

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.Equal(expectedNativeCalls, captured.NativeCallCount);
        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(1, result.CompletedCycles);
    }

    [Fact]
    public void ProductionReplayRegeneratesActionFaultTerminal()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            3,
            ProductionReplayScenario.ActionFaulted,
            varyingClock: true);

        var result = RunProduction(captured, new Factory(3));

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.Equal(1, captured.NativeCallCount);
        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(1, result.CompletedCycles);
    }

    [Fact]
    public void EvaluationFaultArtifactIsTypedPreflightRejection()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            3,
            ProductionReplayScenario.EvaluationFaulted);
        var factory = new Factory(3);

        var result = RunProduction(captured, factory);

        Assert.False(captured.Artifact.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.EvaluationAborted, captured.Artifact.Eligibility);
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.ExecutionFaulted, result.Failure.Fault.Code);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete,
            result.Failure.Fault.DetailCode);
        Assert.Equal(0, factory.CreationCount);
    }

    [Fact]
    public void ProductionReplayRegeneratesEveryArtifactTimingObservation()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3, varyingClock: true);

        var result = RunProduction(captured, new Factory(3));

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.True(result.Succeeded, ExecutionFailure(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionReplayRegeneratesBoundStrategyPublications(bool publishBeforeFirstPump)
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            0,
            varyingClock: true,
            bindStrategy: true,
            publishStrategyBeforeFirstPump: publishBeforeFirstPump);

        var result = RunProduction(captured, new Factory());

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.True(result.Succeeded, ExecutionFailure(result));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void ProductionReplayConsumesNonStartingAndFailedCaptureHistory(
        int notReady,
        int unavailable,
        int captureFault)
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            0,
            varyingClock: true,
            notReadyAttempts: notReady,
            unavailableAttempts: unavailable,
            captureFaultAttempts: captureFault);

        var result = RunProduction(captured, new Factory());

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.True(result.Succeeded, ExecutionFailure(result));
    }

    [Fact]
    public void ProductionReplayRegeneratesCaptureFaultRecovery()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            0,
            varyingClock: true,
            captureFaultAttempts: 1);

        var result = RunProduction(captured, new Factory());

        Assert.Contains(
            Enumerable.Range(0, captured.Artifact.SemanticTrace.Count)
                .Select(index => captured.Artifact.SemanticTrace[index]),
            item => item.Kind == ServiceCycleSemanticEventKind.CaptureFaulted);
        Assert.Contains(
            Enumerable.Range(0, captured.Artifact.SemanticTrace.Count)
                .Select(index => captured.Artifact.SemanticTrace[index]),
            item => item.Kind == ServiceCycleSemanticEventKind.FaultRecovered);
        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.True(result.Succeeded, ExecutionFailure(result));
    }

    [Fact]
    public void ProductionReplayConsumesPublicationOnlyInitialConfigurationGeneration()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            0,
            varyingClock: true,
            publicationOnlyInitialConfiguration: true);

        var result = RunProduction(captured, new Factory());

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.True(result.Succeeded, ExecutionFailure(result));
    }

    [Fact]
    public void ClockScriptRejectsExtraAndMissingOwnerReads()
    {
        var artifact = ServiceCycleReplayProductionScenarioFixture.Capture(0).Artifact;
        var extra = new ServiceCycleReplayClockScript(artifact.SemanticTrace, 1);
        extra.PrepareConstructor();
        _ = extra.Now;
        Assert.True(extra.IsComplete);
        Assert.Throws<InvalidOperationException>(() => _ = extra.Now);

        var missing = new ServiceCycleReplayClockScript(artifact.SemanticTrace, 1);
        missing.PrepareConstructor();
        Assert.False(missing.IsComplete);
    }

    [Fact]
    public void EmergencySuffixReplayUsesCommonRejectionWithoutNativeScriptCalls()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            3,
            ProductionReplayScenario.EmergencyRejected);
        var factory = new Factory(3);

        var result = RunProduction(captured, factory);

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.Equal(0, captured.NativeCallCount);
        Assert.True(result.Succeeded, ExecutionFailure(result));
    }

    [Fact]
    public void LifecycleSuffixReplayOrphansWithoutNativeScriptCalls()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            3,
            ProductionReplayScenario.LifecycleOrphaned);
        var factory = new Factory(3);

        var result = RunProduction(captured, factory);

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.Equal(0, captured.NativeCallCount);
        Assert.True(result.Succeeded, ExecutionFailure(result));
    }

    [Fact]
    public void ProductionReplayRejectsReentrantActionEmergencyBeforeApplyingControls()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(
            2,
            ProductionReplayScenario.ReentrantActionEmergency,
            varyingClock: true);

        var result = RunProduction(captured, new Factory(2));

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.ExecutionFaulted, result.Failure.Fault.Code);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.InPumpControlUnsupported,
            result.Failure.Fault.DetailCode);
        Assert.Equal(1, result.Failure.Cycle.TraceServiceKey);
    }

    [Fact]
    public void ControlBoundaryRejectsTimedActionEmergency()
    {
        var trace = ServiceCycleReplayControlBoundaryFixture.TimedActionEmergency();

        var failure = ServiceCycleReplayControlBoundaryValidator.Validate(trace, new[] { 1 });

        Assert.True(failure.IsValid);
        Assert.Equal(ServiceCycleSemanticEventKind.EmergencyEntered, failure.ControlKind);
        Assert.Equal(ServiceCycleSemanticEventKind.ActionAttempted, trace[failure.OwnerEventIndex].Kind);
        Assert.Equal(1, failure.TraceServiceKey);
    }

    [Fact]
    public void ControlBoundaryRejectsCaptureLifecycleAtConstantClockEquality()
    {
        var trace = ServiceCycleReplayControlBoundaryFixture.ConstantClockCaptureLifecycle();

        var failure = ServiceCycleReplayControlBoundaryValidator.Validate(trace, new[] { 1 });

        Assert.True(failure.IsValid);
        Assert.Equal(ServiceCycleSemanticEventKind.LifecycleRequested, failure.ControlKind);
        Assert.Equal(ServiceCycleSemanticEventKind.CaptureStarted, trace[failure.OwnerEventIndex].Kind);
        Assert.Equal(1, failure.TraceServiceKey);
    }

    [Fact]
    public void ControlBoundaryAllowsBetweenFrameLifecycleAtConstantClockEquality()
    {
        var trace = ServiceCycleReplayControlBoundaryFixture.ConstantClockBetweenFrameLifecycle();

        var failure = ServiceCycleReplayControlBoundaryValidator.Validate(trace, new[] { 1 });

        Assert.False(failure.IsValid);
    }

    [Fact]
    public void EvaluatorAndHydratorMutationsStopAtStableFirstDivergence()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3);

        var evaluator = RunProduction(captured, new Factory(2));
        var hydration = RunProduction(captured, new Factory(3, frameHydrationOffset: 1));

        Assert.False(evaluator.Succeeded);
        Assert.Equal(ServiceCycleReplayMismatchCode.ActionCount, evaluator.Mismatch.Mismatch.Code);
        Assert.False(hydration.Succeeded);
        Assert.Equal(ServiceCycleReplayMismatchCode.CycleInput, hydration.Mismatch.Mismatch.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.CycleInput, hydration.Mismatch.Mismatch.Record.Kind);
        Assert.Equal(1, hydration.Mismatch.Mismatch.FieldCode);
    }

    [Fact]
    public void ConfigurationPreviousStateAndStrategyMutationsReportTheirPreciseFirstFeatureMismatch()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3);

        var configuration = RunProduction(
            captured,
            new Factory(3, configurationHydrationOffset: 1));
        var previousState = RunProduction(
            captured,
            new Factory(3, previousStateHydrationOffset: 1));
        var strategy = RunProduction(
            captured,
            new Factory(3, strategyRecreationOffset: 1));

        Assert.False(configuration.Succeeded);
        Assert.Equal(ServiceCycleReplayMismatchCode.CycleInput, configuration.Mismatch.Mismatch.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.CycleInput, configuration.Mismatch.Mismatch.Record.Kind);
        Assert.Equal(2, configuration.Mismatch.Mismatch.FieldCode);
        Assert.False(previousState.Succeeded);
        Assert.Equal(ServiceCycleReplayMismatchCode.PreviousState, previousState.Mismatch.Mismatch.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.PreviousState, previousState.Mismatch.Mismatch.Record.Kind);
        Assert.Equal(1, previousState.Mismatch.Mismatch.FieldCode);
        Assert.False(strategy.Succeeded);
        Assert.Equal(ServiceCycleReplayMismatchCode.CycleInput, strategy.Mismatch.Mismatch.Code);
        Assert.Equal(ServiceCycleReplayRecordKind.CycleInput, strategy.Mismatch.Mismatch.Record.Kind);
        Assert.Equal(3, strategy.Mismatch.Mismatch.FieldCode);
    }

    [Fact]
    public void ProductionWorkerWakeMustMatchItsAuthoritativeFooter()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3);
        var factory = new Factory(
            3,
            productionWake: WakePolicy.AfterBatch(new MonotonicDuration(8)));

        var result = RunProduction(captured, factory);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceCycleReplayMismatchCode.WakePolicy, result.Mismatch.Mismatch.Code);
    }

    [Fact]
    public void ProductionSourceRejectsUncapturedConfigurationAndStrategyGenerations()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3);
        var factory = new Factory(3);
        var decoded = ServiceCycleReplayTypedPlanDecoder.Decode(
            captured.Artifact,
            1,
            factory.ServiceId,
            new InputCodec(),
            new StateCodec(),
            new ActionCodec());
        Assert.True(decoded.Succeeded);
        var productionPlan = new ServiceCycleReplayProductionArtifactPlan(captured.Artifact);
        var source = new ServiceCycleReplayProductionSource<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(
            factory,
            new Hydrator(),
            decoded,
            ServiceCycleReplayNativeOutcomeScript.FromArtifact(captured.Artifact, 1),
            productionPlan,
            1);

        Assert.Throws<InvalidOperationException>(() => source.ConfigurationFor(99));
        var strategy = new ServiceCycleReplayStrategyGenerationSource(0);
        Assert.Throws<InvalidOperationException>(() => source.PublishStrategy(99, strategy));
        Assert.Equal(0, source.SemanticIndexBuildOperationCount);
        Assert.Equal(decoded.CycleCount, source.CycleIndexBuildOperationCount);
        Assert.Equal(captured.Artifact.CodecCount, productionPlan.CodecVisitCount);
        Assert.Equal(captured.Artifact.CycleCount, productionPlan.CycleVisitCount);
        Assert.Equal(captured.Artifact.SemanticTrace.Count, productionPlan.SemanticVisitCount);
        for (var index = 0; index < 128; index++) _ = source.ConfigurationFor(1);
        Assert.Equal(0, source.SemanticIndexBuildOperationCount);
        Assert.Equal(decoded.CycleCount, source.CycleIndexBuildOperationCount);
    }

    [Fact]
    public void ExecutionRejectsCodecMaximumThatDiffersFromFrozenArtifactManifest()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(
            1,
            new Factory(inputMaximumEncodedBytes: 9));

        var result = registration.VerifyEvaluator(captured.Artifact);

        Assert.False(result.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.CodecDescriptorRejected,
            result.Failure.Fault.DetailCode);
        Assert.Equal(1, result.Failure.Cycle.TraceServiceKey);
    }

    [Fact]
    public void IncompleteArtifactGateDoesNotConstructFeatureComponents()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0, byteCapacity: 1);
        var factory = new Factory();
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var result = registration.VerifyEvaluator(captured.Artifact);

        Assert.False(captured.Artifact.IsComplete);
        Assert.False(result.Succeeded);
        Assert.Equal(0, factory.CreationCount);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete,
            result.Failure.Fault.DetailCode);
    }

    [Fact]
    public void StateFactoryContentionRequiresFreshRecordingEpochBeforeProductionReplay()
    {
        var contended = ServiceCycleReplayProductionScenarioFixture.CaptureStateFactoryContention();
        var factory = new Factory();
        var catalog = new ServiceCycleReplayExecutionCatalog(1);
        catalog.Register(new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory));

        var rejected = catalog.RunProduction(contended, TimeSpan.FromSeconds(2));

        Assert.False(contended.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete, contended.Eligibility);
        Assert.Equal(ServiceCycleReplayCompletenessCode.SemanticTraceIncomplete, contended.Completeness.Code);
        Assert.Equal(0, contended.CycleCount);
        Assert.Equal(0, contended.Recording.HighWater.RecordCount);
        Assert.Equal(0, contended.Recording.HighWater.FooterCount);
        Assert.Contains(
            Enumerable.Range(0, contended.SemanticTrace.Count).Select(index => contended.SemanticTrace[index]),
            item => item.Kind == ServiceCycleSemanticEventKind.EvaluationDeferred &&
                item.Payload.Code == CommonServiceDecisionCodes.TransientContention.Value);
        Assert.False(rejected.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete,
            rejected.Failure.Fault.DetailCode);
        Assert.Equal(0, factory.CreationCount);

        var fresh = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var replayed = catalog.RunProduction(fresh.Artifact, TimeSpan.FromSeconds(2));

        Assert.True(replayed.Succeeded, ExecutionFailure(replayed));
    }

    [Fact]
    public void ResponsePublicationRaceIsClosedAcrossFreshProductionRuns()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3);

        for (var index = 0; index < 16; index++)
        {
            var result = RunProduction(captured, new Factory(3));
            Assert.True(result.Succeeded, ExecutionFailure(result));
        }
    }

    [Fact]
    public void ProductionWorkerBoundaryTimeoutReturnsStableFailure()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var factory = new Factory(0, productionDelay: TimeSpan.FromMilliseconds(100));
        var timer = Stopwatch.StartNew();

        var result = RunProduction(captured, factory, TimeSpan.FromMilliseconds(5));
        timer.Stop();

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.ExecutionFaulted, result.Failure.Fault.Code);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.EvaluatorDidNotFinish,
            result.Failure.Fault.DetailCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CatalogRunsTwoTypedServicesThroughOneRegistryAndPump()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.CaptureTwoServices();
        var catalog = new ServiceCycleReplayExecutionCatalog(2);
        catalog.Register(new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(
            1,
            new Factory(0, serviceId: new ServiceId("test.replay-execution"))));
        catalog.Register(new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(
            2,
            new Factory(0, serviceId: new ServiceId("test.replay-execution.second"))));

        var result = catalog.RunProduction(captured.Artifact, TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded, ExecutionFailure(result));
        Assert.Equal(2, result.CompletedCycles);
    }

    [Fact]
    public void ProductionClockSerializesReferenceStateFactoriesBeforeLedgerAdmission()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.CaptureTwoServices();
        var plan = new ServiceCycleReplayProductionArtifactPlan(captured.Artifact);
        var clock = new ServiceCycleReplayClockScript(plan, TimeSpan.FromMilliseconds(20));
        var claims = new ServiceResourceClaimLedger(2);
        using var firstEntered = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        var firstWorker = new TestReplayWorker(new StateFactoryProbeEvaluator(
            new Evaluator(0),
            () =>
            {
                firstEntered.Set();
                if (!releaseFirst.Wait(TimeSpan.FromSeconds(2)))
                    throw new TimeoutException("The first state factory was not released.");
            }));
        var secondWorker = new TestReplayWorker(new Evaluator(0));
        var firstState = new ServiceCycleWorkerState<Frame, Config, State, Action>(
            firstWorker, claims, clock);
        var secondState = new ServiceCycleWorkerState<Frame, Config, State, Action>(
            secondWorker, claims, clock);
        var firstResult = default(ServiceCycleWorkerStateCreationResult);
        var secondResult = default(ServiceCycleWorkerStateCreationResult);
        Exception? firstFailure = null;
        Exception? secondFailure = null;
        var lifecycle = new LifecycleGeneration(1);
        var firstThread = new Thread(() =>
        {
            try { firstResult = firstState.TryCreate(lifecycle); }
            catch (Exception exception) { firstFailure = exception; }
        })
        {
            IsBackground = true,
        };
        var secondThread = new Thread(() =>
        {
            try { secondResult = secondState.TryCreate(lifecycle); }
            catch (Exception exception) { secondFailure = exception; }
        })
        {
            IsBackground = true,
        };

        try
        {
            firstThread.Start();
            Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(2)));
            secondThread.Start();
            Assert.True(secondThread.Join(TimeSpan.FromSeconds(2)));
            Assert.IsType<TimeoutException>(secondFailure);

            releaseFirst.Set();
            Assert.True(firstThread.Join(TimeSpan.FromSeconds(2)));
            Assert.Null(firstFailure);
            Assert.Equal(ServiceCycleWorkerStateCreationResult.Created, firstResult);
            secondFailure = null;
            secondResult = secondState.TryCreate(lifecycle);
            Assert.Equal(ServiceCycleWorkerStateCreationResult.Created, secondResult);
        }
        finally
        {
            releaseFirst.Set();
            if (firstThread.IsAlive) firstThread.Join(TimeSpan.FromSeconds(2));
            if (secondThread.IsAlive) secondThread.Join(TimeSpan.FromSeconds(2));
            if (!firstThread.IsAlive) firstState.ReleaseForShutdown();
            if (!secondThread.IsAlive) secondState.ReleaseForShutdown();
        }
    }

    [Fact]
    public void SparseArtifactRemainsDetachedVerifiableButProductionFailsBeforeFeatureCallbacks()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.CaptureSparseReplayService();
        var productionFactory = new Factory(serviceId: new ServiceId("test.replay-execution.second"));
        var catalog = new ServiceCycleReplayExecutionCatalog(2);
        catalog.Register(new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(
                2,
                productionFactory));

        var result = catalog.RunProduction(captured.Artifact, TimeSpan.FromSeconds(5));

        Assert.True(captured.Artifact.IsComplete, ArtifactFailure(captured));
        Assert.False(result.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.CodecDescriptorRejected,
            result.Failure.Fault.DetailCode);
        Assert.Equal(0, productionFactory.CreationCount);

        var detached = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(
            2,
            new Factory(serviceId: new ServiceId("test.replay-execution.second")))
            .VerifyEvaluator(captured.Artifact);

        Assert.True(detached.Succeeded, ExecutionFailure(detached));
    }

    private static ServiceCycleReplayExecutionResult RunProduction(
        ProductionReplayCapture captured,
        Factory factory,
        TimeSpan? timeout = null)
    {
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);
        return ServiceCycleReplayProductionDriver.Run(
            captured.Artifact,
            registration,
            factory,
            timeout ?? TimeSpan.FromSeconds(2));
    }

    private static string ArtifactFailure(ProductionReplayCapture captured)
    {
        var artifact = captured.Artifact;
        var join = artifact.CycleCount == 0 ? default : artifact.GetCycle(0).Join.Code;
        return $"eligibility={artifact.Eligibility}, completeness={artifact.Completeness.Code}, join={join}, " +
            $"cycles={artifact.CycleCount}, records={artifact.Recording.HighWater.RecordCount}, " +
            $"footers={artifact.Recording.HighWater.FooterCount}, events={artifact.SemanticTrace.Count}";
    }

    private static string ExecutionFailure(ServiceCycleReplayExecutionResult result) =>
        $"fault={result.Failure.Fault.Code}/{result.Failure.Fault.DetailCode}, " +
        $"mismatch={result.Mismatch.Mismatch.Code}/{result.Mismatch.Mismatch.FieldCode}/" +
        $"{result.Mismatch.Mismatch.ElementIndex}";

    private static ServiceCycleReplayEvaluatorOracle<
        Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord> CreateOracle(Evaluator evaluator) => new(
        Service,
        WakePolicy.Immediate,
        evaluator,
        new Hydrator(),
        new InputComparer(),
        new ValueComparer<StateRecord>(),
        new ValueComparer<ActionRecord>());

    private static ServiceCycleReplayDecodedCycle<InputRecord, StateRecord, ActionRecord> Cycle(
        int actionCount,
        int nextState = 6,
        WakePolicy wake = default,
        int projectedState = 6,
        int actionOverrideIndex = -1,
        int actionOverrideValue = 0)
    {
        var identity = new ServiceCycleIdentity(
            Service,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(1),
            new CycleId(1));
        var ordinary = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var context = new ServiceCycleReplayContext(1, in ordinary);
        var actions = new ActionRecord[actionCount];
        for (var index = 0; index < actionCount; index++)
            actions[index] = new ActionRecord(index == actionOverrideIndex ? actionOverrideValue : index);
        return new ServiceCycleReplayDecodedCycle<InputRecord, StateRecord, ActionRecord>(
            context,
            new InputRecord(70, 7, 1),
            new StateRecord(5),
            new StateRecord(nextState),
            actions,
            wake == WakePolicy.Default ? WakePolicy.Immediate : wake,
            Projection(projectedState),
            new StatePublicationId(1),
            new MonotonicTimestamp(12));
    }

    private static ServiceCycleReplayDecodedCycle<InputRecord, StateRecord, ActionRecord> CycleWithActions(
        ActionRecord[] actions)
    {
        var identity = new ServiceCycleIdentity(
            Service,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(1),
            new CycleId(1));
        var ordinary = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var context = new ServiceCycleReplayContext(1, in ordinary);
        return new ServiceCycleReplayDecodedCycle<InputRecord, StateRecord, ActionRecord>(
            context,
            new InputRecord(70, 7, 1),
            new StateRecord(5),
            new StateRecord(6),
            actions,
            WakePolicy.Immediate,
            Projection(6),
            new StatePublicationId(1),
            new MonotonicTimestamp(12));
    }

    private static ServiceStateProjectionSnapshot Projection(int value)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        builder.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(value));
        return buffer.CreateSnapshot();
    }

    private static readonly ServiceId Service = new("test.replay-execution");
}
