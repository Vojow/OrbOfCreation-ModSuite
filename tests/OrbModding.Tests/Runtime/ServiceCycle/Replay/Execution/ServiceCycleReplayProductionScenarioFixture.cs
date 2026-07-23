using System;
using System.IO;
using System.Reflection;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

internal enum ProductionReplayScenario
{
    Completed = 1,
    EmergencyRejected = 2,
    LifecycleOrphaned = 3,
    ReentrantActionEmergency = 4,
    FirstNativeRejected = 5,
    MiddleNativeRejected = 6,
    ActionFaulted = 7,
    EvaluationFaulted = 8,
}

internal readonly struct ProductionReplayCapture
{
    internal ProductionReplayCapture(ServiceCycleReplayArtifactDocument artifact, int nativeCallCount)
    {
        Artifact = artifact;
        NativeCallCount = nativeCallCount;
    }

    internal ServiceCycleReplayArtifactDocument Artifact { get; }
    internal int NativeCallCount { get; }
}

internal static class ServiceCycleReplayProductionScenarioFixture
{
    internal static ServiceCycleReplayArtifactDocument CaptureStateFactoryContention()
    {
        var traceSession = new ServiceCycleTraceSessionId(950);
        var clock = new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10));
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 65_536, 8_192, 16));
        var definition = new OriginalReplayDefinition(
            0,
            new ServiceId("test.replay-execution"));
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        using var registration = registry.RegisterReplay(definition, new Config(7), recording);
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        if (ledger.TryBeginFactory(ServiceResourceRole.Frame, out var blocker) !=
            ServiceResourceClaimResult.Claimed)
        {
            throw new InvalidOperationException("The contention fixture could not reserve the factory token.");
        }

        try
        {
            registry.Seal();
            var semantic = new ServiceCycleSemanticRecorder(traceSession, 512, 1);
            using var pump = new SuiteFramePump(registry, semantic);

            pump.PumpFrame(1);
            if (!registration.WaitForResponseReady(TimeSpan.FromSeconds(2)))
                throw new InvalidOperationException("The contended worker did not publish its deferral.");
            pump.PumpFrame(2);

            if (!recording.TryReadSnapshot(out var snapshot))
                throw new InvalidOperationException("The contended recording fence was unstable.");
            var events = new ServiceCycleSemanticEvent[semantic.Count];
            var drain = semantic.DrainSince(default, events);
            if (!drain.IsComplete || drain.HasMore || drain.Copied != events.Length)
                throw new InvalidOperationException("The contended semantic trace was not complete.");
            var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
            ServiceCycleTraceCodec.Encode(traceSession, default, events, semanticBytes);
            var encoded = ServiceCycleReplayArtifactCodec.Encode(
                semanticBytes,
                recording,
                in snapshot);
            return ServiceCycleReplayArtifactCodec.Decode(encoded);
        }
        finally
        {
            ledger.EndFactory(blocker);
        }
    }

    internal static ProductionReplayCapture CaptureTwoServices(bool varyingClock = false)
    {
        // Two fresh workers race their first state factories on the registry-wide shared resource
        // claim ledger; the loser defers its evaluation by design, and a deferred cycle publishes
        // no replay footer, which poisons the artifact. Retry the whole capture so each returned
        // artifact comes from one uncontended attempt; a genuine never-publish defect still fails
        // every attempt.
        string? detail = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (TryCaptureTwoServices(varyingClock, out var capture, out detail)) return capture;
        }
        throw new InvalidOperationException(detail);
    }

    private static bool TryCaptureTwoServices(
        bool varyingClock,
        out ProductionReplayCapture capture,
        out string? failureDetail)
    {
        capture = default;
        failureDetail = null;
        var traceSession = new ServiceCycleTraceSessionId(948);
        IMonotonicClock clock = varyingClock
            ? new IncrementingReplayClock(10)
            : new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10));
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 65_536, 8_192, 16, 2));
        var firstDefinition = new OriginalReplayDefinition(0, new ServiceId("test.replay-execution"));
        var secondDefinition = new OriginalReplayDefinition(0, new ServiceId("test.replay-execution.second"));
        using var registry = new ServiceCycleRegistry(2, new LifecycleGeneration(1), clock);
        using var first = registry.RegisterReplay(firstDefinition, new Config(7), recording);
        using var second = registry.RegisterReplay(secondDefinition, new Config(9), recording);
        registry.Seal();
        var semantic = new ServiceCycleSemanticRecorder(traceSession, 512, 2);
        using var pump = new SuiteFramePump(registry, semantic);

        pump.PumpFrame(1);
        var boundaryTimeout = TimeSpan.FromSeconds(5);
        if (!recording.WaitForFooterAfter(1, boundaryTimeout))
        {
            failureDetail = "The two-service capture did not publish both footers.";
            return false;
        }
        if (!first.WaitForResponseReady(boundaryTimeout))
        {
            failureDetail = "The first captured replay response was not published.";
            return false;
        }
        if (!second.WaitForResponseReady(boundaryTimeout))
        {
            failureDetail = "The second captured replay response was not published.";
            return false;
        }
        pump.PumpFrame(2);

        if (!recording.TryReadSnapshot(out var snapshot))
            throw new InvalidOperationException("The captured replay recording fence was unstable.");
        var events = new ServiceCycleSemanticEvent[semantic.Count];
        var drain = semantic.DrainSince(default, events);
        if (!drain.IsComplete || drain.HasMore || drain.Copied != events.Length)
            throw new InvalidOperationException("The captured semantic trace was not complete.");
        var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(traceSession, default, events, semanticBytes);
        var encoded = ServiceCycleReplayArtifactCodec.Encode(semanticBytes, recording, in snapshot);
        capture = new ProductionReplayCapture(ServiceCycleReplayArtifactCodec.Decode(encoded), 0);
        return true;
    }

    internal static ProductionReplayCapture CaptureSparseReplayService()
    {
        var traceSession = new ServiceCycleTraceSessionId(949);
        var clock = new IncrementingReplayClock(10);
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, 65_536, 8_192, 16, 2));
        var replayDefinition = new OriginalReplayDefinition(
            0,
            new ServiceId("test.replay-execution.second"));
        using var registry = new ServiceCycleRegistry(2, new LifecycleGeneration(1), clock);
        using var ordinary = registry.Register(
            new DormantOrdinaryDefinition(new ServiceId("test.ordinary")), new Config(3));
        using var replay = registry.RegisterReplay(replayDefinition, new Config(7), recording);
        registry.Seal();
        var semantic = new ServiceCycleSemanticRecorder(traceSession, 512, 2);
        using var pump = new SuiteFramePump(registry, semantic);

        var saved = ConfigurationSaveResult<Config>.Saved(new Config(4));
        ordinary.Configuration.CompleteSave(in saved);
        pump.PumpFrame(1);
        var timeout = TimeSpan.FromSeconds(5);
        if (!recording.WaitForFooterAfter(0, timeout) || !replay.WaitForResponseReady(timeout))
            throw new InvalidOperationException("The sparse replay capture did not reach its response boundary.");
        pump.PumpFrame(2);

        if (!recording.TryReadSnapshot(out var snapshot))
            throw new InvalidOperationException("The sparse replay recording fence was unstable.");
        var events = new ServiceCycleSemanticEvent[semantic.Count];
        var drain = semantic.DrainSince(default, events);
        if (!drain.IsComplete || drain.HasMore || drain.Copied != events.Length)
            throw new InvalidOperationException("The sparse semantic trace was not complete.");
        var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(traceSession, default, events, semanticBytes);
        var encoded = ServiceCycleReplayArtifactCodec.Encode(semanticBytes, recording, in snapshot);
        return new ProductionReplayCapture(ServiceCycleReplayArtifactCodec.Decode(encoded), 0);
    }

    internal static ProductionReplayCapture Capture(
        int actionCount,
        ProductionReplayScenario scenario = ProductionReplayScenario.Completed,
        int byteCapacity = 65_536,
        bool varyingClock = false,
        int notReadyAttempts = 0,
        int unavailableAttempts = 0,
        int captureFaultAttempts = 0,
        bool publicationOnlyInitialConfiguration = false,
        bool projectionFault = false,
        bool bindStrategy = false,
        bool publishStrategyBeforeFirstPump = false,
        bool backgroundExport = false)
    {
        var traceSession = new ServiceCycleTraceSessionId(947);
        IMonotonicClock clock = varyingClock
            ? new IncrementingReplayClock(10)
            : new ServiceCycleReplayVirtualClock(new MonotonicTimestamp(10));
        var recording = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(true, byteCapacity, 8_192, 16));
        var definition = new OriginalReplayDefinition(
            actionCount,
            new ServiceId("test.replay-execution"),
            notReadyAttempts,
            unavailableAttempts,
            captureFaultAttempts,
            projectionFault,
            scenario);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        using var registration = registry.RegisterReplay(definition, new Config(7), recording);
        using var strategy = bindStrategy
            ? new ServiceStrategyPublisher<ReplayStrategyBulletin>(new ReplayStrategyBulletin(1))
            : null;
        if (strategy is not null)
        {
            registration.BindStrategy(strategy);
            definition.StrategyGenerationProvider = () => strategy.ReadLatest().Generation;
        }
        registry.Seal();
        var semanticCapacity = Math.Max(512, checked(actionCount * 4 + 128));
        if (semanticCapacity > ServiceCycleReplayArtifactExporter.MaximumSupportedSemanticEventCapacity)
            throw new ArgumentOutOfRangeException(nameof(actionCount));
        var semantic = new ServiceCycleSemanticRecorder(traceSession, semanticCapacity, 1);
        using var pump = new SuiteFramePump(registry, semantic);
        if (publishStrategyBeforeFirstPump)
        {
            if (strategy is null)
                throw new InvalidOperationException("A strategy publication requires a bound strategy source.");
            strategy.Publish(new ReplayStrategyBulletin(2));
        }
        if (scenario == ProductionReplayScenario.ReentrantActionEmergency)
            definition.BeforeNativeExecution = () =>
                pump.SetEmergencyStop(true, EmergencyStopReason.UserRequested);

        if (publicationOnlyInitialConfiguration)
        {
            var saved = ConfigurationSaveResult<Config>.Saved(new Config(8));
            registration.Configuration.CompleteSave(in saved);
        }

        var boundaryTimeout = TimeSpan.FromSeconds(2);
        if (!registration.Runner.WaitForWorkerReady(boundaryTimeout))
            throw new InvalidOperationException("The captured replay worker did not reach its initial wait.");

        var frame = 1L;
        var prefixAttempts = checked(notReadyAttempts + unavailableAttempts + captureFaultAttempts);
        for (var index = 0; index <= prefixAttempts; index++) pump.PumpFrame(frame++);
        if (!recording.WaitForFooterAfter(0, boundaryTimeout))
            throw new InvalidOperationException("The captured replay worker did not publish its footer.");
        if (!registration.WaitForResponseReady(boundaryTimeout))
            throw new InvalidOperationException("The captured replay response was not published.");

        switch (scenario)
        {
            case ProductionReplayScenario.Completed:
                break;
            case ProductionReplayScenario.EmergencyRejected:
                pump.SetEmergencyStop(true, EmergencyStopReason.UserRequested);
                break;
            case ProductionReplayScenario.LifecycleOrphaned:
                // Acquire the completed evaluation first so lifecycle replacement owns and orphans
                // the published batch rather than discarding an unobserved response.
                pump.PumpFrame(frame++);
                pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
                break;
            case ProductionReplayScenario.ReentrantActionEmergency:
            case ProductionReplayScenario.FirstNativeRejected:
            case ProductionReplayScenario.MiddleNativeRejected:
            case ProductionReplayScenario.ActionFaulted:
            case ProductionReplayScenario.EvaluationFaulted:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        pump.PumpFrame(frame++);
        var actionPumpCount = scenario switch
        {
            ProductionReplayScenario.EmergencyRejected or
            ProductionReplayScenario.LifecycleOrphaned or
            ProductionReplayScenario.EvaluationFaulted => 0,
            ProductionReplayScenario.FirstNativeRejected or
            ProductionReplayScenario.ActionFaulted => Math.Min(1, actionCount),
            ProductionReplayScenario.MiddleNativeRejected => Math.Min(2, actionCount),
            _ => actionCount,
        };
        for (var index = 0; index < actionPumpCount; index++) pump.PumpFrame(frame++);

        if (!recording.TryReadSnapshot(out var snapshot))
            throw new InvalidOperationException("The captured replay recording fence was unstable.");
        var artifact = backgroundExport
            ? ExportAndDecode(semantic, recording)
            : EncodeAndDecode(traceSession, semantic, recording, in snapshot);
        return new ProductionReplayCapture(
            artifact,
            definition.NativeCallCount);
    }

    private static ServiceCycleReplayArtifactDocument ExportAndDecode(
        ServiceCycleSemanticRecorder semantic,
        ServiceCycleReplaySession recording)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "service-cycle-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var exporter = new ServiceCycleReplayArtifactExporter(
                new ServiceCycleSemanticTraceSource(semantic),
                recording,
                new FileTraceSegmentStorage(directory, "snapshot", ".oscr"),
                new ServiceCycleReplayExportOptions(true, 1));
            var timeout = TimeSpan.FromSeconds(2);
            if (!SpinWait.SpinUntil(
                    () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Running,
                    timeout))
                throw new InvalidOperationException("The replay exporter did not start.");
            if (exporter.RequestSnapshot() != ServiceCycleReplayExportRequestResult.Accepted)
                throw new InvalidOperationException("The replay exporter did not accept the completed capture.");
            if (!SpinWait.SpinUntil(() => exporter.Metrics().ExportedArtifacts == 1, timeout))
                throw new InvalidOperationException("The replay exporter did not commit the completed capture.");
            exporter.Stop();
            if (!SpinWait.SpinUntil(
                    () => exporter.Metrics().Status == ServiceCycleReplayExportStatus.Stopped,
                    timeout))
                throw new InvalidOperationException("The replay exporter did not stop after its committed capture.");
            var path = Path.Combine(directory, "snapshot-000000.oscr");
            return ServiceCycleReplayArtifactCodec.Decode(File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceCycleReplayArtifactDocument EncodeAndDecode(
        ServiceCycleTraceSessionId traceSession,
        ServiceCycleSemanticRecorder semantic,
        ServiceCycleReplaySession recording,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        var events = new ServiceCycleSemanticEvent[semantic.Count];
        var drain = semantic.DrainSince(default, events);
        if (!drain.IsComplete || drain.HasMore || drain.Copied != events.Length)
            throw new InvalidOperationException("The captured semantic trace was not complete.");
        var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(traceSession, default, events, semanticBytes);
        var encoded = ServiceCycleReplayArtifactCodec.Encode(
            semanticBytes,
            recording,
            in snapshot);
        return ServiceCycleReplayArtifactCodec.Decode(encoded);
    }

}

internal sealed class IncrementingReplayClock : IMonotonicClock
{
    private long _ticks;

    internal IncrementingReplayClock(long initialTicks) => _ticks = checked(initialTicks - 1);
    public MonotonicTimestamp Now => new(Interlocked.Increment(ref _ticks));
}

internal sealed class OriginalReplayDefinition : IServiceCycleReplayDefinition<
    Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
{
    private readonly int _actionCount;
    private readonly ServiceId _serviceId;
    private int _notReadyAttempts;
    private int _unavailableAttempts;
    private int _captureFaultAttempts;
    private bool _captured;
    private readonly bool _projectionFault;
    private readonly ProductionReplayScenario _scenario;

    internal OriginalReplayDefinition(
        int actionCount,
        ServiceId serviceId,
        int notReadyAttempts = 0,
        int unavailableAttempts = 0,
        int captureFaultAttempts = 0,
        bool projectionFault = false,
        ProductionReplayScenario scenario = ProductionReplayScenario.Completed)
    {
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        _actionCount = actionCount;
        _serviceId = serviceId;
        _notReadyAttempts = notReadyAttempts;
        _unavailableAttempts = unavailableAttempts;
        _captureFaultAttempts = captureFaultAttempts;
        _projectionFault = projectionFault;
        _scenario = scenario;
    }

    internal int NativeCallCount { get; private set; }
    internal System.Action? BeforeNativeExecution { get; set; }
    internal Func<StrategyGeneration>? StrategyGenerationProvider { get; set; }
    public ServiceId ServiceId => _serviceId;
    public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(new MonotonicDuration(1), new MonotonicDuration(8));
    public Frame CreateFrame() => new();
    public ServiceCycleReplayWorker<Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>
        CreateWorkerDefinition()
    {
        var evaluator = new Evaluator(_actionCount, projectionFault: _projectionFault)
        {
            ThrowEvaluation = _scenario == ProductionReplayScenario.EvaluationFaulted,
        };
        return new TestReplayWorker(evaluator);
    }

    public ServiceStartDecision ShouldStart(in Config config, in ServiceCycleStartContext context)
    {
        if (_captured || _notReadyAttempts-- > 0)
            return ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(new MonotonicDuration(1)));
        return ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    }

    public ServiceCaptureResult Capture(
        ref Frame frame,
        in Config config,
        in ServiceCaptureContext context)
    {
        if (_captureFaultAttempts-- > 0) throw new InvalidOperationException("captured fault");
        if (_unavailableAttempts-- > 0)
            return ServiceCaptureResult.Unavailable(
                CommonServiceDecisionCodes.CaptureUnavailable,
                WakePolicy.AfterDecision(new MonotonicDuration(1)));
        frame.Value = 70;
        _captured = true;
        return ServiceCaptureResult.Captured(
            StrategyGenerationProvider?.Invoke() ?? new StrategyGeneration(1),
            CommonServiceDecisionCodes.Captured);
    }

    public InputRecord CreateCycleInputRecord(
        in Frame frame,
        in Config config,
        in ServiceCaptureContext context,
        in ServiceCaptureResult capture) => new(
            frame.Value,
            config.Value,
            capture.StrategyGeneration.Value);

    public ServiceActionResult TryExecute(
        in Action action,
        in Config config,
        in ServiceActionContext context)
    {
        BeforeNativeExecution?.Invoke();
        BeforeNativeExecution = null;
        NativeCallCount++;
        if ((_scenario == ProductionReplayScenario.FirstNativeRejected && context.ActionIndex == 0) ||
            (_scenario == ProductionReplayScenario.MiddleNativeRejected && context.ActionIndex == 1))
            return ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected);
        if (_scenario == ProductionReplayScenario.ActionFaulted && context.ActionIndex == 0)
        {
            var failedCall = new NativeMutationCallOutcome(1, 1, 0);
            var failedEvidence = ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.ExecutionThrew,
                failedCall);
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, failedEvidence);
        }
        var call = new NativeMutationCallOutcome(1, 1, 1);
        var evidence = ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call);
        return ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
    }

}

internal readonly struct ReplayStrategyBulletin
{
    internal ReplayStrategyBulletin(int value) => Value = value;
    internal int Value { get; }
}

internal sealed class DormantOrdinaryDefinition : IServiceCycleDefinition<Frame, Config, State, Action>
{
    private readonly ServiceId _serviceId;

    internal DormantOrdinaryDefinition(ServiceId serviceId) => _serviceId = serviceId;

    public ServiceId ServiceId => _serviceId;
    public WakePolicy DefaultWakePolicy => WakePolicy.AfterDecision(new MonotonicDuration(1));
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(new MonotonicDuration(1), new MonotonicDuration(8));
    public Frame CreateFrame() => new();
    public IServiceCycleWorkerDefinition<Frame, Config, State, Action> CreateWorkerDefinition() =>
        new TestReplayWorker();
    public ServiceStartDecision ShouldStart(in Config config, in ServiceCycleStartContext context) =>
        ServiceStartDecision.Wait(CommonServiceDecisionCodes.NotReady, DefaultWakePolicy);
    public ServiceCaptureResult Capture(ref Frame frame, in Config config, in ServiceCaptureContext context) =>
        ServiceCaptureResult.Unavailable(CommonServiceDecisionCodes.CaptureUnavailable, DefaultWakePolicy);
    public ServiceActionResult TryExecute(in Action action, in Config config, in ServiceActionContext context) =>
        ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
}
