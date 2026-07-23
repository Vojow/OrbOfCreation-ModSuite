using System;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal interface IServiceCycleReplayConfigurationSource<TConfig> where TConfig : notnull
{
    TConfig ConfigurationFor(ulong generation);
}

/// <summary>Feature-neutral production definition backed only by validated replay inputs and scripts.</summary>
internal sealed class ServiceCycleReplayProductionSource<
    TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> :
    IServiceCycleReplayDefinition<
        TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>,
    IServiceCycleReplayConfigurationSource<TConfig>,
    IServiceCycleFatalExceptionPolicy
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly IServiceCycleReplayExecutionFactory<
        TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> _factory;
    private readonly IServiceCycleReplayHydrator<
        TFrame, TConfig, TState, TCycleInputRecord, TStateRecord> _hydrator;
    private readonly ServiceCycleReplayTypedServicePlan<TCycleInputRecord, TStateRecord, TActionRecord> _plan;
    private readonly ServiceCycleReplayNativeOutcomeScript _native;
    private int _captureCursor;
    private int _startCursor;
    private int _strategyCursor;
    private int _lastCapturedCycleIndex = -1;
    private ServiceCycleReplayTypedServicePlan<
        TCycleInputRecord, TStateRecord, TActionRecord>.StartAttempt _pendingStart;
    private bool _hasPendingStart;
    private ulong _latestStrategy;

    internal ServiceCycleReplayProductionSource(
        IServiceCycleReplayExecutionFactory<
            TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord> factory,
        IServiceCycleReplayHydrator<TFrame, TConfig, TState, TCycleInputRecord, TStateRecord> hydrator,
        ServiceCycleReplayTypedArtifactResult<TCycleInputRecord, TStateRecord, TActionRecord> decoded,
        ServiceCycleReplayNativeOutcomeScript native,
        ServiceCycleReplayProductionArtifactPlan artifactPlan,
        int traceServiceKey)
    {
        _factory = factory;
        _hydrator = hydrator;
        _native = native;
        _capturesServiceKey = traceServiceKey;
        _plan = new ServiceCycleReplayTypedServicePlan<
            TCycleInputRecord, TStateRecord, TActionRecord>(artifactPlan, traceServiceKey, decoded);
    }

    public ServiceId ServiceId => _factory.ServiceId;
    public WakePolicy DefaultWakePolicy => _factory.DefaultWakePolicy;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _factory.FaultRecoveryPolicy;
    public TFrame CreateFrame() => _factory.CreateFrame();
    public ServiceCycleReplayWorker<
        TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>
        CreateWorkerDefinition()
    {
        var worker = Required(_factory.CreateProductionWorkerDefinition());
        ServiceCycleFatalExceptionPolicy.Register(worker);
        return worker;
    }

    public ServiceStartDecision ShouldStart(in TConfig config, in ServiceCycleStartContext context)
    {
        if (!_hasPendingStart || _startCursor >= _plan.StartCount)
            throw new InvalidOperationException("Production start exhausted its artifact evidence.");
        var attempt = _pendingStart;
        _pendingStart = default;
        _hasPendingStart = false;
        _startCursor++;
        if (context.Lifecycle.Value != attempt.Lifecycle ||
            context.LatestConfig.Value != attempt.Configuration)
            throw new InvalidOperationException("Production start diverged from artifact generations.");
        if (attempt.Kind == ServiceCycleSemanticEventKind.StartFaulted)
            throw new InvalidOperationException("The artifact requires a start callback fault.");
        if (attempt.Kind == ServiceCycleSemanticEventKind.StartDeferred)
            return ServiceStartDecision.Wait(
                DecisionCode(attempt.Code, CommonServiceDecisionCodes.NotReady),
                attempt.Wake);
        if (attempt.Kind != ServiceCycleSemanticEventKind.StartReady)
            throw new InvalidOperationException("Production start has an unsupported artifact terminal.");
        return ServiceStartDecision.Ready(
            DecisionCode(attempt.Code, CommonServiceDecisionCodes.Ready));
    }

    public ServiceCaptureResult Capture(
        ref TFrame frame,
        in TConfig config,
        in ServiceCaptureContext context)
    {
        if (_captureCursor >= _plan.CaptureCount)
            throw new InvalidOperationException("Production capture exhausted its artifact evidence.");
        var attempt = _plan.GetCapture(_captureCursor++);
        if (context.Lifecycle.Value != attempt.Lifecycle ||
            context.Config.Value != attempt.Configuration ||
            context.Capture.Value != attempt.Capture || context.Cycle.Value != attempt.Cycle)
            throw new InvalidOperationException("Production capture diverged from the replay cycle order.");
        if (attempt.Kind == ServiceCycleSemanticEventKind.CaptureFaulted)
            throw new InvalidOperationException("The artifact requires a capture fault.");
        if (attempt.Kind == ServiceCycleSemanticEventKind.CaptureUnavailable)
            return ServiceCaptureResult.Unavailable(
                DecisionCode(attempt.Code, CommonServiceDecisionCodes.CaptureUnavailable),
                attempt.Wake);
        if (!_plan.TryFindCycle(in attempt, out var cycleIndex))
            throw new InvalidOperationException("Captured semantic evidence has no detached replay cycle.");
        var expected = _plan.GetCycle(cycleIndex);
        var key = expected.Context.Cycle;
        if (_latestStrategy != key.Strategy)
        {
            // Capture-derived publications have no control step; the capture itself is their
            // observation seam, so consume the matching pending publication here.
            if (_strategyCursor < _plan.StrategyPublicationCount)
            {
                var publication = _plan.GetStrategyPublication(_strategyCursor);
                if (publication.IsCaptureDerived && publication.Generation == key.Strategy)
                {
                    _strategyCursor++;
                    _latestStrategy = key.Strategy;
                }
            }
            if (_latestStrategy != key.Strategy)
                throw new InvalidOperationException("Production strategy publication diverged from capture evidence.");
        }
        var expectedInput = expected.Input;
        var expectedContext = expected.Context;
        _hydrator.HydrateFrame(in expectedInput, in expectedContext, ref frame);
        _lastCapturedCycleIndex = cycleIndex;
        return ServiceCaptureResult.Captured(
            new StrategyGeneration(key.Strategy),
            DecisionCode(attempt.Code, CommonServiceDecisionCodes.Captured));
    }

    public TCycleInputRecord CreateCycleInputRecord(
        in TFrame frame,
        in TConfig config,
        in ServiceCaptureContext context,
        in ServiceCaptureResult capture)
    {
        if (_lastCapturedCycleIndex < 0)
            throw new InvalidOperationException("No captured replay cycle is available.");
        var expected = _plan.GetCycle(_lastCapturedCycleIndex);
        var expectedContext = expected.Context;
        return _hydrator.RecreateCycleInputRecord(in frame, in config, in expectedContext);
    }

    public ServiceActionResult TryExecute(
        in TAction action,
        in TConfig config,
        in ServiceActionContext context) => _native.Take(in context);

    internal bool NativeComplete => _native.IsComplete;
    internal bool CaptureEvidenceComplete =>
        _captureCursor == _plan.CaptureCount && _startCursor == _plan.StartCount &&
        _strategyCursor == _plan.StrategyPublicationCount && !_hasPendingStart;

    internal ulong InitialStrategyGeneration => _plan.InitialStrategyGeneration;
    internal int SemanticIndexBuildOperationCount => _plan.SemanticScanOperationCount;
    internal int CycleIndexBuildOperationCount => _plan.CycleIndexBuildOperationCount;

    internal TConfig ConfigurationForInitialPublication()
    {
        if (_plan.ConfigurationPublicationCount == 0 || _plan.GetConfigurationPublication(0) != 1)
            throw new InvalidOperationException("Replay has no initial configuration publication evidence.");
        return ConfigurationFor(_plan.GetConfigurationPublication(0));
    }

    internal static bool HasReplayableConfigurationPublications(
        ServiceCycleReplayArtifactDocument artifact,
        int serviceKey)
    {
        if (artifact is null) throw new ArgumentNullException(nameof(artifact));
        if (serviceKey <= 0) throw new ArgumentOutOfRangeException(nameof(serviceKey));
        ulong expected = 1;
        var found = false;
        for (var index = 0; index < artifact.SemanticTrace.Count; index++)
        {
            var item = artifact.SemanticTrace[index];
            if (item.Kind != ServiceCycleSemanticEventKind.ConfigurationPublished ||
                item.Payload.Service != (ulong)serviceKey) continue;
            if (item.Payload.Configuration != expected) return false;
            found = true;
            if (expected == ulong.MaxValue) return false;
            expected++;
        }
        return found;
    }

    internal void PreparePump(ServiceCycleReplayPumpPlan pump)
    {
        if (_hasPendingStart)
            throw new InvalidOperationException("The previous replay pump did not consume its start decision.");
        var sequence = pump.StartSequence(_capturesServiceKey);
        if (sequence == 0) return;
        if (_startCursor >= _plan.StartCount || _plan.GetStart(_startCursor).Sequence != sequence)
            throw new InvalidOperationException("Replay start evidence is missing, extra, or out of order.");
        _pendingStart = _plan.GetStart(_startCursor);
        _hasPendingStart = true;
    }

    public TConfig ConfigurationFor(ulong generation)
    {
        if (!_plan.ContainsConfiguration(generation))
            throw new InvalidOperationException("Replay has no configuration publication evidence for this generation.");
        if (_plan.TryFindConfigurationCycle(generation, out var cycleIndex))
        {
            var cycle = _plan.GetCycle(cycleIndex);
            var input = cycle.Input;
            var context = cycle.Context;
            return _hydrator.HydrateConfiguration(in input, in context);
        }
        // Publication-only generations have no detached value bytes by design. The value is never
        // consumed by a captured cycle, so hydrate a typed placeholder while preserving exact generation evidence.
        if (_plan.CycleCount != 0)
        {
            var first = _plan.GetCycle(0);
            var input = first.Input;
            var context = first.Context;
            return _hydrator.HydrateConfiguration(in input, in context);
        }
        throw new InvalidOperationException("Replay has no typed configuration evidence.");
    }

    internal void PublishStrategy(
        ulong generation,
        ServiceCycleReplayStrategyGenerationSource generationSource)
    {
        if (generationSource is null) throw new ArgumentNullException(nameof(generationSource));
        if (_strategyCursor >= _plan.StrategyPublicationCount ||
            _plan.GetStrategyPublication(_strategyCursor).Generation != generation)
            throw new InvalidOperationException("Replay strategy publication evidence is missing or out of order.");
        if (generation <= _latestStrategy)
            throw new InvalidOperationException("Replay strategy generation did not advance.");
        var publication = _plan.GetStrategyPublication(_strategyCursor++);
        if (!publication.IsCaptureDerived && generationSource.Generation != generation)
            generationSource.AdvanceTo(generation);
        _latestStrategy = generation;
    }

    private readonly int _capturesServiceKey;

    private static ServiceDecisionCode DecisionCode(int value, ServiceDecisionCode common) =>
        value >= ServiceDecisionCode.FirstFeatureCode ? new ServiceDecisionCode(value) : common;

    private static T Required<T>(T value) where T : class =>
        value ?? throw new InvalidOperationException("The replay execution factory returned a null component.");
}
