using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoHarvestServiceProjection
{
    private const int FruitHealthFirstKey = 4;
    private const int TreasureHealthFirstKey = 7;

    public static void Write(
        in AutoHarvestCycleState state,
        ServiceStateProjectionBuilder output)
    {
        output.Add(Key(1), Integer((int)state.NextPair));
        output.Add(Key(2), Boolean(state.HasPlannedAction));
        output.Add(Key(3), Integer((int)state.PlannedPair));
        WriteHealth(state.FruitHealth, FruitHealthFirstKey, output);
        WriteHealth(state.TreasureHealth, TreasureHealthFirstKey, output);
    }

    internal static bool TryReadFruitHealth(
        in ServiceStateProjectionSnapshot projection,
        out AutoHarvestPairHealth health) =>
        TryReadHealth(
            in projection,
            AutoHarvestPair.FruitTree,
            FruitHealthFirstKey,
            out health);

    internal static bool TryReadTreasureHealth(
        in ServiceStateProjectionSnapshot projection,
        out AutoHarvestPairHealth health) =>
        TryReadHealth(
            in projection,
            AutoHarvestPair.TreasureTree,
            TreasureHealthFirstKey,
            out health);

    private static void WriteHealth(
        in AutoHarvestPairHealth health,
        int firstKey,
        ServiceStateProjectionBuilder output)
    {
        output.Add(Key(firstKey), Boolean(health.Selected));
        output.Add(Key(firstKey + 1), Integer((int)health.Kind));
        output.Add(Key(firstKey + 2), Boolean(health.FeatureScoped));
    }

    private static bool TryReadHealth(
        in ServiceStateProjectionSnapshot projection,
        AutoHarvestPair pair,
        int firstKey,
        out AutoHarvestPairHealth health)
    {
        var selected = default(ServiceProjectionValue);
        var kind = default(ServiceProjectionValue);
        var featureScoped = default(ServiceProjectionValue);
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == firstKey) selected = entry.Value;
            else if (entry.Key.Value == firstKey + 1) kind = entry.Value;
            else if (entry.Key.Value == firstKey + 2) featureScoped = entry.Value;
        }
        if (selected.Kind != ServiceProjectionValueKind.Boolean ||
            kind.Kind != ServiceProjectionValueKind.Integer ||
            featureScoped.Kind != ServiceProjectionValueKind.Boolean ||
            kind.Integer is < (int)AutoHarvestPairHealthKind.NotSelected or
                > (int)AutoHarvestPairHealthKind.Faulted)
        {
            health = default;
            return false;
        }
        health = new AutoHarvestPairHealth(
            pair,
            selected.Boolean,
            (AutoHarvestPairHealthKind)kind.Integer,
            featureScoped.Boolean);
        return true;
    }

    private static ServiceProjectionKey Key(int value) => new(value);
    private static ServiceProjectionValue Boolean(bool value) =>
        ServiceProjectionValue.FromBoolean(value);
    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}
