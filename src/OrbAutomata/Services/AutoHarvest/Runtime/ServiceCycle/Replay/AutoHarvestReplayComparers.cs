using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbAutomata;

internal sealed class AutoHarvestCycleInputComparer :
    IServiceCycleReplayComparer<AutoHarvestCycleInputRecord>
{
    public ServiceCycleReplayRecordComparison Compare(
        in AutoHarvestCycleInputRecord expected,
        in AutoHarvestCycleInputRecord actual)
    {
        var pair = ComparePair(expected.Fruit, actual.Fruit, 1);
        if (!pair.IsMatch) return pair;
        pair = ComparePair(expected.Treasure, actual.Treasure, 12);
        if (!pair.IsMatch) return pair;
        if (expected.MasterEnabled != actual.MasterEnabled) return Mismatch(23);
        if (expected.EmergencyDisabled != actual.EmergencyDisabled) return Mismatch(24);
        if (expected.ActiveMode != actual.ActiveMode) return Mismatch(25);
        if (expected.FruitSelected != actual.FruitSelected) return Mismatch(26);
        if (expected.TreasureSelected != actual.TreasureSelected) return Mismatch(27);
        if (expected.OwnsActionFamily != actual.OwnsActionFamily) return Mismatch(28);
        return expected.EvaluationIntervalTicks != actual.EvaluationIntervalTicks
            ? Mismatch(29)
            : ServiceCycleReplayRecordComparison.Match;
    }

    private static ServiceCycleReplayRecordComparison ComparePair(
        in AutoHarvestPairCaptureRecord expected,
        in AutoHarvestPairCaptureRecord actual,
        int firstCode)
    {
        if (expected.CaptureKind != actual.CaptureKind) return Mismatch(firstCode);
        if (expected.UnavailableReason != actual.UnavailableReason) return Mismatch(firstCode + 1);
        if (expected.FailureScope != actual.FailureScope) return Mismatch(firstCode + 2);
        if (expected.Identity != actual.Identity) return Mismatch(firstCode + 3);
        if (expected.PlotVisibility != actual.PlotVisibility) return Mismatch(firstCode + 4);
        if (expected.ActionAvailability != actual.ActionAvailability) return Mismatch(firstCode + 5);
        if (expected.Prerequisites != actual.Prerequisites) return Mismatch(firstCode + 6);
        if (expected.Readiness != actual.Readiness) return Mismatch(firstCode + 7);
        if (expected.ActionSafety != actual.ActionSafety) return Mismatch(firstCode + 8);
        if (expected.NoDuplicate != actual.NoDuplicate) return Mismatch(firstCode + 9);
        return expected.ActionSlotAvailability != actual.ActionSlotAvailability
            ? Mismatch(firstCode + 10)
            : ServiceCycleReplayRecordComparison.Match;
    }

    private static ServiceCycleReplayRecordComparison Mismatch(int fieldCode) => new(fieldCode);
}

internal sealed class AutoHarvestStateComparer : IServiceCycleReplayComparer<AutoHarvestStateRecord>
{
    public ServiceCycleReplayRecordComparison Compare(
        in AutoHarvestStateRecord expected,
        in AutoHarvestStateRecord actual)
    {
        if (expected.Lifecycle != actual.Lifecycle) return Mismatch(1);
        if (expected.NextPair != actual.NextPair) return Mismatch(2);
        if (expected.HasPlannedAction != actual.HasPlannedAction) return Mismatch(3);
        if (expected.PlannedPair != actual.PlannedPair) return Mismatch(4);
        var health = CompareHealth(expected.FruitHealth, actual.FruitHealth, 5);
        if (!health.IsMatch) return health;
        return CompareHealth(expected.TreasureHealth, actual.TreasureHealth, 8);
    }

    private static ServiceCycleReplayRecordComparison CompareHealth(
        in AutoHarvestPairHealthRecord expected,
        in AutoHarvestPairHealthRecord actual,
        int firstCode)
    {
        if (expected.Selected != actual.Selected) return Mismatch(firstCode);
        if (expected.Kind != actual.Kind) return Mismatch(firstCode + 1);
        return expected.FeatureScoped != actual.FeatureScoped
            ? Mismatch(firstCode + 2)
            : ServiceCycleReplayRecordComparison.Match;
    }

    private static ServiceCycleReplayRecordComparison Mismatch(int fieldCode) => new(fieldCode);
}

internal sealed class AutoHarvestActionComparer : IServiceCycleReplayComparer<AutoHarvestActionRecord>
{
    public ServiceCycleReplayRecordComparison Compare(
        in AutoHarvestActionRecord expected,
        in AutoHarvestActionRecord actual) =>
        expected.Pair == actual.Pair
            ? ServiceCycleReplayRecordComparison.Match
            : new ServiceCycleReplayRecordComparison(1);
}
