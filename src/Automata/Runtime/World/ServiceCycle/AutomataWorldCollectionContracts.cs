using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// One derived snapshot on its way from the worker to the main thread.
/// </summary>
/// <remarks>
/// <para>
/// Collection's only action. The worker produces an immutable snapshot and the main thread hands it
/// to the publisher, which is what makes the live generation change at exactly one point in the pump
/// — before any service's <c>ShouldStart</c> runs that frame. A worker publishing directly would
/// advance the world while consumers were mid-decision.
/// </para>
/// <para>
/// Carrying the snapshot by reference is the sanctioned shape rather than an exception: actions are
/// validated as immutable publications, the same rule class as configuration, strategy and world, so
/// a value nobody can mutate is exactly what an action is allowed to hold. See W4 in
/// <c>docs/runtime-architecture/world-collection-decisions.md</c>.
/// </para>
/// </remarks>
internal readonly struct AutomataWorldCollectionAction
{
    internal AutomataWorldCollectionAction(GameWorldState world, WorldGeneration generation)
    {
        World = world;
        Generation = generation;
    }

    internal GameWorldState World { get; }

    /// <summary>The frame the readings were true for, not the frame they finished deriving on.</summary>
    internal WorldGeneration Generation { get; }
}

/// <summary>
/// What the collection worker remembers between cycles.
/// </summary>
/// <remarks>
/// Almost nothing, deliberately. Each pass supersedes the last, so the only things worth keeping are
/// the ones a reader of the service's diagnostics would ask for: what it last published, how much of
/// the world was in it, and how much of it this build refused to yield.
/// </remarks>
internal struct AutomataWorldCollectionState
{
    internal WorldGeneration LastPublished;
    internal int LastEntities;
    internal int LastCategoriesUnavailable;
    internal bool LastPassComplete;
}
