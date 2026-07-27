namespace OrbAutomata;

internal readonly struct AutoHarvestSubmissionState
{
    public AutoHarvestSubmissionState(
        bool isValid,
        int usedEntryCount,
        int emptyEntryCount,
        bool nativeHasEmptyEntry,
        int supportedCollectCount,
        int pairMatchCount,
        int pairQuantity,
        bool pairEngaged)
    {
        IsValid = isValid;
        UsedEntryCount = usedEntryCount;
        EmptyEntryCount = emptyEntryCount;
        NativeHasEmptyEntry = nativeHasEmptyEntry;
        SupportedCollectCount = supportedCollectCount;
        PairMatchCount = pairMatchCount;
        PairQuantity = pairQuantity;
        PairEngaged = pairEngaged;
    }

    public static AutoHarvestSubmissionState Invalid => new(false, 0, 0, false, 0, 0, 0, false);
    public bool IsValid { get; }
    public int UsedEntryCount { get; }
    public int EmptyEntryCount { get; }
    public bool NativeHasEmptyEntry { get; }
    public int SupportedCollectCount { get; }
    public int PairMatchCount { get; }
    public int PairQuantity { get; }
    public bool PairEngaged { get; }

    public override string ToString() =>
        $"Valid={IsValid}, UsedEntries={UsedEntryCount}, EmptyEntries={EmptyEntryCount}, " +
        $"NativeHasEmptyEntry={NativeHasEmptyEntry}, Supported={SupportedCollectCount}, " +
        $"PairMatches={PairMatchCount}, PairQuantity={PairQuantity}, PairEngaged={PairEngaged}";
}
