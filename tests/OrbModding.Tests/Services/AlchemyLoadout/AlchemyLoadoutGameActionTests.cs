using System;
using System.Collections;
using System.Threading.Tasks;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AlchemyLoadout;

public sealed class AlchemyLoadoutGameActionTests : IDisposable
{
    private const long Epoch = 97;
    private readonly IDictionary _registry = new Hashtable();

    public AlchemyLoadoutGameActionTests()
    {
        AlchemyManager.instance = new AlchemyManager();
        GlobalVariables.MultiBuy = new IntVariable { Value = 2 };
        RegisterConceptCatalog();
    }

    public void Dispose()
    {
        AlchemyManager.instance = null;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
    }

    [Fact]
    public void Add_and_remove_use_the_native_multi_buy_click_and_directional_target_sentinel()
    {
        var recipe = OrdinaryRecipe(5);
        Register(recipe);
        AlchemyManager.instance!.allAlchemy.value.Add(recipe);
        using var boundary = Boundary();

        var added = Submit(boundary, recipe, AlchemyLoadoutActionKind.Add);
        var instance = Assert.Single(AlchemyManager.instance.activeAlchemy.value);
        var addedAmount = instance.GetQueuedQuantity();
        var removed = Submit(boundary, recipe, AlchemyLoadoutActionKind.Remove);

        Assert.True(added.Verified, added.Reason);
        Assert.Equal(2, addedAmount);
        Assert.True(removed.Verified, removed.Reason);
        Assert.Empty(AlchemyManager.instance.activeAlchemy.value);
    }

    [Fact]
    public void Move_uses_the_ui_swap_and_observable_route()
    {
        var first = OrdinaryRecipe(5);
        var second = OrdinaryRecipe(5);
        Register(first);
        AlchemyManager.instance!.activeAlchemy.value.Add(new AlchemyInstance(first) { queuedQuantity = 1 });
        AlchemyManager.instance.activeAlchemy.value.Add(new AlchemyInstance(second) { queuedQuantity = 1 });
        using var boundary = Boundary();

        var result = Submit(boundary, first, AlchemyLoadoutActionKind.Move, destination: 1);

        Assert.True(result.Verified, result.Reason);
        Assert.Same(first, AlchemyManager.instance.activeAlchemy.value[1].get_reference());
        Assert.Equal(1, AlchemyManager.instance.activeAlchemy.UpdateObservableCalls);
    }

    [Fact]
    public void Concept_recipe_is_refused_by_the_shared_domain_classifier()
    {
        var concept = ConceptRecipe();
        Register(concept);
        using var boundary = Boundary();

        var result = Submit(boundary, concept, AlchemyLoadoutActionKind.Add);

        Assert.Equal(AlchemyLoadoutPreflight.WrongDomain, result.Preflight);
        Assert.Empty(AlchemyManager.instance!.activeAlchemy.value);
    }

    [Fact]
    public void Missing_native_transition_fails_the_one_outcome_sentinel()
    {
        var recipe = OrdinaryRecipe(5);
        Register(recipe);
        AlchemyManager.instance!.activeAlchemy.SuppressAddMutation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, AlchemyLoadoutActionKind.Add);

        Assert.Equal(AlchemyLoadoutPreflight.VerificationFailed, result.Preflight);
        Assert.Empty(AlchemyManager.instance.activeAlchemy.value);
    }

    [Fact]
    public async Task Unity_thread_is_refused_before_identity_or_native_state()
    {
        var recipe = OrdinaryRecipe(5);
        Register(recipe);
        using var boundary = Boundary();

        var result = await Task.Run(() => Submit(boundary, recipe, AlchemyLoadoutActionKind.Add));

        Assert.Equal(AlchemyLoadoutPreflight.WrongThread, result.Preflight);
        Assert.Empty(AlchemyManager.instance!.activeAlchemy.value);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in AlchemyLoadoutNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private AlchemyLoadoutGameAction Boundary(Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new AlchemyLoadoutGameAction(() => Epoch, static () => true,
            static () => "AlchemyLoadout ownership was revoked.",
            includeContract: includeContract, registry: resolver,
            classifier: new AlchemyGameplayDomainClassifier(resolver));
    }

    private static AlchemyLoadoutSubmission Submit(AlchemyLoadoutGameAction boundary,
        AlchemyRecipeSO recipe, AlchemyLoadoutActionKind kind, int destination = -1)
    {
        var action = new AlchemyLoadoutAction(kind, recipe.GetGuid(), destination, Epoch);
        return boundary.Submit(in action);
    }

    private void RegisterConceptCatalog()
    {
        var list = new AlchemyRecipeListVariable();
        list.SetGuid(KnownEntities.ConceptRecipes.Uuid);
        list.value.Add(ConceptRecipe());
        _registry.Add(KnownEntities.ConceptRecipes.Uuid, list);
    }

    private void Register(AlchemyRecipeSO recipe) => _registry.Add(recipe.GetGuid(), recipe);

    private static AlchemyRecipeSO OrdinaryRecipe(int maximum)
    {
        var type = new AlchemyTypeSO(AlchemyGameplayDomainClassifier.AlchemyTypeUuid.ToString("D"));
        var recipe = new AlchemyRecipeSO(Guid.NewGuid().ToString("D"), "Ordinary Alchemy", new[] { type })
        {
            coreType = type,
            discovered = true,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(maximum)),
            freeUsageSlots = new ValueModifierRecord(new BigDouble(1)),
        };
        return recipe;
    }

    private static AlchemyRecipeSO ConceptRecipe()
    {
        var type = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString("D"));
        return new AlchemyRecipeSO(Guid.NewGuid().ToString("D"), "Concept", new[] { type })
        {
            coreType = type,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(5)),
        };
    }
}
