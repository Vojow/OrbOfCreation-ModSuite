using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Derives one captured frame into one immutable snapshot and publishes it, off the Unity thread.
/// </summary>
/// <remarks>
/// <para>
/// This is where the design pays off: everything below is arithmetic over values already in hand, so
/// it costs the game nothing. The worker holds no binder, no accessor, and no game type —
/// <see cref="GameWorldFrameDeriver"/> is static and reads only the frame, which makes "derivation
/// never touches the game" a property of the types rather than a rule to remember.
/// </para>
/// <para>
/// The snapshot leaves as an action rather than being published from here, so the live generation
/// only ever changes on the main thread, at one point in the pump. See
/// <see cref="AutomataWorldCollectionAction"/>.
/// </para>
/// </remarks>
internal sealed class AutomataWorldCollectionWorker :
    IServiceCycleSourceWorkerDefinition<
        AutomataWorldCollectionState,
        AutomataWorldCollectionAction>
{
    public AutomataWorldCollectionState CreateState(LifecycleGeneration lifecycle) => default;

    public void ReleaseState(ref AutomataWorldCollectionState state) => state = default;

    /// <summary>
    /// Derives the world from the buffer the main-thread capture filled.
    /// </summary>
    public WakePolicy Evaluate(
        GameWorldCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutomataWorldCollectionState state,
        ServiceActionWriter<AutomataWorldCollectionAction> actions)
    {
        var report = frame.Report;
        var world = GameWorldFrameDeriver.Build(frame);
        var generation = new WorldGeneration(checked((ulong)frame.CollectedAtFrame));

        // Emitted unconditionally, even when the readings match last cycle's. Suppressing an
        // unchanged snapshot would leave consumers unable to tell a stalled collector from a still
        // world, and the generation is exactly how they tell.
        actions.Add(new AutomataWorldCollectionAction(world, generation));
        state.LastPublished = generation;
        state.LastEntities = report.TotalSampled;
        state.LastPassComplete = report.IsComplete;
        state.LastCategoriesUnavailable = CountUnavailable(report);

        return WakePolicy.Default;
    }

    public void ProjectState(
        in AutomataWorldCollectionState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutomataWorldCollectionProjection.Write(state, output);

    private static int CountUnavailable(WorldCollectionReport report)
    {
        var unavailable = 0;
        foreach (var category in report.Categories)
        {
            if (category.Outcome != WorldCategoryOutcome.Collected) unavailable++;
        }

        return unavailable;
    }
}
