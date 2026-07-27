using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins that a record is read the way the game reads it: its memo while the game will not recompute,
/// its base value and two modifier sets when the game will. Getting either half alone shipped a live
/// defect, in opposite directions and at opposite ends of the economy.
/// </summary>
public sealed class NativeModifierRecordAccessTests
{
    private static NativeModifierRecordAccess Access() =>
        NativeModifierRecordAccess.For(typeof(ValueModifierRecord))
            ?? throw new InvalidOperationException("the stub record did not bind");

    private static ValueModifier Modifier(
        ValueModifier.ValueModifierType type,
        BigDouble amount,
        int order = 0) =>
        new(type, amount, order);

    /// <summary>
    /// Adds a modifier and dirties the record, because the game does both together. A record carrying
    /// a modifier without a dirty flag is a shape the game cannot produce, and a test that built one
    /// would be pinning behaviour against a world that does not exist.
    /// </summary>
    private static void Add(
        ValueModifierRecord record,
        System.Collections.Generic.Dictionary<Guid, ValueModifier> set,
        ValueModifier modifier)
    {
        set[Guid.NewGuid()] = modifier;
        record.Dirty();
    }

    [Fact]
    public void ARecordWithNoModifiersReadsAsItsBaseValue()
    {
        var record = new ValueModifierRecord(new BigDouble(42d));

        Assert.Equal(42d, Access().Fold(record).ToDouble(), 9);
    }

    [Fact]
    public void ANullRecordReadsAsTheDefaultRatherThanThrowing()
    {
        Assert.Equal(BigDouble.Zero, Access().Fold(null));
    }

    /// <summary>
    /// <c>StructureSO.passiveCostMod</c>, and the whole reason this changed. The record carries no
    /// modifiers, so nothing ever dirties it, so the game never runs <c>Calculate()</c> and
    /// <c>GetValue()</c> keeps answering the <c>[NonSerialized]</c> zero for the rest of the session.
    /// A recomputation says 100. The two sit on opposite sides of the <c>Max</c> in
    /// <c>GetNextCostMod()</c>, and reading 100 published every structure at the game's price times
    /// 1.25 to the power of its owned levels — which is 1e133 on a structure with 1375 of them.
    /// </summary>
    [Fact]
    public void APermanentlyUncalculatedRecordReadsAsTheZeroTheGameCharges()
    {
        var record = new ValueModifierRecord(new BigDouble(100d)).WithMemo(BigDouble.Zero);

        Assert.False(record.IsCalculationDirty);
        Assert.Equal(BigDouble.Zero, record.GetValue());
        Assert.Equal(BigDouble.Zero, Access().Fold(record));
    }

    /// <summary>
    /// The cycle-1 protection, and why the memo alone was never the answer either. A save brings every
    /// record back with its memo at zero; one that is <em>dirty</em> is one the game will recompute
    /// the moment anything asks it, so the number to publish is the recomputation. Reading the zero
    /// here is what priced 180 structures at nothing on the first cold collection.
    /// </summary>
    [Fact]
    public void APostLoadDirtyRecordReadsAsItsRecomputationRatherThanItsEmptyMemo()
    {
        var record = new ValueModifierRecord(new BigDouble(250d)).WithMemo(BigDouble.Zero).Dirty();

        Assert.Equal(BigDouble.Zero, record.GetValue());
        Assert.Equal(250d, Access().Fold(record).ToDouble(), 9);
    }

    /// <summary>
    /// A clean record whose memo has drifted from what a recomputation would give. The memo wins,
    /// because nothing will replace it: the game is going to spend that number, and a snapshot that
    /// published the truer one would be planning against an economy that does not exist.
    /// </summary>
    [Fact]
    public void AMemoTheGameWillNotReplaceWinsOverAFreshRecomputation()
    {
        var record = new ValueModifierRecord(new BigDouble(100d)).WithMemo(new BigDouble(7d));

        Assert.Equal(7d, Access().Fold(record).ToDouble(), 9);
    }

    /// <summary>
    /// A dirty record ignores its memo entirely and answers the arithmetic, however far apart the two
    /// are.
    /// </summary>
    [Fact]
    public void ADirtyRecordAnswersTheArithmeticAndNotTheMemo()
    {
        var record = new ValueModifierRecord(new BigDouble(100d)).WithMemo(new BigDouble(999d));
        Add(record, record.passiveModifiers, Modifier(ValueModifier.ValueModifierType.Raw, new BigDouble(50d)));

        Assert.Equal(150d, Access().Fold(record).ToDouble(), 9);
    }

    /// <summary>
    /// The two sets are one stack, exactly as <c>GetAllModifiers()</c> concatenates them: a passive
    /// and an active modifier of the same type and order merge with each other rather than
    /// compounding.
    /// </summary>
    [Fact]
    public void PassiveAndActiveModifiersFoldAsOneStack()
    {
        var record = new ValueModifierRecord(new BigDouble(100d));
        Add(record, record.passiveModifiers, Modifier(ValueModifier.ValueModifierType.MultiDiminishing, new BigDouble(0.5d)));
        Add(record, record.activeModifiers, Modifier(ValueModifier.ValueModifierType.MultiDiminishing, new BigDouble(0.5d)));

        // Merged: 100 * (1 + 0.5 + 0.5) = 200. Compounded it would be 225.
        Assert.Equal(200d, Access().Fold(record).ToDouble(), 9);
    }

    [Fact]
    public void OrderGroupsApplyLowestFirst()
    {
        var record = new ValueModifierRecord(new BigDouble(10d));
        Add(record, record.activeModifiers, Modifier(ValueModifier.ValueModifierType.MultiStacking, new BigDouble(2d), order: 1));
        Add(record, record.passiveModifiers, Modifier(ValueModifier.ValueModifierType.Raw, new BigDouble(5d), order: 0));

        // (10 + 5) * 2 = 30, not 10 * 2 + 5 = 25.
        Assert.Equal(30d, Access().Fold(record).ToDouble(), 9);
    }

    [Fact]
    public void MagnitudesBeyondDoubleRangeSurviveTheRead()
    {
        var record = new ValueModifierRecord(new BigDouble(2d));
        Add(record, record.activeModifiers, Modifier(ValueModifier.ValueModifierType.MultiStacking, BigDouble.Pow10(300L)));

        var folded = Access().Fold(record);

        Assert.Equal(300L, folded.Exponent);
        Assert.Equal(2d, folded.Mantissa, 6);
    }

    /// <summary>
    /// More modifiers than the staging buffer starts with, so growth is exercised rather than
    /// assumed — and read twice, because the buffer is reused across records.
    /// </summary>
    [Fact]
    public void TheStagingBufferGrowsAndIsReusable()
    {
        var record = new ValueModifierRecord(new BigDouble(0d));
        for (var index = 0; index < 40; index++)
        {
            Add(record, record.passiveModifiers, Modifier(ValueModifier.ValueModifierType.Raw, new BigDouble(1d)));
        }

        var access = Access();
        Assert.Equal(40d, access.Fold(record).ToDouble(), 9);
        Assert.Equal(40d, access.Fold(record).ToDouble(), 9);
        Assert.Equal(7d, access.Fold(new ValueModifierRecord(new BigDouble(7d))).ToDouble(), 9);
    }

    [Fact]
    public void ATypeWithoutTheRecordsMembersDoesNotBind()
    {
        Assert.Null(NativeModifierRecordAccess.For(typeof(string)));
        Assert.Null(NativeModifierRecordAccess.For(null));
    }

    [Fact]
    public void AFullyBoundRecordReportsNoDegradation()
    {
        Assert.Equal(string.Empty, Access().Degradation);
    }
}
