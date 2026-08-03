using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeServiceStatusProjectionTests
{
    [Fact]
    public void QueueEvidenceReasonSurvivesProjectionIntoTheUserStatus()
    {
        var profile = AutoScribeIdentityCatalog.Audited;
        var configuration = new SuiteRuntimeConfiguration
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
            },
        };
        var store = new ReusableActionStore<AutoScribeCycleAction>();
        store.BeginWrite();
        AutoScribeCycleEvaluator.Evaluate(
            WorldWithoutQueueEvidence(profile),
            in configuration,
            profile,
            enabledRoles: null,
            afterCraftCostOrder: -1,
            new ServiceActionWriter<AutoScribeCycleAction>(store),
            out var decision);
        Assert.Equal(0, store.Count);
        Assert.Equal(AutoScribeEvidenceReason.QueueEvidenceUnavailable, decision.BlockedReason);
        var state = AutoScribeCycleState.Create(new LifecycleGeneration(1));
        state.RecordDecision(in decision);
        var projection = Projection(in state);

        Assert.True(AutoScribeServiceProjection.TryRead(
            in projection,
            out var kind,
            out var blockedRole,
            out var blockedReason));
        var status = AutoScribeServiceCycleDiagnosticsBridge.ProjectStatus(
            profile,
            emergencyDisabled: false,
            ownsActionFamily: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            new AutoScribeActionHealth(),
            cycleObserved: true,
            kind,
            blockedRole,
            blockedReason);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EvidenceUnavailable, status.Reason);
        Assert.Contains(
            "ActiveScribeInstances capacity evidence was missing or contradictory.",
            status.Summary);
        Assert.DoesNotContain("has complete evidence", status.Summary);
    }

    [Fact]
    public void GenuineActionFailurePrecedesLaterEvidenceBackpressure()
    {
        const string failureReason = "The live recipe relation contradicted its audited role.";
        var failure = AutoScribeSubmission.Reject(
            AutoScribePreflight.RelationshipMismatch,
            failureReason);
        var health = new AutoScribeActionHealth();
        Assert.True(health.Observe(in failure));

        var status = AutoScribeServiceCycleDiagnosticsBridge.ProjectStatus(
            AutoScribeIdentityCatalog.Audited,
            emergencyDisabled: false,
            ownsActionFamily: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            health,
            cycleObserved: true,
            AutoScribeDecisionKind.EvidenceBlocked,
            blockedRole: 0,
            AutoScribeEvidenceReason.QueueEvidenceUnavailable);

        Assert.Equal(FeatureStatusState.ContractUnavailable, status.State);
        Assert.Equal(FeatureStatusReasonCode.IdentityMismatch, status.Reason);
        Assert.Equal(failureReason, status.Summary);
    }

    private static ServiceStateProjectionSnapshot Projection(in AutoScribeCycleState state)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        AutoScribeServiceProjection.Write(in state, builder);
        return builder.CaptureSnapshot();
    }

    private static GameWorldState WorldWithoutQueueEvidence(
        AutoScribeIdentityProfile profile)
    {
        var recipes = new List<WorldScribeRecipe>();
        for (var index = 0; index < profile.Roles.Count; index++)
        {
            var role = profile.Roles[index];
            if (!role.IsProducible) continue;
            recipes.Add(new WorldScribeRecipe(
                role.Recipe!.Value.Uuid,
                profile.RecipeType.Uuid,
                role.Scroll.Uuid,
                visible: true,
                usesQuantityAsLevel: true));
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
            ScribeQueues = PublicationTable<WorldScribeQueue>.Empty,
        };
    }

    private static PublicationTable<T> Table<T>(params T[] rows)
        where T : struct => PublicationTable<T>.Create(rows, rows.Length);
}
