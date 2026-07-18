using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoConceptDomainClassifierAdoptionTests : IDisposable
{
    public AutoConceptDomainClassifierAdoptionTests()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();
    }

    [Fact]
    public void DisabledAutoConceptDoesNotInitializeOrScanSharedClassifier()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        var classifier = new AlchemyGameplayDomainClassifier();
        using var controller = Controller(config, classifier, new ManualLogSource(), () => 1);

        controller.Tick(60.0f);

        Assert.Equal(AlchemyDomainClassifierStatus.Uninitialized, classifier.Status);
        Assert.Equal(0, classifier.CachedRecipeCount);
    }

    [Fact]
    public void ActiveAutoConceptRetriesMissingClassifierEvidenceWithRateLimitedWarning()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoConceptMode.Value = AutoConceptOperationMode.Active;
        var classifier = new AlchemyGameplayDomainClassifier();
        var log = new ManualLogSource();
        long frame = 1;
        using var controller = Controller(config, classifier, log, () => frame);

        controller.Tick(0.0f);
        frame++;
        controller.Tick(10.0f);
        frame++;
        controller.Tick(10.0f);

        Assert.Equal(AlchemyDomainClassifierStatus.Retryable, classifier.Status);
        Assert.Single(
            log.Entries,
            entry => entry?.ToString()?.Contains(
                "Auto Concept domain classifier is not ready: ConceptRecipes resolution failed.",
                StringComparison.Ordinal) == true);
        Assert.Contains(
            log.Entries,
            entry => entry?.ToString()?.Contains("Status=NotFound", StringComparison.Ordinal) == true);

        var generation = classifier.ClassifierGeneration;
        controller.InvalidateLifecycle();
        Assert.Equal(AlchemyDomainClassifierStatus.Uninitialized, classifier.Status);
        Assert.True(classifier.ClassifierGeneration > generation);
    }

    [Fact]
    public void ContradictorySharedDomainEvidenceBlocksAutoConceptOncePerLifecycle()
    {
        var conceptType = Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid);
        var ordinaryType = Type(AlchemyGameplayDomainClassifier.AlchemyTypeUuid);
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), conceptType, ordinaryType));
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AutoConceptMode.Value = AutoConceptOperationMode.Active;
        var classifier = new AlchemyGameplayDomainClassifier();
        var log = new ManualLogSource();
        long frame = 1;
        using var controller = Controller(config, classifier, log, () => frame);

        controller.Tick(0.0f);
        frame++;
        controller.Tick(10.0f);
        frame++;
        controller.Tick(30.0f);

        Assert.Equal(AlchemyDomainClassifierStatus.Blocked, classifier.Status);
        Assert.Single(
            log.Entries,
            entry => entry?.ToString()?.Contains(
                "Auto Concept domain classifier blocked:",
                StringComparison.Ordinal) == true);
    }

    private static AutoConceptController Controller(
        AutomataConfig config,
        AlchemyGameplayDomainClassifier classifier,
        ManualLogSource log,
        Func<long> frameIdentity)
    {
        return new AutoConceptController(
            config,
            new ReflectionConceptRuntime(classifier),
            log,
            new SuitePerformanceCoordinator(StopwatchPerformanceClock.Instance, 1000.0, 1000.0),
            frameIdentity);
    }

    private static AlchemyTypeSO Type(Guid uuid)
    {
        var type = new AlchemyTypeSO();
        type.SetGuid(uuid);
        return type;
    }

    private static AlchemyRecipeSO Recipe(Guid uuid, params AlchemyTypeSO[] types)
    {
        var recipe = new AlchemyRecipeSO();
        recipe.SetGuid(uuid);
        recipe.alchemyTypes.AddRange(types);
        return recipe;
    }

    private static void RegisterConceptRecipes(params AlchemyRecipeSO[] recipes)
    {
        var registry = new AlchemyRecipeListVariable();
        registry.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        registry.value.AddRange(recipes);
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = registry;
    }
}
