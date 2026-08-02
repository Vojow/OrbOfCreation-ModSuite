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
        IdScriptableObject.RuntimeLookup.Clear();
        SpellManager.instance = new SpellManager();
    }

    [Fact]
    public void DiscoverUsesTheNativePipelineAndVerifiesTheTargetOutcome()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.Verified);
        Assert.True(recipe.discovered);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
        Assert.Single(SpellManager.instance.activeSpells.value);
        Assert.True(result.Evidence.After.TargetDiscovered);
    }

    [Fact]
    public void DiscoveryResolvesTheExactSubmittedCoreCompositionBeforePayment()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover,
            recipe.GetGuid(),
            Epoch,
            new[]
            {
                new SpellWorkbenchGlyphStack(first.GetGuid(), 1),
                new SpellWorkbenchGlyphStack(second.GetGuid(), 1),
            },
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.Verified, result.Reason);
        Assert.True(recipe.discovered);
        Assert.True(result.Evidence.After.TargetDiscovered);
    }

    [Fact]
    public void DiscoveryMismatchRefusesWithoutDirtyingTheExistingUiSelection()
    {
        var (recipe, first, second) = Recipe();
        SpellManager.instance!.selectedCoreGlyphs.value.Add(first);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover,
            recipe.GetGuid(),
            Epoch,
            new[]
            {
                new SpellWorkbenchGlyphStack(second.GetGuid(), 1),
                new SpellWorkbenchGlyphStack(first.GetGuid(), 1),
            },
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.WrongSelection, result.Preflight);
        Assert.Equal(new[] { first }, SpellManager.instance.selectedCoreGlyphs.value);
        Assert.False(recipe.discovered);
    }

    [Fact]
    public void LoadoutAddBakesTheExactGlyphLayoutAndPaysOnlyAfterAdmission()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        recipe.NativeUsageRequirementsMet = false;
        var usageResource = new ResourceSO { quantity = new BigDouble(10) };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, new BigDouble(3)));
        var paymentResource = new ResourceSO { quantity = new BigDouble(10) };
        var createCost = new ResourceCostList();
        createCost.costs.Add(new ResourceTuple(paymentResource, new BigDouble(2)));
        SpellManager.instance!.CreateCostOverride = createCost;
        var augment = new GlyphSO
        {
            DisplayName = "Bright",
            NativeAvailable = true,
            augmentsSpells = true,
            maxUsages = new ValueModifierRecord(new BigDouble(2)),
        };
        IdScriptableObject.RuntimeLookup[augment.GetGuid()] = augment;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(),
            Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 2) }));

        Assert.True(result.Verified, result.Reason);
        var equipped = Assert.Single(SpellManager.instance!.activeSpells.value);
        Assert.Equal(new[] { augment, augment }, equipped.GetAugmentGlyphs());
        Assert.Single(result.Evidence.After.MatchingLayoutSpellInstanceIds);
        Assert.Equal(new[] { augment, augment }, SpellManager.instance.selectedAugmentGlyphs.value);
        Assert.Equal(1, createCost.PerformCalls);
        Assert.Equal(8d, paymentResource.GetTrueQuantity().ToDouble());
        Assert.Equal(10d, usageResource.GetTrueQuantity().ToDouble());
    }

    [Fact]
    public void LoadoutAddAggregatesDuplicateGlyphRowsAgainstTheLiveUsableCount()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var augment = Augment(maximum: 2);
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[]
            {
                new SpellWorkbenchGlyphStack(augment.GetGuid(), 2),
                new SpellWorkbenchGlyphStack(augment.GetGuid(), 1),
            }));

        Assert.Equal(SpellWorkbenchPreflight.SelectionUnavailable, result.Preflight);
        Assert.Contains("Requested 3 uses", result.Reason);
        Assert.Empty(SpellManager.instance!.activeSpells.value);
    }

    [Fact]
    public void LoadoutAddRefusesUnmetGlyphAndRecipeRequirementsBeforePayment()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        recipe.NativeUsageRequirementsMet = false;
        var augment = Augment();
        augment.requiresDuration = true;
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        using var action = Action();

        var glyphResult = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            new[] { new SpellWorkbenchGlyphStack(augment.GetGuid(), 1) }));
        var recipeResult = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout,
            recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(),
            Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.GlyphRequirementsUnavailable, glyphResult.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UsageRequirementsUnavailable, recipeResult.Preflight);
        Assert.Equal(0, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void LoadoutAddChecksUsageBudgetAndUniqueCompatibilityBeforePayment()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var usageResource = new ResourceSO { quantity = BigDouble.Zero };
        recipe.baseUsageCost.costs.Add(new ResourceTuple(usageResource, BigDouble.One));
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        using var action = Action();

        var budget = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));
        recipe.baseUsageCost = new ResourceCostList();
        recipe.NativeUniqueSpell = true;
        SpellManager.instance.activeSpells.value.Add(recipe.CreateEmpty(0));
        var unique = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.UsageUnaffordable, budget.Preflight);
        Assert.Equal(SpellWorkbenchPreflight.UniqueSpellConflict, unique.Preflight);
        Assert.Equal(0, payment.PerformCalls);
    }

    [Fact]
    public void LoadoutAddFaultsWhenPaymentRunsWithoutTheExactRequestedOutcome()
    {
        var (recipe, _, _) = Recipe(discovered: true);
        var payment = new ResourceCostList();
        SpellManager.instance!.CreateCostOverride = payment;
        SpellManager.instance.SuppressCreation = true;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.CreateWithLayout, recipe.GetGuid(), Epoch,
            Array.Empty<SpellWorkbenchGlyphStack>(), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal(SpellWorkbenchPreflight.VerificationFailed, result.Preflight);
        Assert.True(result.Evidence.PaymentInvoked);
        Assert.Equal(1, payment.PerformCalls);
        Assert.Empty(SpellManager.instance.activeSpells.value);
    }

    [Fact]
    public void DiscoveryThrowAfterOutcomeStillCommitsWithoutQuarantine()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();
        SpellManager.instance!.ThrowAfterDiscovery = true;

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.True(result.Verified);
    }

    [Theory]
    [InlineData(false, true, (int)SpellWorkbenchPreflight.DiscoveryUnavailable)]
    [InlineData(true, false, (int)SpellWorkbenchPreflight.RecipeUnavailable)]
    public void DiscoveryRevalidatesNativePrerequisitesBeforeMutation(
        bool canDiscover, bool creatable, int expected)
    {
        var (recipe, first, second) = Recipe();
        recipe.NativeCanDiscover = canDiscover;
        recipe.NativeIsCreatable = creatable;
        using var action = Action();

        var result = action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));

        Assert.Equal((SpellWorkbenchPreflight)expected, result.Preflight);
        Assert.False(recipe.discovered);
    }

    [Fact]
    public async Task OffThreadSubmissionRefusesBeforeNativeExecution()
    {
        var (recipe, first, second) = Recipe();
        using var action = Action();

        var result = await Task.Run(() => action.Submit(new SpellWorkbenchAction(
            SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
            CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>())));

        Assert.Equal(SpellWorkbenchPreflight.WrongThread, result.Preflight);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
    }

    [Fact]
    public void EveryMissingLifecycleBindingFailsClosed()
    {
        var (recipe, first, second) = Recipe();
        foreach (var missing in SpellWorkbenchNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
                CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>()));
            Assert.Equal(SpellWorkbenchPreflight.ContractUnavailable, result.Preflight);
        }
    }

    [Fact]
    public void StaleLifecycleAndMissingPermitRefuseWithoutChangingSelection()
    {
        var (recipe, first, second) = Recipe();
        using var stale = Action(epoch: Epoch + 1);
        using var unowned = Action(permit: false);

        Assert.Equal(SpellWorkbenchPreflight.LifecycleReplaced,
            stale.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
                CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>())).Preflight);
        Assert.Equal(SpellWorkbenchPreflight.MutationPermitUnavailable,
            unowned.Submit(new SpellWorkbenchAction(
                SpellWorkbenchActionKind.Discover, recipe.GetGuid(), Epoch,
                CoreLayout(first, second), Array.Empty<SpellWorkbenchGlyphStack>())).Preflight);
        Assert.Empty(SpellManager.instance!.selectedCoreGlyphs.value);
    }

    private static (SpellRecipeSO Recipe, GlyphSO First, GlyphSO Second) Recipe(bool discovered = false)
    {
        var first = new GlyphSO
        {
            DisplayName = "Form",
            NativeAvailable = true,
            maxUsages = new ValueModifierRecord(new BigDouble(4)),
        };
        var second = new GlyphSO
        {
            DisplayName = "Bolt",
            NativeAvailable = true,
            maxUsages = new ValueModifierRecord(new BigDouble(4)),
        };
        var recipe = new SpellRecipeSO { discovered = discovered };
        recipe.coreRecipe.Add(first);
        recipe.coreRecipe.Add(second);
        SpellRecipeSO.All.Add(recipe);
        SpellManager.instance!.availableSpellRecipes.value.Add(recipe);
        IdScriptableObject.RuntimeLookup[first.GetGuid()] = first;
        IdScriptableObject.RuntimeLookup[second.GetGuid()] = second;
        return (recipe, first, second);
    }

    private static SpellWorkbenchGlyphStack[] CoreLayout(GlyphSO first, GlyphSO second) =>
        new[]
        {
            new SpellWorkbenchGlyphStack(first.GetGuid(), 1),
            new SpellWorkbenchGlyphStack(second.GetGuid(), 1),
        };

    private static GlyphSO Augment(int maximum = 4)
    {
        var augment = new GlyphSO
        {
            DisplayName = "Bright",
            NativeAvailable = true,
            augmentsSpells = true,
            maxUsages = new ValueModifierRecord(new BigDouble(maximum)),
        };
        IdScriptableObject.RuntimeLookup[augment.GetGuid()] = augment;
        return augment;
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
        IdScriptableObject.RuntimeLookup.Clear();
        SpellManager.instance = null;
    }
}
