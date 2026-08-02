using System;

namespace OrbAutomata;

internal enum SpellWorkbenchActionKind
{
    Select = 0,
    Discover = 1,
    Create = 2,
}

internal readonly struct SpellWorkbenchAction
{
    internal SpellWorkbenchAction(SpellWorkbenchActionKind kind, Guid spellRecipeId, long lifecycleEpoch)
    {
        if (spellRecipeId == Guid.Empty)
            throw new ArgumentException("A spell recipe identity is required.", nameof(spellRecipeId));
        Kind = kind;
        SpellRecipeId = spellRecipeId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal SpellWorkbenchActionKind Kind { get; }
    internal Guid SpellRecipeId { get; }
    internal long LifecycleEpoch { get; }
}
