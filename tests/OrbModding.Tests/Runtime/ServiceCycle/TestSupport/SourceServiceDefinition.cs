using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>
/// A service of the source shape, with its capture under the test's control.
/// </summary>
/// <remarks>
/// The capture is the only main-thread stage a service can still hold, so every fact about what
/// happens while service code is running between the start decision and the handoff has to be
/// written against this shape. <see cref="ExecutionServiceDefinition"/> cannot serve: it is the
/// ordinary shape and by construction has nothing there.
/// </remarks>
internal sealed class SourceServiceDefinition :
    IServiceCycleSourceDefinition<SourceState, SourceAction>
{
    private readonly SourceWorkerControl _worker = new();

    internal SourceServiceDefinition(string id) => ServiceId = new ServiceId(id);

    public ServiceId ServiceId { get; }
    public WakePolicy DefaultWakePolicy { get; set; } = WakePolicy.Immediate;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; set; } = new(
        new MonotonicDuration(10), new MonotonicDuration(80));

    internal int StartCount { get; private set; }
    internal int CaptureCount { get; private set; }

    /// <summary>The world the runtime pinned for the most recent capture, as a structure count.</summary>
    internal int LastCapturedWorldStructures { get; private set; }
    internal StrategyGeneration LastCapturedStrategy { get; private set; }
    internal ServiceStartDecision StartDecision { get; set; } =
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
    internal ServiceCaptureResult CaptureResult { get; set; } =
        ServiceCaptureResult.Captured(CommonServiceDecisionCodes.Captured);
    internal Action? ShouldStartCallback { get; set; }
    internal Action? CaptureCallback { get; set; }
    internal bool FaultNextCapture { get; set; }

    /// <summary>The reading of the world the cycle the worker ran was opened against.</summary>
    /// <remarks>
    /// Taken from the cycle identity rather than from a snapshot: a source worker is handed the buffer
    /// its own capture filled and never the published world, so the generation on the identity is the
    /// only thing it can say about which reading its cycle belongs to.
    /// </remarks>
    internal WorldGeneration LastEvaluatedWorld => _worker.LastEvaluatedWorld;
    internal int EvaluationCount => _worker.EvaluationCount;

    public IServiceCycleSourceWorkerDefinition<SourceState, SourceAction> CreateWorkerDefinition() =>
        new SourceWorkerDefinition(_worker);

    public ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context)
    {
        StartCount++;
        ShouldStartCallback?.Invoke();
        return StartDecision;
    }

    public ServiceCaptureResult Capture(
        GameWorldCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in ServiceCaptureContext context)
    {
        CaptureCount++;
        LastCapturedWorldStructures = context.World.Structures.AsSpan().Length;
        LastCapturedStrategy = context.Strategy;
        CaptureCallback?.Invoke();
        if (FaultNextCapture)
        {
            FaultNextCapture = false;
            throw new InvalidOperationException("synthetic capture fault");
        }
        return CaptureResult;
    }

    public ServiceActionJournalAttribution DescribeAction(in SourceAction action) =>
        ServiceActionJournalAttribution.Publication;

    public ServiceActionResult TryExecute(
        in SourceAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context) =>
        ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
}

internal struct SourceState
{
    internal int Evaluations;
}

internal readonly struct SourceAction
{
}

/// <summary>What the worker half saw, shared with the definition that created it.</summary>
internal sealed class SourceWorkerControl
{
    internal WorldGeneration LastEvaluatedWorld;
    internal int EvaluationCount;
}

/// <summary>The worker half. It emits nothing: these fixtures are about the main-thread stage.</summary>
internal sealed class SourceWorkerDefinition :
    IServiceCycleSourceWorkerDefinition<SourceState, SourceAction>
{
    private readonly SourceWorkerControl _control;

    internal SourceWorkerDefinition(SourceWorkerControl control) => _control = control;

    public SourceState CreateState(LifecycleGeneration lifecycle) => default;
    public void ReleaseState(ref SourceState state) => state = default;

    public WakePolicy Evaluate(
        GameWorldCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref SourceState state,
        ServiceActionWriter<SourceAction> actions)
    {
        state.Evaluations++;
        _control.EvaluationCount++;
        _control.LastEvaluatedWorld = context.Identity.World;
        return WakePolicy.Default;
    }

    public void ProjectState(
        in SourceState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output)
    {
    }
}
