namespace OrbAutomata;

internal enum AutoHarvestPairHealthKind
{
    NotSelected = 0,
    NotObserved = 1,
    Eligible = 2,
    ProgressionLocked = 3,
    NativeBusy = 4,
    QueueBlocked = 5,
    RegistryNotReady = 6,
    ContractUnavailable = 7,
    Faulted = 8,
}

internal readonly struct AutoHarvestPairHealth
{
    public AutoHarvestPairHealth(
        AutoHarvestPair pair,
        bool selected,
        AutoHarvestPairHealthKind kind,
        bool featureScoped = false)
    {
        Pair = pair;
        Selected = selected;
        Kind = kind;
        FeatureScoped = featureScoped;
    }

    public AutoHarvestPair Pair { get; }
    public bool Selected { get; }
    public AutoHarvestPairHealthKind Kind { get; }
    public bool FeatureScoped { get; }

    public static AutoHarvestPairHealth NotSelected(AutoHarvestPair pair) =>
        new(pair, selected: false, AutoHarvestPairHealthKind.NotSelected);

    public static AutoHarvestPairHealth NotObserved(AutoHarvestPair pair) =>
        new(pair, selected: true, AutoHarvestPairHealthKind.NotObserved);

    public static AutoHarvestPairHealth Eligible(AutoHarvestPair pair) =>
        new(pair, selected: true, AutoHarvestPairHealthKind.Eligible);
}
