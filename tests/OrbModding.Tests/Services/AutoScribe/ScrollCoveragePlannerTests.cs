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

        Assert.True(plan.TryChooseCraft(out var selected));
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
        Assert.True(ScrollCoveragePlanner.Build(world, Profile())
            .TryChooseCraft(out var selected));
        Assert.Equal(13, selected.RequestedCraftLevel);
        Assert.True(selected.ShouldProbeProgression);
    }

    [Fact]
    public void CompleteCoverageBlocksUseButStillProbesTheNextAffordableLevel()
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
        Assert.True(role.ShouldProbeProgression);
        Assert.Equal(13, role.ProgressionLevel);
        Assert.Equal(13, role.RequestedCraftLevel);
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
        Assert.True(ScrollCoveragePlanner.Build(world, Profile())
            .TryChooseCraft(out var selected));
        Assert.Equal(13, selected.RequestedCraftLevel);
    }

    [Fact]
    public void DisabledHighestDeficitDoesNotStarveAnEnabledRole()
    {
        var disabled = Production(
            "scribe.advancement", deficit: 4, craftCostOrder: 0);
        var enabled = Production(
            "scribe.power", deficit: 1, craftCostOrder: 5);
        var plan = new ScrollCoveragePlan(
            frame: 8,
            epoch: 2,
            new[] { disabled, enabled });

        Assert.True(plan.TryChooseCraft(
            AutoScribeRoleSelection.ParsePublication(
                "scribe.power",
                Profile().Roles),
            out var selected));
        Assert.Equal(new ScrollRoleKey("scribe.power"), selected.Role);
    }

    [Fact]
    public void ProductionSelectionRotatesAcrossEveryProducibleFacadeRole()
    {
        var advancement = Production(
            "scribe.advancement", deficit: 6, craftCostOrder: 0);
        var power = Production(
            "scribe.power", deficit: 5, craftCostOrder: 1);
        var learning = Production(
            "scribe.learning", deficit: 4, craftCostOrder: 2);
        var excellence = Production(
            "scribe.excellence", deficit: 3, craftCostOrder: 3);
        var development = Production(
            "scribe.development", deficit: 2, craftCostOrder: 4);
        var echo = Production(
            "scribe.echo", deficit: 1, craftCostOrder: 5);
        var plan = new ScrollCoveragePlan(
            frame: 8,
            epoch: 2,
            new[] { development, power, echo, advancement, excellence, learning });

        var expected = new[]
        {
            "scribe.advancement",
            "scribe.power",
            "scribe.learning",
            "scribe.excellence",
            "scribe.development",
            "scribe.echo",
            "scribe.advancement",
        };
        var cursor = -1;
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.True(plan.TryChooseCraft(
                enabledRoles: null,
                cursor,
                out var selected));
            Assert.Equal(new ScrollRoleKey(expected[index]), selected.Role);
            cursor = selected.CraftCostOrder;
        }
    }

    [Fact]
    public void LowerLevelStockRemainsUsableButDoesNotReserveHighestLevelProduction()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 4,
            owned: new[]
            {
                new WorldConsumableCount(
                    KnownEntities.ScrollAdvancement.Uuid, 11, 3, 0),
            },
            work: Array.Empty<WorldScribeWork>());

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(11, role.StrongestOwnedLevel);
        Assert.Equal(0, role.OwnedSupply);
        Assert.Equal(1, role.Deficit);
        Assert.Equal(ScrollUseDirective.AllowUse, role.UseDirective);
        Assert.True(role.ShouldProduce);
    }

    [Fact]
    public void SameOrHigherManualQueueWorkReservesDeficitButLowerWorkDoesNot()
    {
        var lower = World(
            Guid.NewGuid(),
            enchantmentLevel: 4,
            owned: Array.Empty<WorldConsumableCount>(),
            work: new[]
            {
                new WorldScribeWork(
                    KnownEntities.ActiveScribeInstances.Uuid,
                    KnownEntities.CraftScrollAdvancement.Uuid,
                    level: 11,
                    isAutomatic: false,
                    isExpired: false),
            });
        var matching = lower with
        {
            ScribeWork = Table(new WorldScribeWork(
                KnownEntities.ActiveScribeInstances.Uuid,
                KnownEntities.CraftScrollAdvancement.Uuid,
                level: 12,
                isAutomatic: false,
                isExpired: false)),
        };

        var lowerRole = FindAdvancement(ScrollCoveragePlanner.Build(lower, Profile()));
        var matchingRole = FindAdvancement(ScrollCoveragePlanner.Build(matching, Profile()));

        Assert.Equal(1, lowerRole.Deficit);
        Assert.True(lowerRole.ShouldProduce);
        Assert.Equal(1, matchingRole.QueuedSupply);
        Assert.Equal(0, matchingRole.Deficit);
        Assert.False(matchingRole.ShouldProduce);
        Assert.False(matchingRole.ShouldProbeProgression);
        Assert.False(ScrollCoveragePlanner.Build(matching, Profile())
            .TryChooseCraft(out _));
    }

    [Fact]
    public void PlayerAutomaticWorkSuppressesCompetingProductionAndIsReportedSeparately()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 4,
            owned: Array.Empty<WorldConsumableCount>(),
            work: new[]
            {
                new WorldScribeWork(
                    KnownEntities.AutoScribeInstances.Uuid,
                    KnownEntities.CraftScrollAdvancement.Uuid,
                    level: 12,
                    isAutomatic: true,
                    isExpired: false),
            });

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(0, role.QueuedSupply);
        Assert.Equal(1, role.Deficit);
        Assert.Equal(ScrollCoverageState.ExternallyProducing, role.State);
        Assert.False(role.ShouldProduce);
    }

    [Fact]
    public void ExpiredAutomaticWorkDoesNotSuppressNeededProduction()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 4,
            owned: Array.Empty<WorldConsumableCount>(),
            work: new[]
            {
                new WorldScribeWork(
                    KnownEntities.AutoScribeInstances.Uuid,
                    KnownEntities.CraftScrollAdvancement.Uuid,
                    level: 12,
                    isAutomatic: true,
                    isExpired: true),
            });

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(ScrollCoverageState.ProductionNeeded, role.State);
        Assert.Equal(1, role.Deficit);
        Assert.True(role.ShouldProduce);
    }

    [Fact]
    public void MissingTargetEvidenceBlocksBothUseAndProduction()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 4,
            owned: new[]
            {
                new WorldConsumableCount(
                    KnownEntities.ScrollAdvancement.Uuid, 12, 1, 0),
            },
            work: Array.Empty<WorldScribeWork>()) with
        {
            ScrollTargetEvidence = PublicationTable<WorldScrollTargetEvidence>.Empty,
        };

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(ScrollCoverageState.EvidenceUnknown, role.State);
        Assert.Equal(ScrollUseDirective.BlockUnknown, role.UseDirective);
        Assert.False(role.ShouldProduce);
    }

    [Fact]
    public void HigherCreatedScrollLevelReopensOnlyThatRolesCoverage()
    {
        var structure = Guid.NewGuid();
        var levelTwelve = World(
            structure,
            enchantmentLevel: 12,
            owned: Array.Empty<WorldConsumableCount>(),
            work: Array.Empty<WorldScribeWork>());
        var levelThirteen = levelTwelve with
        {
            Consumables = Table(Scroll(
                maxCreatedLevel: 13,
                maximumCarryLoad: 0)),
        };

        var complete = FindAdvancement(
            ScrollCoveragePlanner.Build(levelTwelve, Profile()));
        var reopened = FindAdvancement(
            ScrollCoveragePlanner.Build(levelThirteen, Profile()));

        Assert.Equal(ScrollCoverageState.Covered, complete.State);
        Assert.True(complete.ShouldProbeProgression);
        Assert.Equal(13, complete.ProgressionLevel);
        Assert.Equal(ScrollCoverageState.ProductionNeeded, reopened.State);
        Assert.Equal(13, reopened.TargetLevel);
        Assert.Equal(1, reopened.Deficit);
    }

    [Fact]
    public void SharedScribeMaximumRaisedByCheaperRecipeDoesNotRaiseAnotherRolesTarget()
    {
        var structure = Guid.NewGuid();
        var world = World(
            structure,
            enchantmentLevel: 24,
            owned: Array.Empty<WorldConsumableCount>(),
            work: Array.Empty<WorldScribeWork>(),
            scrollId: KnownEntities.ScrollPower.Uuid,
            recipeId: KnownEntities.CraftScrollPower.Uuid,
            enchantmentId: KnownEntities.EnchantPower.Uuid) with
        {
            CraftingRecipeTypes = Table(RecipeType(maxStartingLevel: 67)),
            Consumables = Table(Scroll(
                KnownEntities.ScrollPower.Uuid,
                maxCreatedLevel: 24,
                maximumCarryLoad: 0)),
        };

        var plan = ScrollCoveragePlanner.Build(world, Profile());
        Assert.True(plan.TryFind(KnownEntities.ScrollPower.Uuid, out var role));

        Assert.Equal(24, role.TargetLevel);
        Assert.Equal(ScrollCoverageState.Covered, role.State);
        Assert.False(role.ShouldProduce);
        Assert.True(role.ShouldProbeProgression);
        Assert.Equal(25, role.ProgressionLevel);
    }

    [Fact]
    public void VisibleRecipeWithoutItsScrollFrontierFailsClosed()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 0,
            owned: Array.Empty<WorldConsumableCount>(),
            work: Array.Empty<WorldScribeWork>()) with
        {
            Consumables = PublicationTable<WorldConsumable>.Empty,
        };

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(ScrollCoverageState.EvidenceUnknown, role.State);
        Assert.Equal(ScrollUseDirective.BlockUnknown, role.UseDirective);
        Assert.False(role.ShouldProduce);
    }

    [Fact]
    public void HigherQueuedWorkRaisesOnlyItsRecipeFrontierBeforeDelivery()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 12,
            owned: Array.Empty<WorldConsumableCount>(),
            work: new[]
            {
                new WorldScribeWork(
                    KnownEntities.ActiveScribeInstances.Uuid,
                    KnownEntities.CraftScrollAdvancement.Uuid,
                    level: 17,
                    isAutomatic: false,
                    isExpired: false),
            });

        var role = FindAdvancement(ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(17, role.TargetLevel);
        Assert.Equal(1, role.QueuedSupply);
        Assert.Equal(0, role.Deficit);
        Assert.False(role.ShouldProbeProgression);
    }

    [Fact]
    public void LockedRoleIsDormantAndBecomesProductionEligibleWhenUnlocked()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 0,
            owned: Array.Empty<WorldConsumableCount>(),
            work: Array.Empty<WorldScribeWork>());
        var locked = world with
        {
            ScribeRecipes = Table(new WorldScribeRecipe(
                KnownEntities.CraftScrollAdvancement.Uuid,
                KnownEntities.ScribeCrafting.Uuid,
                KnownEntities.ScrollAdvancement.Uuid,
                visible: false,
                usesQuantityAsLevel: true)),
        };

        var before = FindAdvancement(
            ScrollCoveragePlanner.Build(locked, Profile()));
        var after = FindAdvancement(
            ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(ScrollCoverageState.Unavailable, before.State);
        Assert.False(before.ShouldProduce);
        Assert.Equal(ScrollCoverageState.ProductionNeeded, after.State);
        Assert.True(after.ShouldProduce);
    }

    [Fact]
    public void CompleteCoverageStillFillsHighestLevelStockToNativeCarryLimit()
    {
        var world = World(
            Guid.NewGuid(),
            enchantmentLevel: 12,
            owned: new[]
            {
                new WorldConsumableCount(
                    KnownEntities.ScrollAdvancement.Uuid, 12, 2, 0),
            },
            work: Array.Empty<WorldScribeWork>()) with
        {
            Consumables = Table(Scroll(
                maxCreatedLevel: 12,
                maximumCarryLoad: 5)),
        };

        var role = FindAdvancement(
            ScrollCoveragePlanner.Build(world, Profile()));

        Assert.Equal(3, role.Deficit);
        Assert.Equal(ScrollCoverageState.ProductionNeeded, role.State);
        Assert.Equal(ScrollUseDirective.BlockNoCandidate, role.UseDirective);
    }

    [Fact]
    public void CraftQueueStockPendingUseAndAppliedEnchantmentConvergeWithoutOverproduction()
    {
        var structure = Guid.NewGuid();
        var initial = World(
            structure,
            enchantmentLevel: 4,
            owned: Array.Empty<WorldConsumableCount>(),
            work: Array.Empty<WorldScribeWork>());
        var queued = initial with
        {
            ScribeWork = Table(new WorldScribeWork(
                KnownEntities.ActiveScribeInstances.Uuid,
                KnownEntities.CraftScrollAdvancement.Uuid,
                level: 12,
                isAutomatic: false,
                isExpired: false)),
        };
        var stocked = initial with
        {
            ConsumableCounts = Table(new WorldConsumableCount(
                KnownEntities.ScrollAdvancement.Uuid,
                level: 12,
                quantity: 1,
                freeQuantity: 0)),
        };
        var pendingUse = initial with
        {
            ConsumableUsages = Table(new WorldConsumableUsage(
                KnownEntities.ScrollAdvancement.Uuid,
                Guid.NewGuid(),
                level: 12,
                engaged: false,
                remainingDuration: BigDouble.One,
                maximumDuration: BigDouble.One)),
        };
        var applied = initial with
        {
            StructureEnchantments = Table(new WorldStructureEnchantment(
                structure,
                KnownEntities.EnchantAdvancement.Uuid,
                level: 12)),
        };

        var missing = FindAdvancement(
            ScrollCoveragePlanner.Build(initial, Profile()));
        var reservedByQueue = FindAdvancement(
            ScrollCoveragePlanner.Build(queued, Profile()));
        var readyToUse = FindAdvancement(
            ScrollCoveragePlanner.Build(stocked, Profile()));
        var reservedByUse = FindAdvancement(
            ScrollCoveragePlanner.Build(pendingUse, Profile()));
        var complete = FindAdvancement(
            ScrollCoveragePlanner.Build(applied, Profile()));

        Assert.True(missing.ShouldProduce);
        Assert.Equal(0, reservedByQueue.Deficit);
        Assert.Equal(ScrollUseDirective.AllowUse, readyToUse.UseDirective);
        Assert.Equal(0, reservedByUse.Deficit);
        Assert.Equal(ScrollCoverageState.Covered, complete.State);
        Assert.False(reservedByQueue.ShouldProduce);
        Assert.False(readyToUse.ShouldProduce);
        Assert.False(reservedByUse.ShouldProduce);
        Assert.False(complete.ShouldProduce);
    }

    private static ScrollRoleCoverage FindAdvancement(ScrollCoveragePlan plan)
    {
        Assert.True(plan.TryFind(
            KnownEntities.ScrollAdvancement.Uuid, out var role));
        return role;
    }

    private static ScrollRoleCoverage Production(
        string role,
        int deficit,
        int craftCostOrder) =>
        new(
            new ScrollRoleKey(role),
            role,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CraftCostOrder: craftCostOrder,
            TargetLevel: 12,
            ProgressionLevel: 0,
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
        WorldScribeWork[] work,
        Guid? scrollId = null,
        Guid? recipeId = null,
        Guid? enchantmentId = null)
    {
        var resolvedScrollId = scrollId ?? KnownEntities.ScrollAdvancement.Uuid;
        var resolvedRecipeId = recipeId ?? KnownEntities.CraftScrollAdvancement.Uuid;
        var resolvedEnchantmentId =
            enchantmentId ?? KnownEntities.EnchantAdvancement.Uuid;
        var recipeType = RecipeType(maxStartingLevel: 12);
        return new GameWorldState
        {
            CollectedAtFrame = 8,
            CollectedAtEpoch = 2,
            CraftingRecipeTypes = Table(recipeType),
            ScribeRecipes = Table(new WorldScribeRecipe(
                resolvedRecipeId,
                KnownEntities.ScribeCrafting.Uuid,
                resolvedScrollId,
                visible: true,
                usesQuantityAsLevel: true)),
            ScrollTargets = Table(new WorldScrollTarget(
                resolvedScrollId,
                resolvedEnchantmentId,
                structure)),
            ScrollTargetEvidence = Table(new WorldScrollTargetEvidence(
                resolvedScrollId,
                resolvedEnchantmentId,
                candidateCount: 1)),
            StructureEnchantments = Table(new WorldStructureEnchantment(
                structure,
                resolvedEnchantmentId,
                enchantmentLevel)),
            ConsumableCounts = PublicationTable<WorldConsumableCount>.Create(
                owned, owned.Length),
            Consumables = Table(Scroll(
                resolvedScrollId,
                maxCreatedLevel: 12,
                maximumCarryLoad: 0)),
            ScribeWork = PublicationTable<WorldScribeWork>.Create(
                work, work.Length),
        };
    }

    private static WorldCraftingRecipeType RecipeType(int maxStartingLevel) =>
        new(
            KnownEntities.ScribeCrafting.Uuid,
            startingLevel: 10,
            maxStartingLevel,
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

    private static WorldConsumable Scroll(
        int maxCreatedLevel,
        int maximumCarryLoad) =>
        Scroll(
            KnownEntities.ScrollAdvancement.Uuid,
            maxCreatedLevel,
            maximumCarryLoad);

    private static WorldConsumable Scroll(
        Guid scrollId,
        int maxCreatedLevel,
        int maximumCarryLoad) =>
        new(
            scrollId,
            visible: true,
            randomized: true,
            quantity: 0,
            queuedQuantity: 0,
            gainedSince: 0,
            maxCreatedLevel,
            currentPrepTime: BigDouble.Zero,
            currentCooldown: BigDouble.Zero,
            currentCooldownTime: BigDouble.Zero,
            modifiers: new RawConsumableModifiers(
                BigDouble.One,
                BigDouble.One,
                BigDouble.One,
                BigDouble.One,
                BigDouble.Zero),
            preparationTime: 1d,
            canBeRandomized: true,
            hasDuration: false,
            durationBase: 0d,
            queueOnStart: false,
            maximumCarryLoad: maximumCarryLoad);

    private static PublicationTable<T> Table<T>(T row) where T : struct =>
        PublicationTable<T>.Create(new[] { row }, 1);
}
