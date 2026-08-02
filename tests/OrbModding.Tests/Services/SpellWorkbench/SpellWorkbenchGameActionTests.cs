using System;
using System.Linq;
using System.Threading.Tasks;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.SpellWorkbench;

public sealed class SpellWorkbenchGameActionTests : IDisposable
{
    private const long Epoch = 41;

    public SpellWorkbenchGameActionTests()
    {
        SpellRecipeSO.All.Clear();
        SpellManager.instance = new SpellManager();
    }

    [Fact]
    public void SelectAppliesTheAuthoredCoreSequenceAndClearsAugments()
    {
        var (recipe, first, second) = Recipe();
        SpellManager.instance!.selectedAugmentGlyphs.value.Add(new GlyphSO { augmentsSpells = true });
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new[] { first, second }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.Empty(SpellManager.instance.selectedAugmentGlyphs.value);
        Assert.Equal(recipe.GetGuid(), result.Evidence.After.ResolvedRecipeId);
    }

    [Fact]
    public void DiscoverUsesTheNativePipelineAndVerifiesTheTargetOutcome()
    {
        var (recipe, _, _) = Recipe();
        using var action = Action();
        Assert.True(action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Verified);

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch));

        Assert.True(result.Verified);
        Assert.True(recipe.discovered);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
        Assert.Single(SpellManager.instance.activeSpells.value);
        Assert.True(result.Evidence.After.TargetDiscovered);
    }

    [Fact]
    public void DiscoveryThrowAfterOutcomeStillCommitsWithoutQuarantine()
    {
        var (recipe, _, _) = Recipe();
        using var action = Action();
        Assert.True(action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Verified);
        SpellManager.instance!.ThrowAfterDiscovery = true;

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch));

        Assert.True(result.Verified);
        Assert.False(action.IsQuarantined);
    }

    [Fact]
    public void CreateVerifiesANewRuntimeInstanceOfTheExactRecipe()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var action = Action();
        Assert.True(action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Verified);

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Create, recipe.GetGuid(), Epoch));

        Assert.True(result.Verified);
        var spell = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Same(recipe, spell.get_reference());
        Assert.NotEqual(Guid.Empty, spell.guidContainer.guid);
    }

    [Fact]
    public void MissingCreateOutcomeQuarantinesTheFamilyForThisLifecycle()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var action = Action();
        Assert.True(action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Verified);
        SpellManager.instance!.SuppressCreation = true;

        var failed = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Create, recipe.GetGuid(), Epoch));
        var retry = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Create, recipe.GetGuid(), Epoch));

        Assert.Equal(SpellWorkbenchPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.Quarantined, retry.Preflight);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void EmptyRuntimeIdentityDoesNotVerifyCreation()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        using var action = Action();
        Assert.True(action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Verified);
        SpellManager.instance!.CreateEmptyIdentity = true;

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Create, recipe.GetGuid(), Epoch));

        Assert.Equal(SpellWorkbenchPreflight.VerificationFailed, result.Preflight);
        Assert.True(action.IsQuarantined);
    }

    [Theory]
    [InlineData(false, true, (int)SpellWorkbenchPreflight.DiscoveryUnavailable)]
    [InlineData(true, false, (int)SpellWorkbenchPreflight.RecipeUnavailable)]
    public void DiscoveryRevalidatesNativePrerequisitesBeforeMutation(
        bool canDiscover, bool creatable, int expected)
    {
        var (recipe, _, _) = Recipe();
        recipe.NativeCanDiscover = canDiscover;
        recipe.NativeIsCreatable = creatable;
        using var action = Action();
        Assert.True(action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Verified);

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch));

        Assert.Equal((SpellWorkbenchPreflight)expected, result.Preflight);
        Assert.False(recipe.discovered);
    }

    [Fact]
    public async Task OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        var (recipe, _, _) = Recipe();
        using var action = Action();

        var result = await Task.Run(() => action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)));

        Assert.Equal(SpellWorkbenchPreflight.WrongThread, result.Preflight);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        var (recipe, _, _) = Recipe();
        foreach (var missing in SpellWorkbenchNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch));
            Assert.Equal(SpellWorkbenchPreflight.ContractUnavailable, result.Preflight);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutChangingSelection()
    {
        var (recipe, _, _) = Recipe();
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(SpellWorkbenchPreflight.LifecycleReplaced,
            stale.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Preflight);
        Assert.Equal(SpellWorkbenchPreflight.MutationPermitUnavailable,
            unowned.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Select, recipe.GetGuid(), Epoch)).Preflight);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
    }

    private static (SpellRecipeSO Recipe, GlyphSO First, GlyphSO Second) Recipe(bool discovered = false)
    {
        var first = new GlyphSO { DisplayName = "Form", NativeAvailable = true };
        var second = new GlyphSO { DisplayName = "Bolt", NativeAvailable = true };
        var recipe = new SpellRecipeSO { discovered = discovered };
        recipe.coreRecipe.Add(first);
        recipe.coreRecipe.Add(second);
        SpellRecipeSO.All.Add(recipe);
        SpellManager.instance!.availableSpellRecipes.value.Add(recipe);
        return (recipe, first, second);
    }

    private static SpellWorkbenchGameAction Action(
        long epoch = Epoch,
        bool permit = true,
        Func<string, bool>? include = null)
    {
        var action = new SpellWorkbenchGameAction(
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
        SpellRecipeSO.All.Clear();
        SpellManager.instance = null;
    }
}
