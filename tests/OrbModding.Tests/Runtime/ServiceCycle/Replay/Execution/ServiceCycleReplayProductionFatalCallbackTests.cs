using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServiceCycleReplayFatalCallbackCollection
{
    public const string Name = "ServiceCycle fatal callback isolation";
}

[Collection(ServiceCycleReplayFatalCallbackCollection.Name)]
public sealed class ServiceCycleReplayProductionFatalCallbackTests
{
    [Theory]
    [MemberData(nameof(OwnerFatalCases))]
    public void RealPumpOwnerCallbacksDoNotContainProductionFatalExceptions(
        ProductionFatalBoundary boundary,
        Type exceptionType)
    {
        var expected = Fatal(exceptionType);

        var actual = Assert.Throws(exceptionType, () => RunMarkedOwnerPump(boundary, expected));

        Assert.Same(expected, actual);
    }

    [Theory]
    [MemberData(nameof(WorkerFatalCases))]
    public void ProductionDriverRelaysWorkerAndCleanupFatalExceptions(
        ProductionFatalBoundary boundary,
        Type exceptionType)
    {
        var expected = Fatal(exceptionType);
        var factory = new ProductionFatalFactory(boundary, expected);
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var actual = Assert.Throws(exceptionType, () => ServiceCycleReplayProductionDriver.Run(
            Artifact.Value,
            registration,
            factory,
            TimeSpan.FromSeconds(2)));

        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData(ProductionFatalBoundary.Start)]
    [InlineData(ProductionFatalBoundary.Capture)]
    [InlineData(ProductionFatalBoundary.Action)]
    public void RealPumpOwnerCallbacksStillContainOrdinaryExceptions(ProductionFatalBoundary boundary)
    {
        var exception = Record.Exception(() =>
            RunMarkedOwnerPump(boundary, new InvalidOperationException("ordinary callback")));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(ProductionFatalBoundary.Capture)]
    [InlineData(ProductionFatalBoundary.StateFactory)]
    [InlineData(ProductionFatalBoundary.Evaluation)]
    [InlineData(ProductionFatalBoundary.Projection)]
    [InlineData(ProductionFatalBoundary.ReleaseState)]
    [InlineData(ProductionFatalBoundary.ReleaseFrame)]
    public void ProductionDriverStillContainsOrdinaryCallbackExceptions(
        ProductionFatalBoundary boundary)
    {
        var factory = new ProductionFatalFactory(
            boundary,
            new InvalidOperationException("ordinary callback"));
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var exception = Record.Exception(() => ServiceCycleReplayProductionDriver.Run(
            Artifact.Value,
            registration,
            factory,
            TimeSpan.FromSeconds(2)));

        Assert.Null(exception);
    }

    [Fact]
    public void LiveReplayRecordingRetainsOrdinaryEvaluatorOomContainment()
    {
        var traceSession = new ServiceCycleTraceSessionId(991);
        var clock = new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10));
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 65_536, 128, 8));
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        using var registration = registry.RegisterReplay(
            new OwnerDefinition(
                ProductionFatalBoundary.Evaluation,
                new OutOfMemoryException("live evaluator")),
            new Config(7),
            recording);
        registry.Seal();
        using var pump = new SuiteFramePump(
            registry,
            new ServiceCycleSemanticRecorder(traceSession, 128, 1));

        pump.PumpFrame(1);
        var exception = Record.Exception(() =>
            Assert.True(registration.WaitForResponseReady(TimeSpan.FromSeconds(2))));

        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(RecordProductionFatalCases))]
    public void ProductionDriverDoesNotContainFatalRecordProductionOrEncoding(
        ProductionFatalBoundary boundary,
        Type exceptionType)
    {
        var expected = Fatal(exceptionType);
        var factory = new ProductionFatalFactory(boundary, expected);
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var actual = Assert.Throws(exceptionType, () => ServiceCycleReplayProductionDriver.Run(
            ArtifactFor(boundary),
            registration,
            factory,
            TimeSpan.FromSeconds(2)));

        Assert.Same(expected, actual);
    }

    [Theory]
    [MemberData(nameof(LiveObservationalRecordCases))]
    public void LiveRecordingKeepsAllocationAndAccessRecordFailuresObservational(
        ProductionFatalBoundary boundary,
        Type exceptionType)
    {
        var traceSession = new ServiceCycleTraceSessionId(992);
        var clock = new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10));
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 65_536, 128, 8));
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        using var registration = registry.RegisterReplay(
            new OwnerDefinition(boundary, Fatal(exceptionType)),
            new Config(7),
            recording);
        registry.Seal();
        using var pump = new SuiteFramePump(
            registry,
            new ServiceCycleSemanticRecorder(traceSession, 128, 1));

        var exception = Record.Exception(() =>
        {
            pump.PumpFrame(1);
            // Signal-driven; five seconds is only the failure deadline under host scheduling pressure.
            Assert.True(registration.WaitForResponseReady(TimeSpan.FromSeconds(5)));
        });

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(ProductionFatalBoundary.ReplacementWorkerFactory)]
    [InlineData(ProductionFatalBoundary.ReplacementFrameFactory)]
    public void ReplacementConstructionFatalEscapesWhileOrdinaryFailureBacksOff(
        ProductionFatalBoundary boundary)
    {
        var fatal = new OutOfMemoryException("replacement construction fatal");
        var fatalDefinition = new MarkedOwnerDefinition(boundary, fatal);
        using (var registry = new ServiceCycleRegistry(
                   1,
                   new LifecycleGeneration(1),
                   new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(100))))
        using (var registration = registry.RegisterReplay(
                   fatalDefinition,
                   new Config(7),
                   Recording(993)))
        {
            registry.Seal();
            using var pump = new SuiteFramePump(registry);
            var actual = Assert.Throws<OutOfMemoryException>(() =>
                pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
            Assert.Same(fatal, actual);
        }

        var ordinaryDefinition = new MarkedOwnerDefinition(
            boundary,
            new InvalidOperationException("replacement construction ordinary"));
        var ordinaryClock = new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(100));
        using var ordinaryRegistry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            ordinaryClock);
        using var ordinaryRegistration = ordinaryRegistry.RegisterReplay(
            ordinaryDefinition,
            new Config(7),
            Recording(994));
        ordinaryRegistry.Seal();
        using var ordinaryPump = new SuiteFramePump(ordinaryRegistry);

        Assert.True(ordinaryPump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.True(ordinaryRegistration.Slot.LifecycleSnapshot.ConstructionFault.IsValid);
        Assert.True(ordinaryRegistration.Slot.LifecycleSnapshot.ConstructionRetryDue > ordinaryClock.Now);
    }

    [Fact]
    public void WorkerFatalWakesOfflineWaiterBeforeBlockedCleanupCompletes()
    {
        using var boundary = BlockingFatalBoundary.Create(
            new AccessViolationException("fatal before blocked cleanup"));
        var definition = new MarkedBlockingFatalDefinition(boundary.Key);
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10)));
        using var registration = registry.RegisterReplay(
            definition,
            new Config(7),
            Recording(995));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        pump.PumpFrame(1);
        Assert.True(boundary.ReleaseEntered.Wait(TimeSpan.FromSeconds(2)));
        var actual = Assert.Throws<AccessViolationException>(() =>
            registration.WaitForResponseReady(TimeSpan.Zero));

        Assert.Same(boundary.Exception, actual);
        Assert.False(boundary.ReleaseAllowed.IsSet);
    }

    [Fact]
    public void OfflineCleanupWaitsBothLifecyclePositionsAndThreadUnwind()
    {
        using var exit = new BlockingExitObserver();
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock, false, workerExitObserver: exit);
        var definition = new LifecycleServiceDefinition("replay.cleanup-all-positions");
        using var registration = registry.Register(
            definition,
            new LifecycleConfig(1),
            new LifecycleGeneration(1));
        var slot = registration.Slot;
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.True(exit.WaitForCount(1));
        registration.Dispose();
        Assert.True(exit.WaitForCount(2));
        Assert.False(slot.WaitForAllWorkersExited(TimeSpan.Zero));

        exit.Release.Set();
        Assert.True(slot.WaitForAllWorkersExited(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void OfflineCleanupRetainsFatalUntilBothLifecycleWorkersUnwind()
    {
        using var boundary = BlockingFatalBoundary.Create(
            new OutOfMemoryException("fatal retained through slot cleanup"));
        using var exit = new BlockingExitObserver();
        using var waitStarted = new ManualResetEventSlim(false);
        using var waitReturned = new ManualResetEventSlim(false);
        Exception? observed = null;
        var owner = new Thread(() =>
        {
            try
            {
                using var registry = new ServiceCycleRegistry(
                    1,
                    new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10)),
                    false,
                    workerExitObserver: exit);
                var registration = registry.RegisterReplay(
                    new BlockingReleaseFatalDefinition(boundary.Key),
                    new Config(7),
                    Recording(997),
                    new LifecycleGeneration(1));
                var slot = registration.Slot;
                registry.Seal();
                using var pump = new SuiteFramePump(registry);

                if (!pump.RequestLifecycleReplacement(new LifecycleGeneration(2)))
                    throw new InvalidOperationException("The replacement lifecycle was not accepted.");
                if (!boundary.ReleaseEntered.Wait(TimeSpan.FromSeconds(2)))
                    throw new TimeoutException("The fatal worker did not enter blocked cleanup.");
                registration.Dispose();
                if (!exit.WaitForCount(1))
                    throw new TimeoutException("The replacement worker did not prepare its exit.");

                waitStarted.Set();
                try { slot.WaitForAllWorkersExited(TimeSpan.FromSeconds(2)); }
                catch (Exception exception) { observed = exception; }
            }
            catch (Exception exception)
            {
                observed = exception;
            }
            finally
            {
                waitReturned.Set();
            }
        })
        {
            IsBackground = true,
            Name = "ServiceCycle fatal slot-cleanup test owner",
        };

        owner.Start();
        Assert.True(
            waitStarted.Wait(TimeSpan.FromSeconds(2)),
            observed?.ToString() ?? "The owner did not reach the slot cleanup wait.");
        Assert.False(waitReturned.Wait(TimeSpan.FromMilliseconds(100)));
        boundary.ReleaseAllowed.Set();
        Assert.True(exit.WaitForCount(2));
        Assert.False(waitReturned.IsSet);
        exit.Release.Set();
        Assert.True(waitReturned.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(owner.Join(TimeSpan.FromSeconds(2)));
        Assert.Same(boundary.Exception, observed);
    }

    [Fact]
    public void ReplacementPromotionObservesRetiringRunnerFatalRelease()
    {
        using var boundary = BlockingFatalBoundary.Create(
            new OutOfMemoryException("retiring release fatal"));
        var definition = new ReplacementReleaseFatalDefinition(boundary.Key);
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10)));
        using var registration = registry.RegisterReplay(
            definition,
            new Config(7),
            Recording(996));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.True(boundary.ReleaseEntered.Wait(TimeSpan.FromSeconds(2)));
        var actual = Assert.Throws<OutOfMemoryException>(() => pump.PumpFrame(1));

        Assert.Same(boundary.Exception, actual);
    }

    public static IEnumerable<object[]> OwnerFatalCases()
    {
        foreach (var boundary in new[]
                 {
                     ProductionFatalBoundary.Start,
                     ProductionFatalBoundary.Capture,
                     ProductionFatalBoundary.Action,
                 })
        foreach (var type in FatalTypes)
            yield return new object[] { boundary, type };
    }

    public static IEnumerable<object[]> WorkerFatalCases()
    {
        foreach (var boundary in new[]
                 {
                     ProductionFatalBoundary.Capture,
                     ProductionFatalBoundary.StateFactory,
                     ProductionFatalBoundary.Evaluation,
                     ProductionFatalBoundary.Projection,
                     ProductionFatalBoundary.ReleaseState,
                     ProductionFatalBoundary.ReleaseFrame,
                 })
        foreach (var type in FatalTypes)
            yield return new object[] { boundary, type };
    }

    public static IEnumerable<object[]> RecordProductionFatalCases()
    {
        foreach (var boundary in RecordProductionBoundaries)
        foreach (var type in FatalTypes)
            yield return new object[] { boundary, type };
    }

    public static IEnumerable<object[]> LiveObservationalRecordCases()
    {
        foreach (var boundary in RecordProductionBoundaries)
        foreach (var type in new[] { typeof(OutOfMemoryException), typeof(AccessViolationException) })
            yield return new object[] { boundary, type };
    }

    private static void RunMarkedOwnerPump(ProductionFatalBoundary boundary, Exception exception)
    {
        var traceSession = new ServiceCycleTraceSessionId(990);
        var clock = new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10));
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 65_536, 128, 8));
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        using var registration = registry.RegisterReplay(
            new MarkedOwnerDefinition(boundary, exception),
            new Config(7),
            recording);
        registry.Seal();
        using var pump = new SuiteFramePump(
            registry,
            new ServiceCycleSemanticRecorder(traceSession, 128, 1));
        var slot = registration.Slot;

        try
        {
            pump.PumpFrame(1);
            if (boundary != ProductionFatalBoundary.Action) return;
            Assert.True(registration.WaitForResponseReady(TimeSpan.FromSeconds(2)));
            pump.PumpFrame(2);
            pump.PumpFrame(3);
        }
        finally
        {
            pump.Dispose();
            if (!slot.WaitForAllWorkersExited(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("The fatal-callback fixture worker did not exit.");
        }
    }

    private static Exception Fatal(Type exceptionType) =>
        exceptionType == typeof(StackOverflowException)
            ? new StackOverflowException("synthetic stack overflow")
            : exceptionType == typeof(OutOfMemoryException)
                ? new OutOfMemoryException("synthetic allocation failure")
                : exceptionType == typeof(AccessViolationException)
                    ? new AccessViolationException("synthetic access violation")
                    : throw new ArgumentOutOfRangeException(nameof(exceptionType));

    private static ServiceCycleReplaySession Recording(ulong traceSession) => new(
        new ServiceCycleTraceSessionId(traceSession),
        new ServiceCycleReplaySessionOptions(true, 65_536, 128, 8));

    private static ServiceCycleReplayArtifactDocument ArtifactFor(ProductionFatalBoundary boundary) =>
        boundary == ProductionFatalBoundary.ActionEncode
            ? ArtifactWithAction.Value
            : Artifact.Value;

    private static readonly Type[] FatalTypes =
    {
        typeof(StackOverflowException),
        typeof(OutOfMemoryException),
        typeof(AccessViolationException),
    };

    private static readonly ProductionFatalBoundary[] RecordProductionBoundaries =
    {
        ProductionFatalBoundary.CycleInputRecord,
        ProductionFatalBoundary.PreviousStateRecord,
        ProductionFatalBoundary.NextStateRecord,
        ProductionFatalBoundary.CycleInputEncode,
        ProductionFatalBoundary.StateEncode,
        ProductionFatalBoundary.ActionEncode,
    };

    private static readonly Lazy<ServiceCycleReplayArtifactDocument> Artifact = new(() =>
        ServiceCycleReplayProductionScenarioFixture.Capture(0).Artifact);
    private static readonly Lazy<ServiceCycleReplayArtifactDocument> ArtifactWithAction = new(() =>
        ServiceCycleReplayProductionScenarioFixture.Capture(1).Artifact);
}

public enum ProductionFatalBoundary
{
    None = 0,
    Start = 1,
    Capture = 2,
    Action = 3,
    StateFactory = 4,
    Evaluation = 5,
    Projection = 6,
    ReleaseState = 7,
    ReleaseFrame = 8,
    CycleInputRecord = 9,
    PreviousStateRecord = 10,
    NextStateRecord = 11,
    CycleInputEncode = 12,
    StateEncode = 13,
    ActionEncode = 14,
    ReplacementWorkerFactory = 15,
    ReplacementFrameFactory = 16,
}

internal sealed class ProductionFatalFactory : IServiceCycleReplayExecutionFactory<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
{
    private readonly Factory _ordinary;
    private readonly ProductionFatalBoundary _boundary;
    private readonly Exception _exception;
    private readonly int _workerExceptionKey;
    private int _hydratorCount;

    internal ProductionFatalFactory(ProductionFatalBoundary boundary, Exception exception)
    {
        _ordinary = new Factory(
            boundary is ProductionFatalBoundary.Action or ProductionFatalBoundary.ActionEncode ? 1 : 0);
        _boundary = boundary;
        _exception = exception;
        _workerExceptionKey = ProductionFatalExceptionRegistry.Register(exception);
    }

    public ServiceId ServiceId => _ordinary.ServiceId;
    public WakePolicy DefaultWakePolicy => _ordinary.DefaultWakePolicy;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _ordinary.FaultRecoveryPolicy;
    public Frame CreateFrame() => _ordinary.CreateFrame();
    public IServiceCycleReplayCodec<InputRecord> CreateCycleInputCodec() =>
        _ordinary.CreateCycleInputCodec();
    public IServiceCycleReplayCodec<StateRecord> CreateStateCodec() =>
        _ordinary.CreateStateCodec();
    public IServiceCycleReplayCodec<ActionRecord> CreateActionCodec() =>
        _ordinary.CreateActionCodec();
    public IServiceCycleReplayComparer<InputRecord> CreateCycleInputComparer() =>
        _ordinary.CreateCycleInputComparer();
    public IServiceCycleReplayComparer<StateRecord> CreateStateComparer() =>
        _ordinary.CreateStateComparer();
    public IServiceCycleReplayComparer<ActionRecord> CreateActionComparer() =>
        _ordinary.CreateActionComparer();

    public IServiceCycleReplayHydrator<Frame, Config, State, InputRecord, StateRecord> CreateHydrator()
    {
        var inner = _ordinary.CreateHydrator();
        _hydratorCount++;
        return (_boundary is ProductionFatalBoundary.Capture or
            ProductionFatalBoundary.CycleInputRecord) && _hydratorCount == 2
            ? new ProductionFatalHydrator(inner, _boundary, _exception)
            : inner;
    }

    public IServiceCycleReplayEvaluatorPort<Frame, Config, State, Action, StateRecord, ActionRecord>
        CreateEvaluatorPort() => _ordinary.CreateEvaluatorPort();

    public ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
        CreateProductionWorkerDefinition() => new ProductionFatalWorker(
            _boundary,
            _workerExceptionKey,
            _boundary is ProductionFatalBoundary.Action or ProductionFatalBoundary.ActionEncode ? 1 : 0);
}

internal sealed class ProductionFatalHydrator :
    IServiceCycleReplayHydrator<Frame, Config, State, InputRecord, StateRecord>
{
    private readonly IServiceCycleReplayHydrator<Frame, Config, State, InputRecord, StateRecord> _inner;
    private readonly ProductionFatalBoundary _boundary;
    private readonly Exception _exception;

    internal ProductionFatalHydrator(
        IServiceCycleReplayHydrator<Frame, Config, State, InputRecord, StateRecord> inner,
        ProductionFatalBoundary boundary,
        Exception exception)
    {
        _inner = inner;
        _boundary = boundary;
        _exception = exception;
    }

    public void HydrateFrame(
        in InputRecord input,
        in ServiceCycleReplayContext context,
        ref Frame frame)
    {
        if (_boundary == ProductionFatalBoundary.Capture) throw _exception;
        _inner.HydrateFrame(in input, in context, ref frame);
    }

    public Config HydrateConfiguration(in InputRecord input, in ServiceCycleReplayContext context) =>
        _inner.HydrateConfiguration(in input, in context);

    public State HydratePreviousState(in StateRecord previousState, in ServiceCycleReplayContext context) =>
        _inner.HydratePreviousState(in previousState, in context);

    public InputRecord RecreateCycleInputRecord(
        in Frame frame,
        in Config config,
        in ServiceCycleReplayContext context)
    {
        if (_boundary == ProductionFatalBoundary.CycleInputRecord) throw _exception;
        return _inner.RecreateCycleInputRecord(in frame, in config, in context);
    }
}

internal sealed class ProductionFatalEvaluator :
    IServiceCycleReplayEvaluatorPort<Frame, Config, State, Action, StateRecord, ActionRecord>
{
    private readonly Evaluator _inner;
    private readonly ProductionFatalBoundary _boundary;
    private readonly int _exceptionKey;
    private int _stateRecordCount;

    internal ProductionFatalEvaluator(
        Evaluator inner,
        ProductionFatalBoundary boundary,
        int exceptionKey)
    {
        _inner = inner;
        _boundary = boundary;
        _exceptionKey = exceptionKey;
    }

    public State CreateState(LifecycleGeneration lifecycle)
    {
        ThrowAt(ProductionFatalBoundary.StateFactory);
        return _inner.CreateState(lifecycle);
    }

    public void ReleaseState(ref State state)
    {
        ThrowAt(ProductionFatalBoundary.ReleaseState);
        _inner.ReleaseState(ref state);
    }

    public void ReleaseFrame(ref Frame frame)
    {
        ThrowAt(ProductionFatalBoundary.ReleaseFrame);
        _inner.ReleaseFrame(ref frame);
    }

    public StateRecord CreateStateRecord(in State state)
    {
        var count = Interlocked.Increment(ref _stateRecordCount);
        if ((_boundary == ProductionFatalBoundary.PreviousStateRecord && count == 1) ||
            (_boundary == ProductionFatalBoundary.NextStateRecord && count == 2))
            throw ProductionFatalExceptionRegistry.Get(_exceptionKey);
        return _inner.CreateStateRecord(in state);
    }

    public WakePolicy Evaluate(
        in Frame frame,
        in Config config,
        in ServiceCycleContext context,
        ref State state,
        ServiceCycleReplayActionWriter<Action, ActionRecord> actions)
    {
        ThrowAt(ProductionFatalBoundary.Evaluation);
        return _inner.Evaluate(in frame, in config, in context, ref state, actions);
    }

    public void ProjectState(
        in State state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output)
    {
        ThrowAt(ProductionFatalBoundary.Projection);
        _inner.ProjectState(in state, in context, output);
    }

    private void ThrowAt(ProductionFatalBoundary boundary)
    {
        if (_boundary == boundary) throw ProductionFatalExceptionRegistry.Get(_exceptionKey);
    }
}

internal sealed class ProductionFatalWorker : ServiceCycleReplayWorker<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
{
    internal ProductionFatalWorker(
        ProductionFatalBoundary boundary,
        int exceptionKey,
        int actionCount)
        : base(
            new ProductionFatalEvaluator(new Evaluator(actionCount), boundary, exceptionKey),
            new ProductionFatalInputCodec(boundary, exceptionKey),
            new ProductionFatalStateCodec(boundary, exceptionKey),
            new ProductionFatalActionCodec(boundary, exceptionKey)) { }
}

internal sealed class ProductionFatalInputCodec : IServiceCycleReplayCodec<InputRecord>
{
    private readonly InputCodec _inner = new();
    private readonly ProductionFatalBoundary _boundary;
    private readonly int _exceptionKey;

    internal ProductionFatalInputCodec(ProductionFatalBoundary boundary, int exceptionKey)
    {
        _boundary = boundary;
        _exceptionKey = exceptionKey;
    }

    public ServiceCycleReplayCodecDescriptor Descriptor => _inner.Descriptor;
    public int Encode(in InputRecord record, Span<byte> destination)
    {
        if (_boundary == ProductionFatalBoundary.CycleInputEncode)
            throw ProductionFatalExceptionRegistry.Get(_exceptionKey);
        return _inner.Encode(in record, destination);
    }
    public InputRecord Decode(ReadOnlySpan<byte> source) => _inner.Decode(source);
}

internal sealed class ProductionFatalStateCodec : IServiceCycleReplayCodec<StateRecord>
{
    private readonly StateCodec _inner = new();
    private readonly ProductionFatalBoundary _boundary;
    private readonly int _exceptionKey;

    internal ProductionFatalStateCodec(ProductionFatalBoundary boundary, int exceptionKey)
    {
        _boundary = boundary;
        _exceptionKey = exceptionKey;
    }

    public ServiceCycleReplayCodecDescriptor Descriptor => _inner.Descriptor;
    public int Encode(in StateRecord record, Span<byte> destination)
    {
        if (_boundary == ProductionFatalBoundary.StateEncode)
            throw ProductionFatalExceptionRegistry.Get(_exceptionKey);
        return _inner.Encode(in record, destination);
    }
    public StateRecord Decode(ReadOnlySpan<byte> source) => _inner.Decode(source);
}

internal sealed class ProductionFatalActionCodec : IServiceCycleReplayCodec<ActionRecord>
{
    private readonly ActionCodec _inner = new();
    private readonly ProductionFatalBoundary _boundary;
    private readonly int _exceptionKey;

    internal ProductionFatalActionCodec(ProductionFatalBoundary boundary, int exceptionKey)
    {
        _boundary = boundary;
        _exceptionKey = exceptionKey;
    }

    public ServiceCycleReplayCodecDescriptor Descriptor => _inner.Descriptor;
    public int Encode(in ActionRecord record, Span<byte> destination)
    {
        if (_boundary == ProductionFatalBoundary.ActionEncode)
            throw ProductionFatalExceptionRegistry.Get(_exceptionKey);
        return _inner.Encode(in record, destination);
    }
    public ActionRecord Decode(ReadOnlySpan<byte> source) => _inner.Decode(source);
}

internal class OwnerDefinition : IServiceCycleReplayDefinition<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
{
    private readonly ProductionFatalBoundary _boundary;
    private readonly Exception _exception;
    private readonly int _workerExceptionKey;
    private bool _captured;
    private int _frameCreateCount;
    private int _workerCreateCount;

    internal OwnerDefinition(ProductionFatalBoundary boundary, Exception exception)
    {
        _boundary = boundary;
        _exception = exception;
        _workerExceptionKey = ProductionFatalExceptionRegistry.Register(exception);
    }

    public ServiceId ServiceId => new("test.production-replay-owner-fatal");
    public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(new MonotonicDuration(1), new MonotonicDuration(8));
    public Frame CreateFrame()
    {
        if (Interlocked.Increment(ref _frameCreateCount) > 1)
            ThrowAt(ProductionFatalBoundary.ReplacementFrameFactory);
        return new Frame();
    }
    public virtual ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
        CreateWorkerDefinition()
    {
        if (Interlocked.Increment(ref _workerCreateCount) > 1)
            ThrowAt(ProductionFatalBoundary.ReplacementWorkerFactory);
        return new ProductionFatalWorker(
            _boundary,
            _workerExceptionKey,
            _boundary is ProductionFatalBoundary.Action or ProductionFatalBoundary.ActionEncode ? 1 : 0);
    }

    public ServiceStartDecision ShouldStart(in Config config, in ServiceCycleStartContext context)
    {
        ThrowAt(ProductionFatalBoundary.Start);
        return _captured
            ? ServiceStartDecision.Wait(CommonServiceDecisionCodes.NotReady, WakePolicy.Immediate)
            : ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    }

    public ServiceCaptureResult Capture(
        ref Frame frame,
        in Config config,
        in ServiceCaptureContext context)
    {
        ThrowAt(ProductionFatalBoundary.Capture);
        frame.Value = 70;
        _captured = true;
        return ServiceCaptureResult.Captured(
            new StrategyGeneration(1),
            CommonServiceDecisionCodes.Captured);
    }

    public virtual InputRecord CreateCycleInputRecord(
        in Frame frame,
        in Config config,
        in ServiceCaptureContext context,
        in ServiceCaptureResult capture)
    {
        ThrowAt(ProductionFatalBoundary.CycleInputRecord);
        return new InputRecord(frame.Value, config.Value, capture.StrategyGeneration.Value);
    }

    public ServiceActionResult TryExecute(
        in Action action,
        in Config config,
        in ServiceActionContext context)
    {
        ThrowAt(ProductionFatalBoundary.Action);
        return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
    }

    protected void ThrowAt(ProductionFatalBoundary boundary)
    {
        if (_boundary == boundary) throw _exception;
    }
}

internal sealed class MarkedOwnerDefinition : OwnerDefinition, IServiceCycleFatalExceptionPolicy
{
    internal MarkedOwnerDefinition(ProductionFatalBoundary boundary, Exception exception)
        : base(boundary, exception) { }
}

internal static class ProductionFatalExceptionRegistry
{
    private static readonly ConcurrentDictionary<int, Exception> Exceptions = new();
    private static int _nextKey;

    internal static int Register(Exception exception)
    {
        var key = Interlocked.Increment(ref _nextKey);
        if (!Exceptions.TryAdd(key, exception))
            throw new InvalidOperationException("A fatal callback test exception key was reused.");
        return key;
    }

    internal static Exception Get(int key) => Exceptions.TryGetValue(key, out var exception)
        ? exception
        : throw new InvalidOperationException("The fatal callback test exception was not registered.");
}

internal sealed class BlockingFatalBoundary : IDisposable
{
    private static readonly ConcurrentDictionary<int, BlockingFatalBoundary> Boundaries = new();
    private static int _nextKey;

    private BlockingFatalBoundary(int key, Exception exception)
    {
        Key = key;
        Exception = exception;
    }

    internal int Key { get; }
    internal Exception Exception { get; }
    internal ManualResetEventSlim ReleaseEntered { get; } = new(false);
    internal ManualResetEventSlim ReleaseAllowed { get; } = new(false);

    internal static BlockingFatalBoundary Create(Exception exception)
    {
        var key = Interlocked.Increment(ref _nextKey);
        var boundary = new BlockingFatalBoundary(key, exception);
        if (!Boundaries.TryAdd(key, boundary))
            throw new InvalidOperationException("A blocking fatal boundary key was reused.");
        return boundary;
    }

    internal static BlockingFatalBoundary Get(int key) =>
        Boundaries.TryGetValue(key, out var boundary)
            ? boundary
            : throw new InvalidOperationException("The blocking fatal boundary was not registered.");

    public void Dispose()
    {
        ReleaseAllowed.Set();
        Boundaries.TryRemove(Key, out _);
        ReleaseEntered.Dispose();
        ReleaseAllowed.Dispose();
    }
}

internal sealed class MarkedBlockingFatalDefinition : OwnerDefinition, IServiceCycleFatalExceptionPolicy
{
    private readonly int _boundaryKey;

    internal MarkedBlockingFatalDefinition(int boundaryKey)
        : base(ProductionFatalBoundary.None, new InvalidOperationException("unused")) =>
        _boundaryKey = boundaryKey;

    public override ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
        CreateWorkerDefinition() => new MarkedBlockingFatalWorker(_boundaryKey);
}

internal sealed class MarkedBlockingFatalWorker : ServiceCycleReplayWorker<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>,
    IServiceCycleFatalExceptionPolicy
{
    internal MarkedBlockingFatalWorker(int boundaryKey)
        : base(
            new BlockingFatalEvaluator(boundaryKey),
            new InputCodec(),
            new StateCodec(),
            new ActionCodec()) { }
}

internal sealed class BlockingFatalEvaluator : IServiceCycleReplayEvaluatorPort<
    Frame, Config, State, Action, StateRecord, ActionRecord>
{
    private readonly int _boundaryKey;
    private readonly Evaluator _inner = new(0);

    internal BlockingFatalEvaluator(int boundaryKey) => _boundaryKey = boundaryKey;

    public State CreateState(LifecycleGeneration lifecycle) => _inner.CreateState(lifecycle);
    public void ReleaseState(ref State state)
    {
        var boundary = BlockingFatalBoundary.Get(_boundaryKey);
        boundary.ReleaseEntered.Set();
        boundary.ReleaseAllowed.Wait();
        _inner.ReleaseState(ref state);
    }
    public void ReleaseFrame(ref Frame frame) => _inner.ReleaseFrame(ref frame);
    public StateRecord CreateStateRecord(in State state) => _inner.CreateStateRecord(in state);
    public WakePolicy Evaluate(
        in Frame frame,
        in Config config,
        in ServiceCycleContext context,
        ref State state,
        ServiceCycleReplayActionWriter<Action, ActionRecord> actions) =>
        throw BlockingFatalBoundary.Get(_boundaryKey).Exception;
    public void ProjectState(
        in State state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        _inner.ProjectState(in state, in context, output);
}

internal sealed class ReplacementReleaseFatalDefinition : OwnerDefinition, IServiceCycleFatalExceptionPolicy
{
    private readonly int _boundaryKey;
    private int _workerCount;

    internal ReplacementReleaseFatalDefinition(int boundaryKey)
        : base(ProductionFatalBoundary.None, new InvalidOperationException("unused")) =>
        _boundaryKey = boundaryKey;

    public override ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
        CreateWorkerDefinition() => Interlocked.Increment(ref _workerCount) == 1
            ? new MarkedReleaseFatalWorker(_boundaryKey)
            : new TestReplayWorker();
}

internal sealed class BlockingReleaseFatalDefinition : OwnerDefinition, IServiceCycleFatalExceptionPolicy
{
    private readonly int _boundaryKey;
    private int _workerCount;

    internal BlockingReleaseFatalDefinition(int boundaryKey)
        : base(ProductionFatalBoundary.None, new InvalidOperationException("unused")) =>
        _boundaryKey = boundaryKey;

    public override ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
        CreateWorkerDefinition() => Interlocked.Increment(ref _workerCount) == 1
            ? new BlockingReleaseFatalWorker(_boundaryKey)
            : new TestReplayWorker();
}

internal sealed class BlockingReleaseFatalWorker : ServiceCycleReplayWorker<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>,
    IServiceCycleFatalExceptionPolicy
{
    private readonly int _boundaryKey;

    internal BlockingReleaseFatalWorker(int boundaryKey)
        : base(new Evaluator(0), new InputCodec(), new StateCodec(), new ActionCodec()) =>
        _boundaryKey = boundaryKey;

    protected override void ReleaseFrameCore(ref Frame frame)
    {
        var boundary = BlockingFatalBoundary.Get(_boundaryKey);
        boundary.ReleaseEntered.Set();
        boundary.ReleaseAllowed.Wait();
        throw boundary.Exception;
    }
}

internal sealed class MarkedReleaseFatalWorker : ServiceCycleReplayWorker<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>,
    IServiceCycleFatalExceptionPolicy
{
    private readonly int _boundaryKey;

    internal MarkedReleaseFatalWorker(int boundaryKey)
        : base(new Evaluator(0), new InputCodec(), new StateCodec(), new ActionCodec()) =>
        _boundaryKey = boundaryKey;

    protected override void ReleaseFrameCore(ref Frame frame)
    {
        var boundary = BlockingFatalBoundary.Get(_boundaryKey);
        boundary.ReleaseEntered.Set();
        throw boundary.Exception;
    }
}

internal sealed class BlockingExitObserver : IServiceCycleWorkerExitObserver, IDisposable
{
    private int _enteredCount;
    internal ManualResetEventSlim Release { get; } = new(false);

    public void OnWorkerExitPrepared()
    {
        Interlocked.Increment(ref _enteredCount);
        Release.Wait();
    }

    internal bool WaitForCount(int count) => SpinWait.SpinUntil(
        () => Volatile.Read(ref _enteredCount) >= count,
        TimeSpan.FromSeconds(2));

    public void Dispose()
    {
        Release.Set();
        Release.Dispose();
    }
}
