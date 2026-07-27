#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

/// <summary>
/// What the suite knows about <see cref="ServiceCycleProfileSpan"/> as a set: the whole enumeration,
/// each span's reported name, and which spans are the observer measuring itself.
/// </summary>
internal static class ServiceCycleProfileSpans
{
    /// <summary>Every declared span, so a reader can enumerate the set without reflecting itself.</summary>
    internal static ServiceCycleProfileSpan[] All() =>
        (ServiceCycleProfileSpan[])Enum.GetValues(typeof(ServiceCycleProfileSpan));

    /// <summary>
    /// Whether a span measures the suite's own observation rather than the game work around it.
    /// </summary>
    /// <remarks>
    /// The full trace emits from inside the frame, so without this the frame's own span would report
    /// the cost of recording it — the red herring the north star's full-trace mandate names. The
    /// measurement recorder subtracts a span marked here from whatever span encloses it, which keeps
    /// the fence at the one place that already knows the nesting and leaves the probe API alone.
    /// </remarks>
    internal static bool IsObserverOverhead(int stageCode) =>
        stageCode is (int)ServiceCycleProfileSpan.SemanticStart or
            (int)ServiceCycleProfileSpan.SemanticTerminal or
            (int)ServiceCycleProfileSpan.SemanticPumpSummary;

    /// <summary>
    /// The reported name of a span id. An id from a build that has since retired the span decodes to
    /// its number rather than to a name that would claim the measurement still exists.
    /// </summary>
    internal static string Name(int stageCode) => stageCode switch
    {
        (int)ServiceCycleProfileSpan.SemanticStart => "Semantic start emission",
        (int)ServiceCycleProfileSpan.SemanticTerminal => "Semantic terminal emission",
        (int)ServiceCycleProfileSpan.SemanticPumpSummary => "Semantic pump summary",
        (int)ServiceCycleProfileSpan.OverallPump => "Overall pump",
        (int)ServiceCycleProfileSpan.AcquireResponses => "Pump acquire responses",
        (int)ServiceCycleProfileSpan.DispatchActions => "Pump dispatch actions",
        (int)ServiceCycleProfileSpan.StartCycles => "Pump start cycles",
        (int)ServiceCycleProfileSpan.ReconcileLifecycle => "Pump reconcile lifecycle",
        (int)ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence => "Auto Harvest binding/coherence",
        (int)ServiceCycleProfileSpan.AutoHarvestActionBeforeSnapshot => "Auto Harvest action before snapshot",
        (int)ServiceCycleProfileSpan.AutoHarvestActionNativeSubmission => "Auto Harvest native submission",
        (int)ServiceCycleProfileSpan.AutoHarvestActionAfterSnapshot => "Auto Harvest after snapshot",
        (int)ServiceCycleProfileSpan.AutoHarvestActionPostconditionVerification =>
            "Auto Harvest postcondition verification",
        (int)ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution => "Auto Harvest prototype resolution",
        (int)ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead => "Auto Buy queue-room read",
        (int)ServiceCycleProfileSpan.AutoBuyActionCandidateResolution => "Auto Buy candidate resolution",
        (int)ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation => "Auto Buy admission revalidation",
        (int)ServiceCycleProfileSpan.AutoBuyActionNativeSubmission => "Auto Buy native submission",
        _ => "Stage " + stageCode.ToString(CultureInfo.InvariantCulture),
    };
}
#endif
