using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class ScrollCoveragePlannerTests
{
    private readonly AutoScribeIdentityProfile _profile = AutoScribeIdentityCatalog.Audited;

    [Fact]
    public void SemanticCostRankChoosesAdvancementBeforeOtherDeficits()
    {
        var plan = ScrollCoveragePlanner.Build(World(), _profile);

        var found = plan.TryChooseProduction(
            enabledRoles: null,
            out var selected,
            out var blocked);

        Assert.True(found);
        Assert.Equal("scribe.advancement", selected.Role.Value);
        Assert.Equal(0, selected.CraftCostOrder);
        Assert.Equal(default, blocked);
    }

    [Fact]
    public void UnknownEnabledRoleBlocksWholePublicationBeforeHealthyRoleCanProduce()
    {
        var plan = ScrollCoveragePlanner.Build(
            World(omitTargetEvidenceForRole: 1),
            _profile);

        var found = plan.TryChooseProduction(
            enabledRoles: null,
            out var selected,
            out var blocked);

        Assert.False(found);
        Assert.Equal(default, selected);
        Assert.Equal("scribe.development", blocked.Role.Value);
        Assert.Equal(AutoScribeEvidenceReason.TargetEvidenceMissing, blocked.EvidenceReason);
        Assert.Contains("Development", ScrollCoveragePlanner.DescribeEvidence(in blocked));
        Assert.Contains("target relationship was unavailable",
            ScrollCoveragePlanner.DescribeEvidence(in blocked));
    }

    [Fact]
    public void AutomaticWorkIsExternalPressureAndNeverAProductionPlan()
    {
        var automaticRole = _profile.Roles[0];
        var world = World() with
        {
            ScribeWork = Table(new WorldScribeWork(
                _profile.AutomaticInstances.Uuid,
                automaticRole.Recipe!.Value.Uuid,
                level: 3,
                isAutomatic: true,
                isExpired: false)),
        };

        var plan = ScrollCoveragePlanner.Build(world, _profile);

        Assert.Equal(ScrollCoverageState.ExternallyProducing, plan.Roles[0].State);
        Assert.False(plan.Roles[0].ShouldProduce);
    }

    [Fact]
    public void RoleSelectionUsesSemanticKeysAndEmptyMeansEveryAuditedRole()
    {
        Assert.Null(AutoScribeRoleSelection.ParsePublication(string.Empty, _profile.Roles));

        var selected = AutoScribeRoleSelection.ParsePublication(
            "scribe.power,native-uuid-is-not-a-role",
            _profile.Roles);

        Assert.NotNull(selected);
        Assert.Equal(1, selected!.Count);
        Assert.Equal("scribe.power", selected[0].Value);
        Assert.False(AutoScribeRoleSelection.Contains(
            selected,
            new ScrollRoleKey("scribe.advancement")));
    }

    [Fact]
    public void EveryEvaluatorDispositionWaitsOnlyForAnotherPublication()
    {
        var active = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration { Mode = AutoScribeOperationMode.Active },
        };

        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(new GameWorldState(), new SuiteRuntimeConfiguration(), out var disabled));
        Assert.Equal(0, disabled);
        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(World(), active, out var planned));
        Assert.Equal(1, planned);
        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(World(omitTargetEvidenceForRole: 1), active, out var blocked));
        Assert.Equal(0, blocked);
        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(World(candidateCount: 0), active, out var idle));
        Assert.Equal(0, idle);
    }

    private WakePolicy Evaluate(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        out int actionCount)
    {
        var store = new ReusableActionStore<AutoScribeCycleAction>();
        store.BeginWrite();
        var wake = AutoScribeCycleEvaluator.Evaluate(
            world,
            in configuration,
            _profile,
            enabledRoles: null,
            new ServiceActionWriter<AutoScribeCycleAction>(store),
            out _);
        actionCount = store.Count;
        return wake;
    }

    private GameWorldState World(
        int omitTargetEvidenceForRole = -1,
        int candidateCount = 1)
    {
        var recipes = new List<WorldScribeRecipe>();
        var targets = new List<WorldScrollTarget>();
        var evidence = new List<WorldScrollTargetEvidence>();
        for (var index = 0; index < _profile.Roles.Count; index++)
        {
            var role = _profile.Roles[index];
            if (!role.IsProducible) continue;
            recipes.Add(new WorldScribeRecipe(
                role.Recipe!.Value.Uuid,
                _profile.RecipeType.Uuid,
                role.Scroll.Uuid,
                visible: true,
                usesQuantityAsLevel: true));
            var structure = Guid.NewGuid();
            if (candidateCount > 0)
                targets.Add(new WorldScrollTarget(
                    role.Scroll.Uuid,
                    role.Enchantment.Uuid,
                    structure));
            if (index != omitTargetEvidenceForRole)
                evidence.Add(new WorldScrollTargetEvidence(
                    role.Scroll.Uuid,
                    role.Enchantment.Uuid,
                    candidateCount));
        }

        return new GameWorldState
        {
            CollectedAtFrame = 10,
            CollectedAtEpoch = 1,
            CollectionCategories = Table(new WorldCollectionCategoryStatus(
                ScrollCoveragePlanner.CollectionCategory,
                WorldCategoryOutcome.Collected,
                sampled: 1,
                skipped: 0,
                firstFailure: string.Empty)),
            ScribeRecipes = Table(recipes.ToArray()),
            ScrollTargets = Table(targets.ToArray()),
            ScrollTargetEvidence = Table(evidence.ToArray()),
            CraftingRecipeTypes = Table(new WorldCraftingRecipeType(
                _profile.RecipeType.Uuid,
                startingLevel: 1,
                maxStartingLevel: 3,
                craftVerb: "Scribe",
                isLevelType: true,
                initiated: true,
                magnitudeLoss: 0,
                magnitudeTime: 0,
                magnitudeIncrement: BigDouble.Zero,
                powerModifiers: 0,
                speedModifiers: 0,
                costModModifiers: 0,
                costIncrementModModifiers: 0,
                efficiencyModModifiers: 0,
                autoPenaltyModModifiers: 0,
                multiPenaltyModModifiers: 0)),
        };
    }

    private static PublicationTable<T> Table<T>(params T[] rows)
        where T : struct =>
        rows.Length == 0
            ? PublicationTable<T>.Empty
            : PublicationTable<T>.Create(rows, rows.Length);
}
