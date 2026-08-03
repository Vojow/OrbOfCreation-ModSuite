using System;

namespace OrbAutomata;

internal enum AlchemyLoadoutActionKind { Add = 1, Remove = 2, Move = 3 }

internal readonly struct AlchemyLoadoutAction
{
    internal AlchemyLoadoutAction(AlchemyLoadoutActionKind kind, Guid recipeId,
        int destination, long lifecycleEpoch)
    {
        if (recipeId == Guid.Empty) throw new ArgumentException("A recipe identity is required.", nameof(recipeId));
        Kind = kind;
        RecipeId = recipeId;
        Destination = destination;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal AlchemyLoadoutActionKind Kind { get; }
    internal Guid RecipeId { get; }
    internal int Destination { get; }
    internal long LifecycleEpoch { get; }
}
