using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using Xunit;
using FormatCodecRole = OrbModding.Common.Runtime.ServiceCycle.Replay.Format.ServiceCycleReplayCodecRole;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayFatalExceptionTests
{
    [Theory]
    [InlineData(typeof(StackOverflowException))]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public void FrozenDescriptorDoesNotContainFatalExceptions(Type exceptionType)
    {
        var expected = Fatal(exceptionType);
        var codec = new FatalCodec(FatalCodecBoundary.Descriptor, expected);
        var artifact = DescriptorArtifact();

        var actual = Assert.Throws(exceptionType, () =>
            ServiceCycleReplayFrozenCodec<FatalRecord>.TryCreate(
                artifact,
                1,
                FormatCodecRole.CycleInput,
                codec,
                out _));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void FrozenDescriptorStillContainsOrdinaryExceptions()
    {
        var codec = new FatalCodec(
            FatalCodecBoundary.Descriptor,
            new InvalidOperationException("descriptor"));

        var created = ServiceCycleReplayFrozenCodec<FatalRecord>.TryCreate(
            DescriptorArtifact(),
            1,
            FormatCodecRole.CycleInput,
            codec,
            out var frozen);

        Assert.False(created);
        Assert.Null(frozen);
    }

    [Theory]
    [InlineData(typeof(StackOverflowException), (int)FatalCodecBoundary.Descriptor)]
    [InlineData(typeof(StackOverflowException), (int)FatalCodecBoundary.Decode)]
    [InlineData(typeof(StackOverflowException), (int)FatalCodecBoundary.Encode)]
    [InlineData(typeof(OutOfMemoryException), (int)FatalCodecBoundary.Descriptor)]
    [InlineData(typeof(OutOfMemoryException), (int)FatalCodecBoundary.Decode)]
    [InlineData(typeof(OutOfMemoryException), (int)FatalCodecBoundary.Encode)]
    [InlineData(typeof(AccessViolationException), (int)FatalCodecBoundary.Descriptor)]
    [InlineData(typeof(AccessViolationException), (int)FatalCodecBoundary.Decode)]
    [InlineData(typeof(AccessViolationException), (int)FatalCodecBoundary.Encode)]
    public void RecordDecoderDoesNotContainFatalExceptions(Type exceptionType, int boundaryValue)
    {
        var expected = Fatal(exceptionType);
        var codec = new FatalCodec((FatalCodecBoundary)boundaryValue, expected);
        var encoded = EncodedRecord();
        var identity = encoded.Identity;

        var actual = Assert.Throws(exceptionType, () =>
            ServiceCycleReplayRecordDecoder.Decode(in encoded, identity, codec));

        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData((int)FatalCodecBoundary.Descriptor)]
    [InlineData((int)FatalCodecBoundary.Decode)]
    [InlineData((int)FatalCodecBoundary.Encode)]
    public void RecordDecoderStillContainsOrdinaryExceptions(int boundaryValue)
    {
        var codec = new FatalCodec(
            (FatalCodecBoundary)boundaryValue,
            new InvalidOperationException("codec"));
        var encoded = EncodedRecord();
        var identity = encoded.Identity;

        var result = ServiceCycleReplayRecordDecoder.Decode(in encoded, identity, codec);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.DecodeRejected, result.Fault.Code);
    }

    [Theory]
    [InlineData(typeof(StackOverflowException))]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public void OracleDoesNotContainFatalExceptionsAtAnyInnerBoundary(Type exceptionType)
    {
        foreach (var boundary in OracleBoundaries)
        {
            var expectedException = Fatal(exceptionType);
            var oracle = Oracle(boundary, expectedException);
            var expectedCycle = Cycle();

            var actual = Assert.Throws(exceptionType, () => oracle.Verify(in expectedCycle));

            Assert.Same(expectedException, actual);
        }
    }

    [Theory]
    [InlineData((int)FatalOracleBoundary.Hydration, (int)ServiceCycleReplayFaultCode.CycleContextRejected)]
    [InlineData((int)FatalOracleBoundary.Recreation, (int)ServiceCycleReplayFaultCode.CycleContextRejected)]
    [InlineData((int)FatalOracleBoundary.PreviousStateRecord, (int)ServiceCycleReplayFaultCode.CycleContextRejected)]
    [InlineData((int)FatalOracleBoundary.Evaluation, (int)ServiceCycleReplayFaultCode.EvaluatorFaulted)]
    [InlineData((int)FatalOracleBoundary.Projection, (int)ServiceCycleReplayFaultCode.EvaluatorFaulted)]
    [InlineData((int)FatalOracleBoundary.NextStateRecord, (int)ServiceCycleReplayFaultCode.EvaluatorFaulted)]
    [InlineData((int)FatalOracleBoundary.Comparer, (int)ServiceCycleReplayFaultCode.ComparerThrew)]
    public void OracleStillContainsOrdinaryParticipantExceptions(int boundaryValue, int faultCodeValue)
    {
        var oracle = Oracle(
            (FatalOracleBoundary)boundaryValue,
            new InvalidOperationException("participant"));
        var expected = Cycle();

        var result = oracle.Verify(in expected);

        Assert.False(result.Succeeded);
        Assert.Equal((ServiceCycleReplayFaultCode)faultCodeValue, result.Failure.Fault.Code);
    }

    [Theory]
    [InlineData((int)FatalOracleBoundary.ReleaseState)]
    [InlineData((int)FatalOracleBoundary.ReleaseFrame)]
    public void OracleReturnsTypedFailureForOrdinaryReleaseExceptions(int boundaryValue)
    {
        var boundary = (FatalOracleBoundary)boundaryValue;
        var exception = new InvalidOperationException("release");
        var evaluator = new FatalEvaluator(boundary, exception);
        var oracle = Oracle(evaluator, boundary, exception);
        var expected = Cycle();

        var result = oracle.Verify(in expected);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.ExecutionFaulted, result.Failure.Fault.Code);
        Assert.Equal(ServiceCycleReplayFailureLocation.Execution, result.Failure.Fault.Location);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.DetachedCleanupRejected,
            result.Failure.Fault.DetailCode);
        Assert.Equal(1, evaluator.ReleaseStateCount);
        Assert.Equal(1, evaluator.ReleaseFrameCount);
    }

    [Fact]
    public void OracleCleanupFailureDoesNotOverrideEarlierMismatchOrFault()
    {
        var mismatchException = new InvalidOperationException("release after mismatch");
        var mismatchEvaluator = new FatalEvaluator(
            FatalOracleBoundary.ReleaseState,
            mismatchException);
        var mismatchOracle = Oracle(
            mismatchEvaluator,
            FatalOracleBoundary.ReleaseState,
            mismatchException);
        var mismatchedCycle = Cycle(nextState: 99);

        var mismatch = mismatchOracle.Verify(in mismatchedCycle);

        Assert.False(mismatch.Succeeded);
        Assert.Equal(ServiceCycleReplayMismatchCode.NextState, mismatch.Mismatch.Mismatch.Code);
        Assert.False(mismatch.Failure.IsValid);
        Assert.Equal(1, mismatchEvaluator.ReleaseStateCount);
        Assert.Equal(1, mismatchEvaluator.ReleaseFrameCount);

        var faultException = new InvalidOperationException("evaluation");
        var cleanupException = new InvalidOperationException("release after fault");
        var faultEvaluator = new FatalEvaluator(
            FatalOracleBoundary.Evaluation,
            faultException,
            FatalOracleBoundary.ReleaseFrame,
            cleanupException);
        var faultOracle = Oracle(
            faultEvaluator,
            FatalOracleBoundary.Evaluation,
            faultException);
        var expectedCycle = Cycle();

        var fault = faultOracle.Verify(in expectedCycle);

        Assert.False(fault.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.EvaluatorFaulted, fault.Failure.Fault.Code);
        Assert.False(fault.Mismatch.IsValid);
        Assert.Equal(1, faultEvaluator.ReleaseStateCount);
        Assert.Equal(1, faultEvaluator.ReleaseFrameCount);
    }

    [Fact]
    public void DetachedVerificationContainsOrdinaryFactoryExceptions()
    {
        var artifact = ServiceCycleReplayProductionScenarioFixture.Capture(0).Artifact;

        foreach (var boundary in FactoryBoundaries)
        {
            var direct = Registration(boundary, new InvalidOperationException("factory"));
            var directResult = direct.VerifyEvaluator(artifact);

            AssertDetachedPreparationFailure(directResult);

            var catalog = new ServiceCycleReplayExecutionCatalog(1);
            catalog.Register(Registration(boundary, new InvalidOperationException("catalog factory")));
            var catalogResult = catalog.VerifyEvaluators(artifact);

            AssertDetachedPreparationFailure(catalogResult);
        }
    }

    [Theory]
    [InlineData(typeof(StackOverflowException))]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    public void DetachedVerificationDoesNotContainFatalFactoryExceptions(Type exceptionType)
    {
        var artifact = ServiceCycleReplayProductionScenarioFixture.Capture(0).Artifact;

        foreach (var boundary in FactoryBoundaries)
        {
            var directExpected = Fatal(exceptionType);
            var direct = Registration(boundary, directExpected);

            var directActual = Assert.Throws(exceptionType, () => direct.VerifyEvaluator(artifact));

            Assert.Same(directExpected, directActual);

            var catalogExpected = Fatal(exceptionType);
            var catalog = new ServiceCycleReplayExecutionCatalog(1);
            catalog.Register(Registration(boundary, catalogExpected));

            var catalogActual = Assert.Throws(exceptionType, () => catalog.VerifyEvaluators(artifact));

            Assert.Same(catalogExpected, catalogActual);
        }
    }

    private static ServiceCycleReplayArtifactDocument DescriptorArtifact()
    {
        var codecs = new[]
        {
            new ServiceCycleReplayCodecManifestEntry(
                1,
                FormatCodecRole.CycleInput,
                new ServiceCycleReplayCodecDescriptor(1, 4)),
        };
        return new ServiceCycleReplayArtifactDocument(
            null!,
            default,
            null!,
            default,
            codecs,
            ServiceCycleReplayCodecIndex.Build(codecs),
            Array.Empty<ServiceCycleReplayArtifactCycle>(),
            ServiceCycleReplayArtifactEligibilityCode.Complete);
    }

    private static ServiceCycleReplayEncodedRecord EncodedRecord()
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 7);
        return new ServiceCycleReplayEncodedRecord(
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0),
            1,
            payload);
    }

    private static ServiceCycleReplayEvaluatorOracle<
        FatalFrame,
        FatalConfig,
        FatalState,
        FatalAction,
        FatalRecord,
        FatalRecord,
        FatalRecord> Oracle(FatalOracleBoundary boundary, Exception exception) =>
        Oracle(new FatalEvaluator(boundary, exception), boundary, exception);

    private static ServiceCycleReplayEvaluatorOracle<
        FatalFrame,
        FatalConfig,
        FatalState,
        FatalAction,
        FatalRecord,
        FatalRecord,
        FatalRecord> Oracle(
            FatalEvaluator evaluator,
            FatalOracleBoundary boundary,
            Exception exception) =>
        new(
            Service,
            WakePolicy.Immediate,
            evaluator,
            new FatalHydrator(boundary, exception),
            new FatalComparer(boundary, exception),
            new FatalComparer(FatalOracleBoundary.None, exception),
            new FatalComparer(FatalOracleBoundary.None, exception));

    private static ServiceCycleReplayDecodedCycle<FatalRecord, FatalRecord, FatalRecord> Cycle(
        int nextState = 6)
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
        return new ServiceCycleReplayDecodedCycle<FatalRecord, FatalRecord, FatalRecord>(
            context,
            new FatalRecord(7),
            new FatalRecord(5),
            new FatalRecord(nextState),
            Array.Empty<FatalRecord>(),
            WakePolicy.Immediate,
            Projection(6),
            new StatePublicationId(1),
            new MonotonicTimestamp(12));
    }

    private static ServiceCycleReplayExecutionRegistration<
        Frame,
        Config,
        State,
        Action,
        InputRecord,
        StateRecord,
        ActionRecord> Registration(FactoryBoundary boundary, Exception exception) =>
        new(1, new ThrowingExecutionFactory(boundary, exception));

    private static void AssertDetachedPreparationFailure(ServiceCycleReplayExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceCycleReplayFaultCode.ExecutionFaulted, result.Failure.Fault.Code);
        Assert.Equal(ServiceCycleReplayFailureLocation.Execution, result.Failure.Fault.Location);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.DetachedPreparationRejected,
            result.Failure.Fault.DetailCode);
    }

    private static ServiceStateProjectionSnapshot Projection(int value)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        builder.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(value));
        return buffer.CreateSnapshot();
    }

    private static Exception Fatal(Type exceptionType) =>
        exceptionType == typeof(StackOverflowException)
            ? new StackOverflowException("synthetic stack overflow")
            : exceptionType == typeof(OutOfMemoryException)
                ? new OutOfMemoryException("synthetic allocation failure")
                : exceptionType == typeof(AccessViolationException)
                    ? new AccessViolationException("synthetic access violation")
                    : throw new ArgumentOutOfRangeException(nameof(exceptionType));

    private static readonly FatalOracleBoundary[] OracleBoundaries =
    {
        FatalOracleBoundary.Hydration,
        FatalOracleBoundary.Recreation,
        FatalOracleBoundary.PreviousStateRecord,
        FatalOracleBoundary.Evaluation,
        FatalOracleBoundary.Projection,
        FatalOracleBoundary.NextStateRecord,
        FatalOracleBoundary.Comparer,
        FatalOracleBoundary.ReleaseState,
        FatalOracleBoundary.ReleaseFrame,
    };

    private static readonly FactoryBoundary[] FactoryBoundaries =
    {
        FactoryBoundary.CycleInputCodec,
        FactoryBoundary.StateCodec,
        FactoryBoundary.ActionCodec,
        FactoryBoundary.CycleInputComparer,
        FactoryBoundary.StateComparer,
        FactoryBoundary.ActionComparer,
        FactoryBoundary.Hydrator,
        FactoryBoundary.Evaluator,
    };

    private static readonly ServiceId Service = new("test.replay-fatal-boundary");

    private enum FatalCodecBoundary
    {
        None,
        Descriptor,
        Decode,
        Encode,
    }

    private enum FatalOracleBoundary
    {
        None,
        Hydration,
        Recreation,
        PreviousStateRecord,
        Evaluation,
        Projection,
        NextStateRecord,
        Comparer,
        ReleaseState,
        ReleaseFrame,
    }

    private enum FactoryBoundary
    {
        CycleInputCodec,
        StateCodec,
        ActionCodec,
        CycleInputComparer,
        StateComparer,
        ActionComparer,
        Hydrator,
        Evaluator,
    }

    private sealed class FatalCodec : IServiceCycleReplayCodec<FatalRecord>
    {
        private readonly FatalCodecBoundary _boundary;
        private readonly Exception _exception;

        internal FatalCodec(FatalCodecBoundary boundary, Exception exception)
        {
            _boundary = boundary;
            _exception = exception;
        }

        public ServiceCycleReplayCodecDescriptor Descriptor
        {
            get
            {
                ThrowAt(FatalCodecBoundary.Descriptor);
                return new ServiceCycleReplayCodecDescriptor(1, 4);
            }
        }

        public int Encode(in FatalRecord record, Span<byte> destination)
        {
            ThrowAt(FatalCodecBoundary.Encode);
            BinaryPrimitives.WriteInt32LittleEndian(destination, record.Value);
            return 4;
        }

        public FatalRecord Decode(ReadOnlySpan<byte> source)
        {
            ThrowAt(FatalCodecBoundary.Decode);
            return new FatalRecord(BinaryPrimitives.ReadInt32LittleEndian(source));
        }

        private void ThrowAt(FatalCodecBoundary boundary)
        {
            if (_boundary == boundary) throw _exception;
        }
    }

    private sealed class FatalHydrator : IServiceCycleReplayHydrator<
        FatalFrame,
        FatalConfig,
        FatalState,
        FatalRecord,
        FatalRecord>
    {
        private readonly FatalOracleBoundary _boundary;
        private readonly Exception _exception;

        internal FatalHydrator(FatalOracleBoundary boundary, Exception exception)
        {
            _boundary = boundary;
            _exception = exception;
        }

        public void HydrateFrame(
            in FatalRecord input,
            in ServiceCycleReplayContext context,
            ref FatalFrame frame)
        {
            ThrowAt(FatalOracleBoundary.Hydration);
            frame ??= new FatalFrame();
            frame.Value = input.Value;
        }

        public FatalConfig HydrateConfiguration(
            in FatalRecord input,
            in ServiceCycleReplayContext context) => new(input.Value);

        public FatalState HydratePreviousState(
            in FatalRecord previousState,
            in ServiceCycleReplayContext context) => new() { Value = previousState.Value };

        public FatalRecord RecreateCycleInputRecord(
            in FatalFrame frame,
            in FatalConfig config,
            in ServiceCycleReplayContext context)
        {
            ThrowAt(FatalOracleBoundary.Recreation);
            return new FatalRecord(frame.Value);
        }

        private void ThrowAt(FatalOracleBoundary boundary)
        {
            if (_boundary == boundary) throw _exception;
        }
    }

    private sealed class FatalEvaluator : IServiceCycleReplayEvaluatorPort<
        FatalFrame,
        FatalConfig,
        FatalState,
        FatalAction,
        FatalRecord,
        FatalRecord>
    {
        private readonly FatalOracleBoundary _boundary;
        private readonly Exception _exception;
        private readonly FatalOracleBoundary _secondaryBoundary;
        private readonly Exception? _secondaryException;
        private int _stateRecordCount;

        internal FatalEvaluator(
            FatalOracleBoundary boundary,
            Exception exception,
            FatalOracleBoundary secondaryBoundary = FatalOracleBoundary.None,
            Exception? secondaryException = null)
        {
            _boundary = boundary;
            _exception = exception;
            _secondaryBoundary = secondaryBoundary;
            _secondaryException = secondaryException;
        }

        internal int ReleaseStateCount { get; private set; }
        internal int ReleaseFrameCount { get; private set; }

        public FatalState CreateState(LifecycleGeneration lifecycle) => new();

        public void ReleaseState(ref FatalState state)
        {
            ReleaseStateCount++;
            ThrowAt(FatalOracleBoundary.ReleaseState);
            state = null!;
        }

        public void ReleaseFrame(ref FatalFrame frame)
        {
            ReleaseFrameCount++;
            ThrowAt(FatalOracleBoundary.ReleaseFrame);
            frame = null!;
        }

        public FatalRecord CreateStateRecord(in FatalState state)
        {
            _stateRecordCount++;
            if (_stateRecordCount == 1) ThrowAt(FatalOracleBoundary.PreviousStateRecord);
            if (_stateRecordCount == 2) ThrowAt(FatalOracleBoundary.NextStateRecord);
            return new FatalRecord(state.Value);
        }

        public WakePolicy Evaluate(
            in FatalFrame frame,
            in FatalConfig config,
            in ServiceCycleContext context,
            ref FatalState state,
            ServiceCycleReplayActionWriter<FatalAction, FatalRecord> actions)
        {
            ThrowAt(FatalOracleBoundary.Evaluation);
            state.Value++;
            return WakePolicy.Immediate;
        }

        public void ProjectState(
            in FatalState state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output)
        {
            ThrowAt(FatalOracleBoundary.Projection);
            output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(state.Value));
        }

        private void ThrowAt(FatalOracleBoundary boundary)
        {
            if (_boundary == boundary) throw _exception;
            if (_secondaryBoundary == boundary) throw _secondaryException!;
        }
    }

    private sealed class ThrowingExecutionFactory : IServiceCycleReplayExecutionFactory<
        Frame,
        Config,
        State,
        Action,
        InputRecord,
        StateRecord,
        ActionRecord>
    {
        private readonly Factory _inner = new();
        private readonly FactoryBoundary _boundary;
        private readonly Exception _exception;

        internal ThrowingExecutionFactory(FactoryBoundary boundary, Exception exception)
        {
            _boundary = boundary;
            _exception = exception;
        }

        public ServiceId ServiceId => _inner.ServiceId;
        public WakePolicy DefaultWakePolicy => _inner.DefaultWakePolicy;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _inner.FaultRecoveryPolicy;
        public Frame CreateFrame() => _inner.CreateFrame();

        public IServiceCycleReplayCodec<InputRecord> CreateCycleInputCodec()
        {
            ThrowAt(FactoryBoundary.CycleInputCodec);
            return _inner.CreateCycleInputCodec();
        }

        public IServiceCycleReplayCodec<StateRecord> CreateStateCodec()
        {
            ThrowAt(FactoryBoundary.StateCodec);
            return _inner.CreateStateCodec();
        }

        public IServiceCycleReplayCodec<ActionRecord> CreateActionCodec()
        {
            ThrowAt(FactoryBoundary.ActionCodec);
            return _inner.CreateActionCodec();
        }

        public IServiceCycleReplayComparer<InputRecord> CreateCycleInputComparer()
        {
            ThrowAt(FactoryBoundary.CycleInputComparer);
            return _inner.CreateCycleInputComparer();
        }

        public IServiceCycleReplayComparer<StateRecord> CreateStateComparer()
        {
            ThrowAt(FactoryBoundary.StateComparer);
            return _inner.CreateStateComparer();
        }

        public IServiceCycleReplayComparer<ActionRecord> CreateActionComparer()
        {
            ThrowAt(FactoryBoundary.ActionComparer);
            return _inner.CreateActionComparer();
        }

        public IServiceCycleReplayHydrator<Frame, Config, State, InputRecord, StateRecord> CreateHydrator()
        {
            ThrowAt(FactoryBoundary.Hydrator);
            return _inner.CreateHydrator();
        }

        public IServiceCycleReplayEvaluatorPort<Frame, Config, State, Action, StateRecord, ActionRecord>
            CreateEvaluatorPort()
        {
            ThrowAt(FactoryBoundary.Evaluator);
            return _inner.CreateEvaluatorPort();
        }

        public ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
            CreateProductionWorkerDefinition() => _inner.CreateProductionWorkerDefinition();

        private void ThrowAt(FactoryBoundary boundary)
        {
            if (_boundary == boundary) throw _exception;
        }
    }

    private sealed class FatalComparer : IServiceCycleReplayComparer<FatalRecord>
    {
        private readonly FatalOracleBoundary _boundary;
        private readonly Exception _exception;

        internal FatalComparer(FatalOracleBoundary boundary, Exception exception)
        {
            _boundary = boundary;
            _exception = exception;
        }

        public ServiceCycleReplayRecordComparison Compare(
            in FatalRecord expected,
            in FatalRecord actual)
        {
            if (_boundary == FatalOracleBoundary.Comparer) throw _exception;
            return expected.Value == actual.Value
                ? ServiceCycleReplayRecordComparison.Match
                : new ServiceCycleReplayRecordComparison(1);
        }
    }

    private sealed class FatalFrame
    {
        internal int Value;
    }

    private readonly struct FatalConfig
    {
        internal FatalConfig(int value) => Value = value;
        internal int Value { get; }
    }

    private sealed class FatalState
    {
        internal int Value;
    }

    private readonly struct FatalAction
    {
        internal FatalAction(int value) => Value = value;
        internal int Value { get; }
    }

    private readonly struct FatalRecord : IServiceCycleReplayRecord
    {
        internal FatalRecord(int value) => Value = value;
        internal int Value { get; }
    }
}
