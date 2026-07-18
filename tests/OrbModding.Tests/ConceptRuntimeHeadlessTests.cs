using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ConceptRuntimeHeadlessTests : IDisposable
{
    public ConceptRuntimeHeadlessTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    [Theory]
    [Trait("Category", "HeadlessIntegration")]
    [InlineData("ordinary-alchemy")]
    [InlineData("wrong-scholar-type")]
    public void ReflectionConceptRuntime_RejectsRecipeListsOutsideTheAuditedConceptDomain(string typeUuid)
    {
        var recipe = new AlchemyRecipeSO("invalid", "Invalid", new[] { new AlchemyTypeSO(typeUuid) });
        InstallNativeLists(recipe);
        using var runtime = new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier());

        var initialized = runtime.TryInitialize(out var reason);

        Assert.False(initialized);
        Assert.Contains("without verified Scholar type evidence", reason);
        Assert.False(runtime.IsReady);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ReflectionConceptRuntime_BatchedDepthClampsToLiveMasteryCap()
    {
        var resource = new ConceptResource();
        var recipe = new AlchemyRecipeSO(
            "valid-concept",
            "Valid concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = 2,
            drainCost = new ConceptCostVector(
                new ConceptCostEntry(resource, new BigDouble(10.0, 0))),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier());
        var candidates = runtime.ReadCandidates(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out var readReason);

        Assert.Equal(string.Empty, readReason);
        var candidate = Assert.Single(candidates);
        Assert.True(runtime.TryFindSafeTarget(
            candidate,
            desiredTarget: 20,
            rateReservePercent: 10.0f,
            minimumResourcePercent: 10.0f,
            out var safeTarget,
            out var projectionReason), projectionReason);
        Assert.Equal(2, safeTarget);
        Assert.False(runtime.TryAdd(candidate, 3, out _));
        Assert.True(runtime.TryAdd(candidate, 2, out var addReason), addReason);
        Assert.Equal(2, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ReflectionConceptRuntime_PositiveDrainFailsClosedWhenNativeResourceIsAtZero()
    {
        var resource = new ConceptResource { AtZero = true };
        var recipe = new AlchemyRecipeSO(
            "zero-resource-concept",
            "Zero resource concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = 4,
            drainCost = new ConceptCostVector(
                new ConceptCostEntry(resource, new BigDouble(1.0, 0))),
        };
        InstallNativeLists(recipe);
        using var runtime = new ReflectionConceptRuntime(new AlchemyGameplayDomainClassifier());
        var candidate = Assert.Single(runtime.ReadCandidates(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            out _));

        Assert.False(runtime.TryFindSafeTarget(
            candidate,
            desiredTarget: 4,
            rateReservePercent: 0.0f,
            minimumResourcePercent: 0.0f,
            out _,
            out var reason));
        Assert.Contains("at zero", reason);
    }

    private static AlchemyInstanceListVariable InstallNativeLists(params AlchemyRecipeSO[] recipes)
    {
        var active = new AlchemyInstanceListVariable();
        var recipeList = new AlchemyRecipeListVariable { value = recipes.ToList() };
        recipeList.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        IdScriptableObject.RuntimeLookup[new Guid(ReflectionConceptRuntime.ActiveConceptsUuid)] = active;
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = recipeList;
        return active;
    }
}
