using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestNativeStateReaderTests
{
    [Fact]
    public void FinalFreeActionEntryIsAvailableWhenNativeEntryEvidenceAgrees()
    {
        var state = State(emptyEntries: 1, nativeHasEmptyEntry: true);

        Assert.Equal(
            AutoHarvestEvidenceState.Verified,
            AutoHarvestNativeStateReader.ProjectActionSlotAvailability(state));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void MissingEnumeratedOrNativeEmptyEntryRejectsAvailability(
        int emptyEntries,
        bool nativeHasEmptyEntry)
    {
        var state = State(emptyEntries, nativeHasEmptyEntry);

        Assert.Equal(
            AutoHarvestEvidenceState.Rejected,
            AutoHarvestNativeStateReader.ProjectActionSlotAvailability(state));
    }

    [Fact]
    public void InvalidNativeStateLeavesAvailabilityUnknown()
    {
        Assert.Equal(
            AutoHarvestEvidenceState.Unknown,
            AutoHarvestNativeStateReader.ProjectActionSlotAvailability(
                AutoHarvestSubmissionState.Invalid));
    }

    [Fact]
    public void SharedActiveActionSnapshotProjectsBothPairsWithoutLosingCommonEvidence()
    {
        var fruit = new AutoHarvestActivePairState(matchCount: 1, quantity: 2, engaged: true);
        var treasure = new AutoHarvestActivePairState(matchCount: 2, quantity: 5, engaged: false);
        var snapshot = new AutoHarvestActiveActionSnapshot(
            true,
            usedEntryCount: 3,
            emptyEntryCount: 1,
            nativeHasEmptyEntry: true,
            supportedCollectCount: 3,
            fruit,
            treasure);

        var fruitState = snapshot.Project(AutoHarvestPair.FruitTree);
        var treasureState = snapshot.Project(AutoHarvestPair.TreasureTree);

        Assert.Equal(3, fruitState.SupportedCollectCount);
        Assert.Equal(1, fruitState.PairMatchCount);
        Assert.Equal(2, fruitState.PairQuantity);
        Assert.True(fruitState.PairEngaged);
        Assert.Equal(3, treasureState.SupportedCollectCount);
        Assert.Equal(2, treasureState.PairMatchCount);
        Assert.Equal(5, treasureState.PairQuantity);
        Assert.False(treasureState.PairEngaged);
        Assert.Equal(fruitState.UsedEntryCount, treasureState.UsedEntryCount);
        Assert.Equal(fruitState.EmptyEntryCount, treasureState.EmptyEntryCount);
    }

    private static AutoHarvestSubmissionState State(
        int emptyEntries,
        bool nativeHasEmptyEntry) =>
        new(
            isValid: true,
            usedEntryCount: 2,
            emptyEntryCount: emptyEntries,
            nativeHasEmptyEntry,
            supportedCollectCount: 0,
            pairMatchCount: 0,
            pairQuantity: 0,
            pairEngaged: false);
}
