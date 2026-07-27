using System;

namespace OrbAutomata;

internal readonly struct AutoHarvestActivePairState
{
    public AutoHarvestActivePairState(int matchCount, int quantity, bool engaged)
    {
        MatchCount = matchCount;
        Quantity = quantity;
        Engaged = engaged;
    }

    public int MatchCount { get; }
    public int Quantity { get; }
    public bool Engaged { get; }
}

internal readonly struct AutoHarvestActiveActionSnapshot
{
    public AutoHarvestActiveActionSnapshot(
        bool isValid,
        int usedEntryCount,
        int emptyEntryCount,
        bool nativeHasEmptyEntry,
        int supportedCollectCount,
        in AutoHarvestActivePairState fruit,
        in AutoHarvestActivePairState treasure)
    {
        IsValid = isValid;
        UsedEntryCount = usedEntryCount;
        EmptyEntryCount = emptyEntryCount;
        NativeHasEmptyEntry = nativeHasEmptyEntry;
        SupportedCollectCount = supportedCollectCount;
        Fruit = fruit;
        Treasure = treasure;
    }

    public static AutoHarvestActiveActionSnapshot Invalid => default;
    public bool IsValid { get; }
    public int UsedEntryCount { get; }
    public int EmptyEntryCount { get; }
    public bool NativeHasEmptyEntry { get; }
    public int SupportedCollectCount { get; }
    public AutoHarvestActivePairState Fruit { get; }
    public AutoHarvestActivePairState Treasure { get; }

    public AutoHarvestSubmissionState Project(AutoHarvestPair pair)
    {
        if (!IsValid) return AutoHarvestSubmissionState.Invalid;
        var pairState = pair switch
        {
            AutoHarvestPair.FruitTree => Fruit,
            AutoHarvestPair.TreasureTree => Treasure,
            _ => throw new ArgumentOutOfRangeException(nameof(pair)),
        };
        return new AutoHarvestSubmissionState(
            true,
            UsedEntryCount,
            EmptyEntryCount,
            NativeHasEmptyEntry,
            SupportedCollectCount,
            pairState.MatchCount,
            pairState.Quantity,
            pairState.Engaged);
    }
}
