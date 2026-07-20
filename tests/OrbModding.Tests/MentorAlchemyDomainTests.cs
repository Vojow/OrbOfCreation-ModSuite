using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using OrbMentor;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorAlchemyDomainTests : IDisposable
{
    public MentorAlchemyDomainTests()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, object>();
        RegisterUnlockedView(MentorDomainUnlockGate.MasteriesEnabledUuid);
        RegisterUnlockedView(MentorDomainUnlockGate.SpellbookUuid);
        RegisterUnlockedView(MentorDomainUnlockGate.ArtifactWorkshopUuid);
        RegisterUnlockedView(MentorDomainUnlockGate.AlchemyScreenUuid);
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, object>();
    }

    [Fact]
    public void DisabledAlchemyDoesNotInitializeSharedClassifier()
    {
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.AlchemyEnabled.Value = false;
        using var gate = new MentorAlchemyDomainGate();
        using var runtime = new MentorRuntime(config, new ManualLogSource(), alchemyDomainGate: gate);

        runtime.LateTick();

        Assert.Equal(AlchemyDomainClassifierStatus.Uninitialized, runtime.AlchemyClassifierStatus);
    }

    [Fact]
    public void HigherLevelScholarConceptCannotDisplaceOrdinaryAlchemyMentor()
    {
        var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        var ordinaryMentor = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.BrewingTypeUuid));
        var ordinaryRecipient = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.RefinementTypeUuid));
        RegisterConceptRecipes(concept);
        using var gate = new MentorAlchemyDomainGate();
        Assert.True(gate.TryInitialize(out var reason), reason);

        var candidates = new[]
        {
            (Recipe: concept, Mastery: 100),
            (Recipe: ordinaryMentor, Mastery: 12),
            (Recipe: ordinaryRecipient, Mastery: 3),
        };
        var ordinaryCatalog = candidates
            .Where(candidate => gate.ClassifyAndCache(candidate.Recipe).Domain == AlchemyGameplayDomain.OrdinaryAlchemy)
            .Select(candidate => new MentorRecipe(
                candidate.Recipe.GetGuid().ToString(), candidate.Mastery, true))
            .ToArray();

        var recipients = new MentorEngine().EligibleRecipients(
            ordinaryMentor.GetGuid().ToString(), ordinaryCatalog);

        Assert.Equal(2, ordinaryCatalog.Length);
        Assert.DoesNotContain(ordinaryCatalog, recipe => recipe.Uuid == concept.GetGuid().ToString());
        Assert.Equal(ordinaryRecipient.GetGuid().ToString(), Assert.Single(recipients).Uuid);
    }

    [Fact]
    public void ScholarConceptXpIsIgnoredWithoutCreatingCapturedOrDroppedWork()
    {
        var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid));
        RegisterConceptRecipes(concept);
        using var gate = new MentorAlchemyDomainGate();
        Assert.True(gate.TryInitialize(out var reason), reason);
        Assert.Equal(AlchemyGameplayDomain.ScholarConcept, gate.ClassifyAndCache(concept).Domain);
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.AlchemyEnabled.Value = true;
        using var runtime = new MentorRuntime(config, new ManualLogSource(), alchemyDomainGate: gate);
        runtime.LateTick();

        runtime.ObserveAlchemy(concept, new global::BigDouble(5, 0));

        Assert.Equal(0, runtime.Diagnostics.CapturedEvents);
        Assert.Equal(0, runtime.Diagnostics.DroppedEvents);
        Assert.False(runtime.IsBlocked);
    }

    [Fact]
    public void UncachedAlchemyXpRequestsReconciliationWithoutClassifyingInTheHook()
    {
        var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        var ordinary = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.BrewingTypeUuid));
        RegisterConceptRecipes(concept);
        using var gate = new MentorAlchemyDomainGate();
        Assert.True(gate.TryInitialize(out var reason), reason);
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.AlchemyEnabled.Value = true;
        using var runtime = new MentorRuntime(config, new ManualLogSource(), alchemyDomainGate: gate);
        runtime.LateTick();

        runtime.ObserveAlchemy(ordinary, new global::BigDouble(5, 0));

        Assert.False(gate.TryGetCached(ordinary, out _));
        Assert.Equal(0, runtime.Diagnostics.CapturedEvents);
        Assert.Equal(0, runtime.Diagnostics.DroppedEvents);
        Assert.False(runtime.IsBlocked);
    }

    [Fact]
    public void UnknownAlchemyEvidenceBlocksMentorAlchemyForTheLifecycle()
    {
        RegisterConceptRecipes(Recipe(
            Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ConceptualizationTypeUuid)));
        var unknown = Recipe(Guid.NewGuid(), Type(Guid.NewGuid()));
        using var gate = new MentorAlchemyDomainGate();
        Assert.True(gate.TryInitialize(out var reason), reason);
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.AlchemyEnabled.Value = true;
        using var runtime = new MentorRuntime(config, new ManualLogSource(), alchemyDomainGate: gate);
        runtime.LateTick();

        var classification = gate.ClassifyAndCache(unknown);
        runtime.ObserveAlchemy(unknown, new global::BigDouble(1, 0));

        Assert.Equal(AlchemyGameplayDomain.Unknown, classification.Domain);
        Assert.Contains("no audited ordinary alchemy", classification.Reason, StringComparison.Ordinal);
        Assert.StartsWith("Blocked: Alchemy XP capture could not prove an ordinary-alchemy recipe", runtime.CurrentMentor(MentorDomain.Alchemy));
        Assert.Equal(0, runtime.Diagnostics.CapturedEvents);
    }

    [Fact]
    public void MentorLifecycleResetInvalidatesClassifierEvidence()
    {
        var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        RegisterConceptRecipes(concept);
        using var gate = new MentorAlchemyDomainGate();
        Assert.True(gate.TryInitialize(out var reason), reason);
        var generation = gate.LifecycleGeneration;
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());
        using var runtime = new MentorRuntime(config, new ManualLogSource(), alchemyDomainGate: gate);

        runtime.ResetLifecycle();

        Assert.True(gate.LifecycleGeneration > generation);
        Assert.Equal(AlchemyDomainClassifierStatus.Uninitialized, gate.Status);
        Assert.False(gate.TryGetCached(concept, out _));
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

    private static void RegisterUnlockedView(string uuid)
    {
        var view = new ViewSO
        {
            uuid = new Guid(uuid),
            available = true,
        };
        IdScriptableObject.RuntimeLookup[view.uuid] = view;
    }
}
