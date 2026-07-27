using System;

namespace OrbAutomata;

internal enum AutoHarvestPair
{
    FruitTree,
    TreasureTree,
}

internal static class AutoHarvestKnownIds
{
    public const string FruitTreePlot = "6782dd13-e229-4385-a1aa-8ed86e6ea1ed";
    public const string FruitTreeCollect = "60ea60a2-44e9-41c2-86d6-3935fae0b647";
    public const string TreasureTreePlot = "2d41cfc1-bffa-43b5-b3a8-5e4d5ad85434";
    public const string TreasureTreeCollect = "3eb68f6f-c2f2-405a-88d2-e5c80345aeb4";
    public const string ActivePlotNodeActions = "70871e86-100b-4ae0-ba9b-fc96e09b7e1f";
    public const string CompletionScalingWeight = "be446180-242f-40d2-910e-91e735fc20ad";
    public const string TreasureTreeRewardPool = "1a370ff9-fea7-4a2a-bca7-57fdb2862356";
    public const string FruitTreeRewardPool = "b3ab80f0-80c7-41d4-b4c7-f34c3e909104";

    private static readonly Guid FruitTreePlotId = new(FruitTreePlot);
    private static readonly Guid FruitTreeCollectId = new(FruitTreeCollect);
    private static readonly Guid TreasureTreePlotId = new(TreasureTreePlot);
    private static readonly Guid TreasureTreeCollectId = new(TreasureTreeCollect);

    public static bool IsSupportedPair(Guid plotUuid, Guid actionUuid) =>
        plotUuid == FruitTreePlotId && actionUuid == FruitTreeCollectId ||
        plotUuid == TreasureTreePlotId && actionUuid == TreasureTreeCollectId;

    public static bool IsSupportedAction(Guid actionUuid) =>
        actionUuid == FruitTreeCollectId || actionUuid == TreasureTreeCollectId;
}

internal enum AutoHarvestObservedPair
{
    Unrelated,
    FruitTree,
    TreasureTree,
    Contradictory,
}

internal static class AutoHarvestIdentityPolicy
{
    public static AutoHarvestObservedPair Classify(
        string plotUuid,
        string actionUuid,
        bool exactFruitReferences,
        bool exactTreasureReferences,
        bool supportedActionReference)
    {
        if (!Guid.TryParse(plotUuid, out var plot) || !Guid.TryParse(actionUuid, out var action))
            return AutoHarvestObservedPair.Contradictory;
        return Classify(plot, action, exactFruitReferences, exactTreasureReferences, supportedActionReference);
    }

    public static AutoHarvestObservedPair Classify(
        Guid plotUuid,
        Guid actionUuid,
        bool exactFruitReferences,
        bool exactTreasureReferences,
        bool supportedActionReference)
    {
        if (plotUuid == Guid.Empty || actionUuid == Guid.Empty)
            return AutoHarvestObservedPair.Contradictory;
        if (exactFruitReferences && !exactTreasureReferences) return AutoHarvestObservedPair.FruitTree;
        if (exactTreasureReferences && !exactFruitReferences) return AutoHarvestObservedPair.TreasureTree;
        if (exactFruitReferences || exactTreasureReferences || supportedActionReference)
            return AutoHarvestObservedPair.Contradictory;
        if (AutoHarvestKnownIds.IsSupportedPair(plotUuid, actionUuid) ||
            AutoHarvestKnownIds.IsSupportedAction(actionUuid))
            return AutoHarvestObservedPair.Contradictory;
        return AutoHarvestObservedPair.Unrelated;
    }
}

internal static class AutoHarvestContractValues
{
    public static bool IsFiniteNear(double actual, double expected, double tolerance = 0.0001) =>
        !double.IsNaN(actual) &&
        !double.IsInfinity(actual) &&
        !double.IsNaN(expected) &&
        !double.IsInfinity(expected) &&
        tolerance >= 0.0 &&
        Math.Abs(actual - expected) <= tolerance;
}
