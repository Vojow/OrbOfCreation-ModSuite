using System;

namespace OrbAutomata;

/// <summary>
/// One cycle-pinned production request. Role narrowing is intentionally absent: the live boundary
/// proves the recipe/Scroll relation, while the worker's configuration generation owns selection.
/// </summary>
internal readonly struct AutoScribeCycleAction
{
    internal AutoScribeCycleAction(
        Guid recipeId,
        Guid scrollId,
        int level,
        long collectedAtEpoch,
        long collectedAtFrame = 0)
    {
        if (recipeId == Guid.Empty)
            throw new ArgumentException("A Scribe recipe identity is required.", nameof(recipeId));
        if (scrollId == Guid.Empty)
            throw new ArgumentException("A Scroll identity is required.", nameof(scrollId));
        if (level <= 0)
            throw new ArgumentOutOfRangeException(nameof(level));
        RecipeId = recipeId;
        ScrollId = scrollId;
        Level = level;
        CollectedAtEpoch = collectedAtEpoch;
        CollectedAtFrame = collectedAtFrame;
    }

    internal Guid RecipeId { get; }
    internal Guid ScrollId { get; }
    internal int Level { get; }
    internal long CollectedAtEpoch { get; }
    internal long CollectedAtFrame { get; }
}
