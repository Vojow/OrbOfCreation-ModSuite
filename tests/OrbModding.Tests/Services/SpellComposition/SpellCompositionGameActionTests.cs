using System;
using System.Linq;
using System.Threading.Tasks;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.SpellComposition;

public sealed class SpellCompositionGameActionTests : IDisposable
{
    private const long Epoch = 73;

    public SpellCompositionGameActionTests()
    {
        GlyphSO.All.Clear();
        SpellManager.instance = new SpellManager();
        Player.Current = new Player();
        Player.GetSpellOutputLevel().Value = 2;
        Player.Current.maxSpellOutputLevel.Value = 12;
    }

    [Fact]
    public void OutputLevelCommitsTheExactGlobalSelectorOutcome()
    {
        using var action = Action();

        var result = action.Submit(Output(7));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(7, Player.GetSpellOutputLevel().AsInt());
        Assert.Equal(2, result.Evidence.Before.OutputLevel);
        Assert.Equal(7, result.Evidence.After.OutputLevel);
        Assert.Equal(12, result.Evidence.After.MaximumOutputLevel);
    }

    [Fact]
    public void OutputSetterThrowAfterOutcomeStillCommitsWithoutQuarantine()
    {
        Player.GetSpellOutputLevel().ThrowAfterWriteFor = 5;
        using var action = Action();

        var result = action.Submit(Output(5));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(5, Player.GetSpellOutputLevel().AsInt());
    }

    [Fact]
    public void OutputLevelRangeAndNoOpRefuseBeforeMutation()
    {
        using var action = Action();

        var range = action.Submit(Output(13));
        var noOp = action.Submit(Output(2));

        Assert.Equal(SpellCompositionPreflight.OutputLevelOutOfRange, range.Preflight);
        Assert.Equal(SpellCompositionPreflight.AlreadyInRequestedState, noOp.Preflight);
        Assert.Equal(0, Player.GetSpellOutputLevel().SetCalls);
    }

    [Fact]
    public void AugmentCompositionCommitsExactNamedRuntimeTargetAndStacks()
    {
        var (spell, first, second) = SpellFixture(recipeMastery: 9);
        first.masteryReqCount = 3;
        second.masteryReqCount = 8;
        using var action = Action();

        var result = action.Submit(Augments(
            spell,
            new SpellCompositionGlyphStack(second.GetGuid(), 1),
            new SpellCompositionGlyphStack(first.GetGuid(), 2)));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(spell.guidContainer.guid, result.Evidence.After.SpellInstanceId);
        Assert.Equal(spell.get_reference()!.GetGuid(), result.Evidence.After.SpellRecipeId);
        Assert.Equal(2, spell.GetQuantityOfGlyph(first));
        Assert.Equal(1, spell.GetQuantityOfGlyph(second));
        Assert.Equal(2, result.Evidence.After.AugmentGlyphs.Length);
    }

    [Fact]
    public void EmptyAugmentListClearsTheComposition()
    {
        var (spell, first, _) = SpellFixture();
        var initial = new Stacked.StackedIdRecord<GlyphSO>();
        initial.Set(first, 2);
        spell.SetAugmentGlyphs(initial);
        using var action = Action();

        var result = action.Submit(Augments(spell));

        Assert.True(result.Verified, result.Reason);
        Assert.Empty(spell.GetAugmentGlyphs());
        Assert.Empty(result.Evidence.After.AugmentGlyphs);
    }

    [Fact]
    public void AugmentSetterThrowAfterOutcomeStillCommitsWithoutQuarantine()
    {
        var (spell, glyph, _) = SpellFixture();
        spell.ThrowAfterAugmentMutation = true;
        using var action = Action();

        var result = action.Submit(Augments(
            spell,
            new SpellCompositionGlyphStack(glyph.GetGuid(), 1)));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, spell.GetQuantityOfGlyph(glyph));
    }

    [Theory]
    [InlineData("duplicate", (int)SpellCompositionPreflight.DuplicateGlyph)]
    [InlineData("missing", (int)SpellCompositionPreflight.GlyphIdentityUnavailable)]
    [InlineData("unavailable", (int)SpellCompositionPreflight.GlyphUnavailable)]
    [InlineData("core", (int)SpellCompositionPreflight.NotAnAugment)]
    [InlineData("over_maximum", (int)SpellCompositionPreflight.UsageLimitExceeded)]
    [InlineData("incompatible", (int)SpellCompositionPreflight.IncompatibleComposition)]
    [InlineData("mastery", (int)SpellCompositionPreflight.MasteryRequirementUnmet)]
    public void AugmentPreconditionsRefuseBeforeNativeMutation(string scenario, int expected)
    {
        var (spell, glyph, _) = SpellFixture(recipeMastery: 4);
        var requestedId = glyph.GetGuid();
        var requestedCount = 1;
        var rows = new[] { new SpellCompositionGlyphStack(requestedId, requestedCount) };
        switch (scenario)
        {
            case "duplicate":
                rows = new[]
                {
                    new SpellCompositionGlyphStack(requestedId, 1),
                    new SpellCompositionGlyphStack(requestedId, 1),
                };
                break;
            case "missing":
                rows = new[] { new SpellCompositionGlyphStack(Guid.NewGuid(), 1) };
                break;
            case "unavailable":
                glyph.NativeAvailable = false;
                break;
            case "core":
                glyph.augmentsSpells = false;
                break;
            case "over_maximum":
                rows = new[] { new SpellCompositionGlyphStack(requestedId, 4) };
                break;
            case "incompatible":
                glyph.requiresDuration = true;
                break;
            case "mastery":
                glyph.masteryReqCount = 5;
                break;
        }
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(
            SpellCompositionActionKind.SetAugments,
            spell.guidContainer.guid,
            0,
            rows,
            Epoch));

        Assert.Equal((SpellCompositionPreflight)expected, result.Preflight);
        Assert.Equal(0, spell.SetAugmentCalls);
    }

    [Fact]
    public void WrongRuntimeTargetRefusesWithoutNativeMutation()
    {
        var (_, glyph, _) = SpellFixture();
        using var action = Action();

        var result = action.Submit(new SpellCompositionAction(
            SpellCompositionActionKind.SetAugments,
            Guid.NewGuid(),
            0,
            new[] { new SpellCompositionGlyphStack(glyph.GetGuid(), 1) },
            Epoch));

        Assert.Equal(SpellCompositionPreflight.IdentityUnavailable, result.Preflight);
        Assert.Equal(0, SpellManager.instance!.activeSpells.value[0].SetAugmentCalls);
    }

    [Fact]
    public void MissingRequestedOutcomeFaultsThisAttemptAndTheNextCallRevalidates()
    {
        var (spell, glyph, _) = SpellFixture();
        spell.SuppressAugmentMutation = true;
        using var action = Action();

        var failed = action.Submit(Augments(
            spell,
            new SpellCompositionGlyphStack(glyph.GetGuid(), 1)));
        var retry = action.Submit(Augments(
            spell,
            new SpellCompositionGlyphStack(glyph.GetGuid(), 1)));

        Assert.Equal(SpellCompositionPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(SpellCompositionPreflight.VerificationFailed, retry.Preflight);
        Assert.True(failed.Evidence.Available);
        Assert.Empty(failed.Evidence.After.AugmentGlyphs);
    }

    [Fact]
    public async Task OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        using var action = Action();

        var result = await Task.Run(() => action.Submit(Output(4)));

        Assert.Equal(SpellCompositionPreflight.WrongThread, result.Preflight);
        Assert.Equal(2, Player.GetSpellOutputLevel().AsInt());
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        foreach (var missing in SpellCompositionNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(Output(4));
            Assert.Equal(SpellCompositionPreflight.ContractUnavailable, result.Preflight);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutMutation()
    {
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(
            SpellCompositionPreflight.LifecycleReplaced,
            stale.Submit(Output(4)).Preflight);
        Assert.Equal(
            SpellCompositionPreflight.MutationPermitUnavailable,
            unowned.Submit(Output(4)).Preflight);
        Assert.Equal(2, Player.GetSpellOutputLevel().AsInt());
    }

    private static SpellCompositionAction Output(int level) => new(
        SpellCompositionActionKind.SetOutputLevel,
        Guid.Empty,
        level,
        Array.Empty<SpellCompositionGlyphStack>(),
        Epoch);

    private static SpellCompositionAction Augments(
        Spell spell,
        params SpellCompositionGlyphStack[] glyphs) => new(
            SpellCompositionActionKind.SetAugments,
            spell.guidContainer.guid,
            0,
            glyphs,
            Epoch);

    private static (Spell Spell, GlyphSO First, GlyphSO Second) SpellFixture(
        int recipeMastery = 10)
    {
        var recipe = new SpellRecipeSO
        {
            uuid = Guid.NewGuid().ToString("D"),
            discovered = true,
            masteryLevel = recipeMastery,
        };
        var first = Glyph("Focus", maximum: 3);
        var second = Glyph("Echo", maximum: 2);
        var spell = new Spell(recipe)
        {
            DisplayName = "Test Spell",
            DurationSpell = false,
            ToggledSpell = false,
        };
        SpellManager.instance!.activeSpells.value.Add(spell);
        return (spell, first, second);
    }

    private static GlyphSO Glyph(string name, int maximum)
    {
        var glyph = new GlyphSO
        {
            DisplayName = name,
            NativeAvailable = true,
            augmentsSpells = true,
            maxUsages = new ValueModifierRecord(new BigDouble(maximum, 0)),
        };
        glyph.SetGuid(Guid.NewGuid());
        GlyphSO.All.Add(glyph);
        return glyph;
    }

    private static SpellCompositionGameAction Action(
        long epoch = Epoch,
        bool permit = true,
        Func<string, bool>? include = null)
    {
        var action = new SpellCompositionGameAction(
            () => epoch,
            () => permit,
            () => "test ownership unavailable",
            name => typeof(SpellManager).Assembly.GetTypes()
                .FirstOrDefault(type => type.Name == name || type.FullName == name),
            include ?? (_ => true));
        if (include is null) Assert.True(action.BindingsAvailable, action.BindingFailure);
        return action;
    }

    public void Dispose()
    {
        GlyphSO.All.Clear();
        SpellManager.instance = null;
        Player.Current = new Player();
    }
}
