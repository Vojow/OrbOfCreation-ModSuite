using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal static class AutomataWorldCollectionPolicies
{
    /// <summary>
    /// How often the world is re-read.
    /// </summary>
    /// <remarks>
    /// Faster than both consumers' defaults on purpose — Auto Buy evaluates every 500 ms and Auto
    /// Harvest every second. A world-bound service waits for a snapshot it has not acted on yet, so
    /// collecting slower than a service evaluates throttles that service to the collection rate. A
    /// measured warm pass is a little over a millisecond, so four passes a second costs well under one
    /// percent of a 60 Hz frame budget.
    ///
    /// Auto Buy's interval is configurable to any positive value, so an operator can set it below this
    /// one. That does not break anything — the gate is what stops a second purchase against one
    /// reading of the world, and it must — but it does mean the configured interval becomes a floor
    /// rather than the achieved cadence. Lowering this is the fix if that ever matters, not weakening
    /// the gate.
    /// </remarks>
    private static readonly MonotonicDuration DefaultInterval =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));

    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));

    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(5));

    /// <summary>How long to wait before retrying when nothing could be read at all.</summary>
    internal static readonly MonotonicDuration UnavailableRetry =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(5));

    internal static ServiceId ServiceId => new("orbautomata.world-collection");

    internal static WakePolicy DefaultWakePolicy => WakePolicy.AfterBatch(DefaultInterval);

    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);
}

/// <summary>
/// The shared world pass: the capture reads the game, the worker derives and publishes.
/// </summary>
/// <remarks>
/// <para>
/// Collection is infrastructure rather than a feature, and it would have been simpler to run it from
/// the frame pump directly. Registering it as a service is what keeps it inside budget accounting,
/// tracing, health projection, and emergency stop — the machinery that already exists to answer "what
/// is this costing and is it healthy", which is exactly what will be asked of the one pass every
/// other service depends on.
/// </para>
/// <para>
/// It has no configuration gate. Every other service decides whether it is switched on; this one runs
/// whenever the runtime does, because a consumer that finds the world empty cannot tell "collection
/// is off" from "the save has nothing in it".
/// </para>
/// </remarks>
internal static class AutomataWorldCollectionService
{
    internal static IServiceCycleSourceDefinition<
        AutomataWorldCollectionState,
        AutomataWorldCollectionAction> Define(
        IAutomataWorldCapturePort capture,
        IServiceWorldPublicationSink<GameWorldState> publish)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        if (publish is null) throw new ArgumentNullException(nameof(publish));

        var metadata = new AutomataServiceMetadata(
            AutomataWorldCollectionPolicies.ServiceId,
            AutomataWorldCollectionPolicies.DefaultWakePolicy,
            AutomataWorldCollectionPolicies.FaultRecoveryPolicy);

        return AutomataService.DefineSource<
            AutomataWorldCollectionState,
            AutomataWorldCollectionAction>(
                in metadata,
                createWorker: static () => new AutomataWorldCollectionWorker(),
                shouldStart: ShouldStart,
                capture: Capture,
                execute: Execute);

        ServiceCaptureResult Capture(
            GameWorldCycleFrame frame,
            in SuiteRuntimeConfiguration config,
            in ServiceCaptureContext context)
        {
            if (!capture.IsAvailable)
            {
                return ServiceCaptureResult.Unavailable(
                    CommonServiceDecisionCodes.CaptureUnavailable,
                    WakePolicy.AfterDecision(AutomataWorldCollectionPolicies.UnavailableRetry));
            }

            frame.CollectedAt = context.CapturedAt;
            capture.Collect(frame);
            return ServiceCaptureResult.Captured(CommonServiceDecisionCodes.Captured);
        }

        // The one point at which the shared world becomes live, and it is on the main thread. Actions
        // dispatch before any service's ShouldStart runs, so a snapshot acquired this frame is
        // visible to every consumer this same frame, and no consumer can see it change mid-decision.
        ServiceActionResult Execute(
            in AutomataWorldCollectionAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.CommittedPublication(
                CommonActionResultCodes.Committed,
                ServicePublicationEvidence.World(publish.Publish(action.World, action.Generation)));
    }

    /// <summary>
    /// Always ready. The interval in the wake policy is what paces collection; there is no state in
    /// which the suite wants a stale world instead of a fresh one.
    /// </summary>
    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
}
