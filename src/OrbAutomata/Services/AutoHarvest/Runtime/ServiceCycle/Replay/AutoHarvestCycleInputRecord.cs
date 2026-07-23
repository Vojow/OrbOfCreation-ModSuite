using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbAutomata;

internal readonly struct AutoHarvestPairCaptureRecord : IServiceCycleReplayRecord
{
    private AutoHarvestPairCaptureRecord(
        AutoHarvestPairCaptureKind captureKind,
        AutoHarvestCaptureUnavailableReason unavailableReason,
        AutoHarvestCaptureFailureScope failureScope,
        in AutoHarvestPairFacts facts)
    {
        CaptureKind = captureKind;
        UnavailableReason = unavailableReason;
        FailureScope = failureScope;
        Identity = facts.Identity;
        PlotVisibility = facts.PlotVisibility;
        ActionAvailability = facts.ActionAvailability;
        Prerequisites = facts.Prerequisites;
        Readiness = facts.Readiness;
        ActionSafety = facts.ActionSafety;
        NoDuplicate = facts.NoDuplicate;
        ActionSlotAvailability = facts.ActionSlotAvailability;
    }

    public AutoHarvestPairCaptureKind CaptureKind { get; }
    public AutoHarvestCaptureUnavailableReason UnavailableReason { get; }
    public AutoHarvestCaptureFailureScope FailureScope { get; }
    public AutoHarvestEvidenceState Identity { get; }
    public AutoHarvestEvidenceState PlotVisibility { get; }
    public AutoHarvestEvidenceState ActionAvailability { get; }
    public AutoHarvestEvidenceState Prerequisites { get; }
    public AutoHarvestEvidenceState Readiness { get; }
    public AutoHarvestActionSafetyState ActionSafety { get; }
    public AutoHarvestEvidenceState NoDuplicate { get; }
    public AutoHarvestEvidenceState ActionSlotAvailability { get; }

    public static AutoHarvestPairCaptureRecord FromCapture(in AutoHarvestPairCapture capture) =>
        new(capture.Kind, capture.UnavailableReason, capture.FailureScope, capture.Facts);

    public AutoHarvestPairCapture ToCapture(AutoHarvestPair pair)
    {
        if (CaptureKind == AutoHarvestPairCaptureKind.NotSelected)
            return AutoHarvestPairCapture.NotSelected(pair);
        if (CaptureKind == AutoHarvestPairCaptureKind.Unavailable)
            return AutoHarvestPairCapture.Unavailable(pair, UnavailableReason, FailureScope);
        if (CaptureKind != AutoHarvestPairCaptureKind.Captured)
            throw new InvalidOperationException("The replay capture kind is invalid.");
        var facts = ToFacts();
        return AutoHarvestPairCapture.Captured(pair, facts);
    }

    internal static AutoHarvestPairCaptureRecord Decode(
        AutoHarvestPairCaptureKind captureKind,
        AutoHarvestCaptureUnavailableReason unavailableReason,
        AutoHarvestCaptureFailureScope failureScope,
        in AutoHarvestPairFacts facts)
    {
        var hasNoFailure = unavailableReason == AutoHarvestCaptureUnavailableReason.None &&
            failureScope == default;
        if (captureKind == AutoHarvestPairCaptureKind.NotSelected && hasNoFailure && IsDefault(facts))
            return FromCapture(AutoHarvestPairCapture.NotSelected(AutoHarvestPair.FruitTree));
        if (captureKind == AutoHarvestPairCaptureKind.Captured && hasNoFailure)
            return FromCapture(AutoHarvestPairCapture.Captured(AutoHarvestPair.FruitTree, facts));
        if (captureKind == AutoHarvestPairCaptureKind.Unavailable && IsDefault(facts))
        {
            var capture = AutoHarvestPairCapture.Unavailable(
                AutoHarvestPair.FruitTree,
                unavailableReason,
                failureScope);
            return FromCapture(capture);
        }
        throw new ArgumentException("The replay pair capture is invalid.");
    }

    private AutoHarvestPairFacts ToFacts() => new(
        Identity,
        PlotVisibility,
        ActionAvailability,
        Prerequisites,
        Readiness,
        ActionSafety,
        NoDuplicate,
        ActionSlotAvailability);

    private static bool IsDefault(in AutoHarvestPairFacts facts) =>
        facts.Identity == AutoHarvestEvidenceState.Unknown &&
        facts.PlotVisibility == AutoHarvestEvidenceState.Unknown &&
        facts.ActionAvailability == AutoHarvestEvidenceState.Unknown &&
        facts.Prerequisites == AutoHarvestEvidenceState.Unknown &&
        facts.Readiness == AutoHarvestEvidenceState.Unknown &&
        facts.ActionSafety == AutoHarvestActionSafetyState.Unknown &&
        facts.NoDuplicate == AutoHarvestEvidenceState.Unknown &&
        facts.ActionSlotAvailability == AutoHarvestEvidenceState.Unknown;
}

internal readonly struct AutoHarvestCycleInputRecord : IServiceCycleReplayRecord
{
    public AutoHarvestCycleInputRecord(
        in AutoHarvestCycleFrame frame,
        in AutomataConfiguration config)
    {
        ValidateSelection(frame.Fruit.Kind, config.AutoHarvest.CollectFruitTrees, nameof(config.AutoHarvest.CollectFruitTrees));
        ValidateSelection(frame.Treasure.Kind, config.AutoHarvest.CollectTreasureTrees, nameof(config.AutoHarvest.CollectTreasureTrees));
        Fruit = AutoHarvestPairCaptureRecord.FromCapture(frame.Fruit);
        Treasure = AutoHarvestPairCaptureRecord.FromCapture(frame.Treasure);
        MasterEnabled = config.General.Enabled;
        EmergencyDisabled = config.Safety.EmergencyDisable;
        ActiveMode = config.AutoHarvest.Mode == AutoHarvestOperationMode.Active;
        FruitSelected = config.AutoHarvest.CollectFruitTrees;
        TreasureSelected = config.AutoHarvest.CollectTreasureTrees;
        OwnsActionFamily = frame.OwnsActionFamily;
        EvaluationIntervalTicks = config.AutoHarvest.EvaluationInterval.Ticks;
    }

    public AutoHarvestPairCaptureRecord Fruit { get; }
    public AutoHarvestPairCaptureRecord Treasure { get; }
    public bool MasterEnabled { get; }
    public bool EmergencyDisabled { get; }
    public bool ActiveMode { get; }
    public bool FruitSelected { get; }
    public bool TreasureSelected { get; }
    public bool OwnsActionFamily { get; }
    public long EvaluationIntervalTicks { get; }

    public AutoHarvestCycleFrame ToFrame()
    {
        var fruit = Fruit.ToCapture(AutoHarvestPair.FruitTree);
        var treasure = Treasure.ToCapture(AutoHarvestPair.TreasureTree);
        return new AutoHarvestCycleFrame(fruit, treasure, OwnsActionFamily);
    }

    public AutomataConfiguration ToConfiguration() => AutoHarvestConfigurationFactory.Create(
        MasterEnabled,
        EmergencyDisabled,
        ActiveMode,
        FruitSelected,
        TreasureSelected,
        new MonotonicDuration(EvaluationIntervalTicks));

    private static void ValidateSelection(
        AutoHarvestPairCaptureKind captureKind,
        bool selected,
        string parameterName)
    {
        if ((captureKind == AutoHarvestPairCaptureKind.NotSelected) != !selected)
            throw new ArgumentException(
                "Auto Harvest selection and pair capture evidence disagree.",
                parameterName);
    }
}
