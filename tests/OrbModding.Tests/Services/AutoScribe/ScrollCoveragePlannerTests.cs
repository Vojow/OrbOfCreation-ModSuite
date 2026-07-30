using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class ScrollCoveragePlannerTests
{
    [Fact]
    public void MissingCoverageProducesOneBoundedCraftAtHighestScribeLevel()
    {
        var structure = Guid.NewGuid();
        var world = World(
            structure,
            enchantmentLevel: 4,
            owned: Array.Empty<WorldConsumableCount>(),
            work: Array.Empty<WorldScribeWork>());

        var plan = ScrollCoveragePlanner.Build(world, Profile());

        Assert.True(plan.TryChooseProduction(out var selected));
        Assert.Equal(new ScrollRoleKey("scribe.advancement"), selected.Role);
        Assert.Equal(12, selected.TargetLevel);
        Assert.Equal(1, selected.Deficit);
        Assert.Equal(ScrollCoverageState.ProductionNeeded, selected.State);
    }

    [Fact]
    public void PlannedCraftUsesConfiguredCadenceInsteadOfImmediateRetry()
    {
        var profile = Profile();
        var worker = new AutoScribeWorker(profile);
        var state = worker.CreateState(new LifecycleGeneration(1));
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
                EvaluationInterval =
                    MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(2)),
            },
        };
        var identity = new ServiceCycleIdentity(
            AutoScribeServiceCycleFeature.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            StrategyGeneration.Initial,
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceCycleContext(
            identity,
            default,
            new MonotonicTimestamp(1));
        var actions = new ReusableActionStore<AutoScribeCycleAction>();
        actions.BeginWrite();

        var wake = worker.Evaluate(
            in config,
            World(
                Guid.NewGuid(),
                enchantmentLevel: 4,
                owned: Array.Empty<WorldConsumableCount>(),
                work: Array.Empty<WorldScribeWork>()),
            SuiteStrategyDefaults.Neutral,
            in context,
            ref state,
            new ServiceActionWriter<AutoScribeCycleAction>(actions));

        Assert.Equal(WakePolicyKind.AfterDecision, wake.Kind);
        Assert.Equal(
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(2)),
            wake.Delay);
        Assert.Equal(1, actions.Count);
    }

    [Fact]
    public void SameOrHigherStockReservesTheDeficitAndAllowsUsefulConsumption()
    {
        var structure = Guid.NewGuid();
        var world = World(
            structure,
            enchantmentLevel: 4,
            owned: new[]
            {
                new WorldConsumableCount(
                    KnownEntities.ScrollAdvancement.Uuid, 12, 1, 0),
            },
            work: Array.Empty<WorldScribeWork>());

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(0, role.Deficit);
        Assert.Equal(ScrollCoverageState.Covered, role.State);
        Assert.Equal(ScrollUseDirective.AllowUse, role.UseDirective);
        Assert.False(ScrollCoveragePlanner.Build(world, Profile())
            .TryChooseProduction(out _));
    }

    [Fact]
    public void CompleteCoverageBlocksScrollUseAndProduction()
    {
        var structure = Guid.NewGuid();
        var world = World(
            structure,
            enchantmentLevel: 12,
            owned: new[]
            {
                new WorldConsumableCount(
                    KnownEntities.ScrollAdvancement.Uuid, 12, 2, 0),
            },
            work: Array.Empty<WorldScribeWork>());

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(0, role.Deficit);
        Assert.Equal(ScrollUseDirective.BlockNoCandidate, role.UseDirective);
        Assert.Equal(0, role.UsableCandidates);
    }

    [Fact]
    public void CompleteZeroTargetEvidenceBlocksUseWithoutPretendingEvidenceIsMissing()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 0,
            owned: new[]
            {
                new WorldConsumableCount(
                    KnownEntities.ScrollAdvancement.Uuid, 12, 1, 0),
            },
            work: Array.Empty<WorldScribeWork>()) with
        {
            ScrollTargets = PublicationTable<WorldScrollTarget>.Empty,
            ScrollTargetEvidence = Table(new WorldScrollTargetEvidence(
                KnownEntities.ScrollAdvancement.Uuid,
                KnownEntities.EnchantAdvancement.Uuid,
                candidateCount: 0)),
        };

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(ScrollUseDirective.BlockNoCandidate, role.UseDirective);
        Assert.Equal(ScrollCoverageState.Covered, role.State);
        Assert.Equal(0, role.ValidTargets);
    }

    [Fact]
    public void PendingSameLevelScrollReservesOneProductionDeficit()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 4,
            owned: Array.Empty<WorldConsumableCount>(),
            work: Array.Empty<WorldScribeWork>()) with
        {
            ConsumableUsages = Table(new WorldConsumableUsage(
                KnownEntities.ScrollAdvancement.Uuid,
                Guid.NewGuid(),
                level: 12,
                engaged: false,
                remainingDuration: BigDouble.One,
                maximumDuration: BigDouble.One)),
        };

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(1, role.PendingUseSupply);
        Assert.Equal(0, role.Deficit);
        Assert.False(ScrollCoveragePlanner.Build(world, Profile())
            .TryChooseProduction(out _));
    }

    [Fact]
    public void DisabledHighestDeficitDoesNotStarveAnEnabledRole()
    {
        var disabled = Production("scribe.advancement", deficit: 4);
        var enabled = Production("scribe.power", deficit: 1);
        var plan = new ScrollCoveragePlan(
            frame: 8,
            epoch: 2,
            new[] { disabled, enabled });

        Assert.True(AutoScribeWorker.TryChooseEnabledProduction(
            plan,
            "scribe.power",
            out var selected));
        Assert.Equal(new ScrollRoleKey("scribe.power"), selected.Role);
    }

    private static ScrollRoleCoverage FindAdvancement(ScrollCoveragePlan plan)
    {
        Assert.True(plan.TryFind(
            KnownEntities.ScrollAdvancement.Uuid, out var role));
        return role;
    }

    private static ScrollRoleCoverage Production(string role, int deficit) =>
        new(
            new ScrollRoleKey(role),
            role,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            TargetLevel: 12,
            ValidTargets: deficit,
            CoveredTargets: 0,
            OwnedSupply: 0,
            QueuedSupply: 0,
            PendingUseSupply: 0,
            Deficit: deficit,
            StrongestOwnedLevel: 0,
            UsableCandidates: 0,
            ScrollUseDirective.BlockNoCandidate,
            ScrollCoverageState.ProductionNeeded);

    private static AutoScribeIdentityProfile Profile()
    {
        var catalog = new AutoScribeIdentityCatalog();
        Assert.True(catalog.TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId, out var profile));
        return profile;
    }

    private static GameWorldState World(
        Guid structure,
        int enchantmentLevel,
        WorldConsumableCount[] owned,
        WorldScribeWork[] work)
    {
        var recipeType = new WorldCraftingRecipeType(
            KnownEntities.ScribeCrafting.Uuid,
            startingLevel: 10,
            maxStartingLevel: 12,
            craftVerb: "Scribe",
            isLevelType: true,
            initiated: true,
            magnitudeLoss: 0,
            magnitudeTime: 0,
            magnitudeIncrement: BigDouble.One,
            powerModifiers: 0,
            speedModifiers: 0,
            costModModifiers: 0,
            costIncrementModModifiers: 0,
            efficiencyModModifiers: 0,
            autoPenaltyModModifiers: 0,
            multiPenaltyModModifiers: 0);
        return new GameWorldState
        {
            CollectedAtFrame = 8,
            CollectedAtEpoch = 2,
            CraftingRecipeTypes = Table(recipeType),
            ScribeRecipes = Table(new WorldScribeRecipe(
                KnownEntities.CraftScrollAdvancement.Uuid,
                KnownEntities.ScribeCrafting.Uuid,
                KnownEntities.ScrollAdvancement.Uuid,
                visible: true,
                usesQuantityAsLevel: true)),
            ScrollTargets = Table(new WorldScrollTarget(
                KnownEntities.ScrollAdvancement.Uuid,
                KnownEntities.EnchantAdvancement.Uuid,
                structure)),
            ScrollTargetEvidence = Table(new WorldScrollTargetEvidence(
                KnownEntities.ScrollAdvancement.Uuid,
                KnownEntities.EnchantAdvancement.Uuid,
                candidateCount: 1)),
            StructureEnchantments = Table(new WorldStructureEnchantment(
                structure,
                KnownEntities.EnchantAdvancement.Uuid,
                enchantmentLevel)),
            ConsumableCounts = PublicationTable<WorldConsumableCount>.Create(
                owned, owned.Length),
            ScribeWork = PublicationTable<WorldScribeWork>.Create(
                work, work.Length),
        };
    }

    private static PublicationTable<T> Table<T>(T row) where T : struct =>
        PublicationTable<T>.Create(new[] { row }, 1);
}
