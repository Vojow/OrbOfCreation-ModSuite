using System;
using System.Collections;
using System.Threading.Tasks;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.CraftingLifecycle;

public sealed class CraftingInstanceLifecycleGameActionTests : IDisposable
{
    private const long Epoch = 131;
    private readonly IDictionary _registry = new Hashtable();

    public CraftingInstanceLifecycleGameActionTests()
    {
        UnityEngine.Resources.Objects.Clear();
        GlobalVariables.MultiBuy = new IntVariable { Value = 3 };
    }

    public void Dispose() => UnityEngine.Resources.Objects.Clear();

    [Fact]
    public void Automate_uses_the_ui_increment_and_requires_a_larger_native_quantity()
    {
        var (recipe, page) = Surface();
        recipe.MultiBuyQuantityOverride = new BigDouble(7);
        var existing = new CraftingInstance(recipe, new BigDouble(2)).SetAuto(true);
        existing.SetAutomationQuantity(2);
        page.craftingAutomationInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.True(result.Verified, result.Reason);
        Assert.Same(existing, page.craftingAutomationInstances.GetInstance(recipe));
        Assert.Equal(5, existing.GetAutomationQuantity());
    }

    [Fact]
    public void Automated_cancel_uses_multi_buy_and_observes_the_game_written_decrease()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(5)).SetAuto(true);
        existing.SetAutomationQuantity(5);
        page.craftingAutomationInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(
            boundary, recipe, CraftingInstanceLifecycleActionKind.CancelAutomation);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(2, existing.GetAutomationQuantity());
        Assert.Single(page.craftingAutomationInstances.value);
    }

    [Fact]
    public void Manual_cancel_runs_cancel_then_removes_the_exact_instance()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(5));
        page.craftingQueueInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.CancelManual);

        Assert.True(result.Verified, result.Reason);
        Assert.True(existing.Removed);
        Assert.Empty(page.craftingQueueInstances.value);
    }

    [Fact]
    public void Native_automation_no_op_fails_the_directional_sentinel()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(2)).SetAuto(true);
        existing.SetAutomationQuantity(2);
        page.craftingAutomationInstances.value.Add(existing);
        page.craftingAutomationInstances.SuppressAutomation = true;
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.Equal(CraftingInstanceLifecyclePreflight.VerificationFailed, result.Preflight);
        Assert.Equal(2, existing.GetAutomationQuantity());
    }

    [Fact]
    public async Task Unity_thread_is_refused_before_native_state()
    {
        var (recipe, _) = Surface();
        using var boundary = Boundary(recipe);

        var result = await Task.Run(() =>
            Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate));

        Assert.Equal(CraftingInstanceLifecyclePreflight.WrongThread, result.Preflight);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_instance_family()
    {
        foreach (var missing in CraftingInstanceLifecycleNativeBindings.ContractIds)
        {
            using var boundary = Boundary(null, id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private CraftingInstanceLifecycleGameAction Boundary(
        CraftingRecipeSO? recipe,
        Func<string, bool>? includeContract = null)
    {
        if (recipe is not null) _registry.Add(recipe.GetGuid(), recipe);
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new CraftingInstanceLifecycleGameAction(
            () => Epoch,
            static () => true,
            static () => "Crafting ownership was revoked.",
            includeContract: includeContract,
            registry: resolver);
    }

    private static CraftingInstanceLifecycleSubmission Submit(
        CraftingInstanceLifecycleGameAction boundary,
        CraftingRecipeSO recipe,
        CraftingInstanceLifecycleActionKind kind)
    {
        var action = new CraftingInstanceLifecycleAction(kind, recipe.GetGuid(), Epoch);
        return boundary.Submit(in action);
    }

    private static (CraftingRecipeSO Recipe, UICraftingPage Page) Surface()
    {
        var recipe = new CraftingRecipeSO
        {
            uuid = Guid.NewGuid(),
            visible = true,
            MainType = new CraftingRecipeTypeSO(),
        };
        var page = new UICraftingPage
        {
            mainCraftType = recipe.MainType,
            craftingQueueInstances = new CraftingInstanceListVariable
            {
                uuid = Guid.NewGuid(),
                Maximum = 4,
            },
            craftingAutomationInstances = new CraftingInstanceListVariable
            {
                uuid = Guid.NewGuid(),
                Maximum = 4,
                isAutoList = true,
            },
        };
        page.availableRecipes.value.Add(recipe);
        UnityEngine.Resources.Objects.Add(page);
        return (recipe, page);
    }
}
