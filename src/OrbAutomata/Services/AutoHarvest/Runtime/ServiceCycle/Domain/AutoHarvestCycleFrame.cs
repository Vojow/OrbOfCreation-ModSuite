using System;

namespace OrbAutomata;

internal enum AutoHarvestPairCaptureKind
{
    NotSelected = 1,
    Captured = 2,
    Unavailable = 3,
}

internal enum AutoHarvestCaptureUnavailableReason
{
    None = 0,
    RegistryNotReady = 1,
    ContractUnavailable = 2,
    Faulted = 3,
}

internal enum AutoHarvestCaptureFailureScope
{
    Feature = 1,
    Pair = 2,
}

internal readonly struct AutoHarvestPairCapture
{
    private AutoHarvestPairCapture(
        AutoHarvestPair pair,
        AutoHarvestPairCaptureKind kind,
        AutoHarvestPairFacts facts,
        AutoHarvestCaptureUnavailableReason unavailableReason,
        AutoHarvestCaptureFailureScope failureScope)
    {
        Pair = pair;
        Kind = kind;
        Facts = facts;
        UnavailableReason = unavailableReason;
        FailureScope = failureScope;
    }

    public AutoHarvestPair Pair { get; }
    public AutoHarvestPairCaptureKind Kind { get; }
    public AutoHarvestPairFacts Facts { get; }
    public AutoHarvestCaptureUnavailableReason UnavailableReason { get; }
    public AutoHarvestCaptureFailureScope FailureScope { get; }
    public bool IsValid => Kind switch
    {
        AutoHarvestPairCaptureKind.NotSelected =>
            UnavailableReason == AutoHarvestCaptureUnavailableReason.None,
        AutoHarvestPairCaptureKind.Captured => UnavailableReason == AutoHarvestCaptureUnavailableReason.None,
        AutoHarvestPairCaptureKind.Unavailable => UnavailableReason != AutoHarvestCaptureUnavailableReason.None,
        _ => false,
    };

    public static AutoHarvestPairCapture NotSelected(AutoHarvestPair pair) =>
        new(pair, AutoHarvestPairCaptureKind.NotSelected, default,
            AutoHarvestCaptureUnavailableReason.None, default);

    public static AutoHarvestPairCapture Captured(
        AutoHarvestPair pair,
        AutoHarvestPairFacts facts) =>
        new(pair, AutoHarvestPairCaptureKind.Captured, facts,
            AutoHarvestCaptureUnavailableReason.None, default);

    public static AutoHarvestPairCapture Unavailable(
        AutoHarvestPair pair,
        AutoHarvestCaptureUnavailableReason unavailableReason,
        AutoHarvestCaptureFailureScope failureScope)
    {
        if (unavailableReason == AutoHarvestCaptureUnavailableReason.None)
            throw new ArgumentOutOfRangeException(nameof(unavailableReason));
        if (failureScope is not AutoHarvestCaptureFailureScope.Feature and not AutoHarvestCaptureFailureScope.Pair)
            throw new ArgumentOutOfRangeException(nameof(failureScope));
        return new AutoHarvestPairCapture(
            pair,
            AutoHarvestPairCaptureKind.Unavailable,
            default,
            unavailableReason,
            failureScope);
    }
}

internal readonly struct AutoHarvestCycleFrame
{
    public AutoHarvestCycleFrame(
        AutoHarvestPairCapture fruit,
        AutoHarvestPairCapture treasure,
        bool ownsActionFamily)
    {
        if (!fruit.IsValid || fruit.Pair != AutoHarvestPair.FruitTree)
            throw new ArgumentException("A valid fruit capture is required.", nameof(fruit));
        if (!treasure.IsValid || treasure.Pair != AutoHarvestPair.TreasureTree)
            throw new ArgumentException("A valid treasure capture is required.", nameof(treasure));
        Fruit = fruit;
        Treasure = treasure;
        OwnsActionFamily = ownsActionFamily;
    }

    public AutoHarvestPairCapture Fruit { get; }
    public AutoHarvestPairCapture Treasure { get; }
    public bool OwnsActionFamily { get; }
}
