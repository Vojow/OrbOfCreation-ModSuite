using System;

namespace OrbAutomata;

internal enum AlchemyLoadoutActionKind { Add = 1, Remove = 2, Move = 3 }

internal readonly struct AlchemyLoadoutAction
{
    internal AlchemyLoadoutAction(AlchemyLoadoutActionKind kind, Guid recipeId,
        int destination, int amount, long lifecycleEpoch)
    {
        if (recipeId == Guid.Empty) throw new ArgumentException("A recipe identity is required.", nameof(recipeId));
        Kind = kind;
        RecipeId = recipeId;
        Destination = destination;
        Amount = amount;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal AlchemyLoadoutActionKind Kind { get; }
    internal Guid RecipeId { get; }
    internal int Destination { get; }
    internal int Amount { get; }
    internal long LifecycleEpoch { get; }
}
