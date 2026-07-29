using System;

namespace OrbAutomata;

internal enum AutoConceptActionKind
{
    Add = 0,
    RemoveOwned = 1,
    RotateOut = 2,
}

internal readonly struct AutoConceptPlanBelief
{
    internal AutoConceptPlanBelief(
        int quantity,
        int queuedQuantity,
        int maximumQuantity,
        Guid coreTypeId,
        int authoredDrainResources)
    {
        Quantity = quantity;
        QueuedQuantity = queuedQuantity;
        MaximumQuantity = maximumQuantity;
        CoreTypeId = coreTypeId;
        AuthoredDrainResources = authoredDrainResources;
    }

    public int Quantity { get; }
    public int QueuedQuantity { get; }
    public int MaximumQuantity { get; }
    public Guid CoreTypeId { get; }
    public int AuthoredDrainResources { get; }
}

internal readonly struct AutoConceptCycleAction
{
    internal AutoConceptCycleAction(
        AutoConceptActionKind kind,
        Guid recipeId,
        int targetOrDelta,
        Guid replacementId,
        long collectedAtEpoch,
        in AutoConceptPlanBelief belief)
    {
        if (recipeId == Guid.Empty) throw new ArgumentException("A Concept action requires a recipe.", nameof(recipeId));
        if (targetOrDelta <= 0) throw new ArgumentOutOfRangeException(nameof(targetOrDelta));
        Kind = kind;
        RecipeId = recipeId;
        TargetOrDelta = targetOrDelta;
        ReplacementId = replacementId;
        CollectedAtEpoch = collectedAtEpoch;
        Belief = belief;
    }

    public AutoConceptActionKind Kind { get; }
    public Guid RecipeId { get; }
    public int TargetOrDelta { get; }
    public Guid ReplacementId { get; }
    public long CollectedAtEpoch { get; }
    public AutoConceptPlanBelief Belief { get; }
}
