using System;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoAgromancy;

public sealed class AutoAgromancyMigrationIntegrationTests
{
    [Fact]
    public void PublicConfigurationIsDisabledByDefaultAndPublishesActiveIntent()
    {
        var source = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.Equal(
            AutoAgromancyOperationMode.Disabled,
            source.Current.AutoAgromancy.Mode);

        source.AutoAgromancyMode.Value = AutoAgromancyOperationMode.Active;
        Assert.Equal(
            AutoAgromancyOperationMode.Active,
            source.Current.AutoAgromancy.Mode);
    }

    [Fact]
    public void AgromancyOwnershipIsIndependentFromHarvestSubmissionOwnership()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        using var ownership = new AutomataActionFamilyOwnership(registry);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoHarvest = new AutoHarvestConfiguration
            {
                Mode = AutoHarvestOperationMode.Active,
                CollectFruitTrees = true,
            },
            AutoAgromancy = new AutoAgromancyConfiguration
            {
                Mode = AutoAgromancyOperationMode.Active,
            },
        };

        ownership.Refresh(config, lifecycleReady: true);

        Assert.True(ownership.OwnsHarvest);
        Assert.True(ownership.OwnsAgromancy);
        Assert.True(ownership.TryCaptureHarvestMutationPermit());
        Assert.True(ownership.TryCaptureAgromancyMutationPermit());
    }

    [Fact]
    public void PlotTriggerAdvancesOnlyAfterMatchingQuantityIncreases()
    {
        var queue = new global::PlotNodeActionInstanceListVariable();
        queue.SetGuid(KnownEntities.ActivePlotNodeActions.Uuid);
        var action = new global::PlotNodeActionSO();
        var instance = new global::PlotNodeActionInstance(action)
        {
            plotNodeRefObj = new global::PlotNodeSO(),
            quantity = 1,
        };
        queue.value.Add(instance);
        var beforeEpoch = WorldHarvestActionTriggerSource.PlotActionEpoch;
        var before = AutoAgromancyPlotActionPatch.Capture(queue, instance);

        Assert.False(
            AutoAgromancyPlotActionPatch.PublishIfIncreased(
                queue, instance, in before));
        Assert.Equal(beforeEpoch, WorldHarvestActionTriggerSource.PlotActionEpoch);

        instance.quantity = 2;
        Assert.True(
            AutoAgromancyPlotActionPatch.PublishIfIncreased(
                queue, instance, in before));
        Assert.Equal(
            beforeEpoch + 1,
            WorldHarvestActionTriggerSource.PlotActionEpoch);
    }

    [Fact]
    public void PlotTriggerIgnoresWrongListAndQuantityDecrease()
    {
        var wrongList = new global::PlotNodeActionInstanceListVariable();
        wrongList.SetGuid(Guid.NewGuid());
        var action = new global::PlotNodeActionSO();
        var instance = new global::PlotNodeActionInstance(action)
        {
            plotNodeRefObj = new global::PlotNodeSO(),
            quantity = 2,
        };
        wrongList.value.Add(instance);
        var beforeEpoch = WorldHarvestActionTriggerSource.PlotActionEpoch;

        var wrongBefore = AutoAgromancyPlotActionPatch.Capture(wrongList, instance);
        instance.quantity = 3;
        Assert.False(
            AutoAgromancyPlotActionPatch.PublishIfIncreased(
                wrongList, instance, in wrongBefore));

        var queue = new global::PlotNodeActionInstanceListVariable();
        queue.SetGuid(KnownEntities.ActivePlotNodeActions.Uuid);
        queue.value.Add(instance);
        var decreaseBefore = AutoAgromancyPlotActionPatch.Capture(queue, instance);
        instance.quantity = 1;
        Assert.False(
            AutoAgromancyPlotActionPatch.PublishIfIncreased(
                queue, instance, in decreaseBefore));
        Assert.Equal(beforeEpoch, WorldHarvestActionTriggerSource.PlotActionEpoch);
    }

    [Fact]
    public void DirectIncreaseRemainsPendingUntilAcceptedOrRemoved()
    {
        var actionId = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        var store = new AutoAgromancyObservedLevelStore();
        store.Initialize(PublicationTable<WorldHarvestAction>.Create(new[]
        {
            new WorldHarvestAction(
                actionId, elementId, 1, 5, true,
                new BigDouble(100), new BigDouble(100), false),
        }));
        var increased = PublicationTable<WorldHarvestAction>.Create(new[]
        {
            new WorldHarvestAction(
                actionId, elementId, 3, 5, true,
                new BigDouble(100), new BigDouble(100), false),
        });

        Assert.True(store.TryTakeIncrease(
            increased, out _, out _, out var previousLevel));
        Assert.Equal(1, previousLevel);
        Assert.True(store.TryTakeIncrease(
            increased, out _, out _, out previousLevel));
        Assert.Equal(1, previousLevel);

        store.Accept(actionId, elementId, 3);
        Assert.False(store.TryTakeIncrease(
            increased, out _, out _, out _));

        var removed = PublicationTable<WorldHarvestAction>.Create(new[]
        {
            new WorldHarvestAction(
                actionId, elementId, 0, 5, true,
                new BigDouble(100), new BigDouble(100), false),
        });
        Assert.False(store.TryTakeIncrease(
            removed, out _, out _, out _));
    }

    [Fact]
    public void SharedWorldPublishesAnActivePairAndItsOrderedBaseCost()
    {
        var resource = new global::ResourceSO();
        var action = new global::HarvestActionSO();
        var element = new global::HarvestElementSO { masteryLevel = 4 };
        element.actionReference.actionCost.costs.Add(
            new global::ResourceTuple(resource, new global::BigDouble(1, 0)));
        var selected = new global::HarvestActionInstance(element, action);
        var active = new global::HarvestActionInstanceListVariable();
        active.AddInstance(selected, 1);

        global::IdScriptableObject.RuntimeLookup[
            KnownEntities.ActiveHarvestActions.Uuid] = active;
        try
        {
            var collector = new GameWorldCollector();
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            collector.Collect(frame);
            var world = GameWorldFrameDeriver.Build(frame);

            Assert.Equal(WorldHarvestActionCaptureState.Complete,
                world.HarvestActionCaptureState);
            Assert.Equal(1, world.HarvestActions.Count);
            var pair = world.HarvestActions[0];
            Assert.Equal(action.GetGuid(), pair.ActionId);
            Assert.Equal(element.GetGuid(), pair.ElementId);
            Assert.Equal(1, pair.CurrentLevel);
            Assert.Equal(2, world.HarvestActionCosts.Count);
            var cost = world.HarvestActionCosts[0];
            Assert.Equal(WorldHarvestActionCostKind.Base, cost.Kind);
            Assert.Equal(resource.GetGuid(), cost.ResourceId);
        }
        finally
        {
            global::IdScriptableObject.RuntimeLookup.Remove(
                KnownEntities.ActiveHarvestActions.Uuid);
        }
    }

    [Fact]
    public void MissingAndPresentEmptyActiveListsRemainDistinct()
    {
        global::IdScriptableObject.RuntimeLookup.Remove(
            KnownEntities.ActiveHarvestActions.Uuid);
        var collector = new GameWorldCollector();
        var missingFrame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var missingReport = collector.Collect(missingFrame);
        var missing = GameWorldFrameDeriver.Build(missingFrame);

        Assert.Equal(
            WorldHarvestActionCaptureState.ContractUnavailable,
            missing.HarvestActionCaptureState);
        Assert.False(missingReport.IsComplete);

        var active = new global::HarvestActionInstanceListVariable();
        global::IdScriptableObject.RuntimeLookup[
            KnownEntities.ActiveHarvestActions.Uuid] = active;
        try
        {
            var emptyFrame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var emptyReport = collector.Collect(emptyFrame);
            var empty = GameWorldFrameDeriver.Build(emptyFrame);

            Assert.Equal(
                WorldHarvestActionCaptureState.Complete,
                empty.HarvestActionCaptureState);
            Assert.Equal(0, empty.HarvestActions.Count);
            Assert.True(emptyReport.For("active Druidry actions").IsClean);
        }
        finally
        {
            global::IdScriptableObject.RuntimeLookup.Remove(
                KnownEntities.ActiveHarvestActions.Uuid);
        }
    }

    [Fact]
    public void WrongListIdentityAndDuplicatePairsFailAtomically()
    {
        var wrongIdentity = new global::HarvestActionInstanceListVariable();
        wrongIdentity.SetGuid(Guid.NewGuid());
        global::IdScriptableObject.RuntimeLookup[
            KnownEntities.ActiveHarvestActions.Uuid] = wrongIdentity;
        try
        {
            var collector = new GameWorldCollector();
            var wrongFrame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var wrongReport = collector.Collect(wrongFrame);
            var wrong = GameWorldFrameDeriver.Build(wrongFrame);

            Assert.Equal(
                WorldHarvestActionCaptureState.Malformed,
                wrong.HarvestActionCaptureState);
            Assert.Equal(0, wrong.HarvestActions.Count);
            Assert.False(wrongReport.IsComplete);

            var action = new global::HarvestActionSO();
            var element = new global::HarvestElementSO();
            var duplicate = new global::HarvestActionInstanceListVariable();
            duplicate.value.Add(new global::HarvestActionInstance(element, action));
            duplicate.value.Add(new global::HarvestActionInstance(element, action));
            global::IdScriptableObject.RuntimeLookup[
                KnownEntities.ActiveHarvestActions.Uuid] = duplicate;

            var duplicateFrame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var duplicateReport = collector.Collect(duplicateFrame);
            var duplicateWorld = GameWorldFrameDeriver.Build(duplicateFrame);

            Assert.Equal(
                WorldHarvestActionCaptureState.Malformed,
                duplicateWorld.HarvestActionCaptureState);
            Assert.Equal(0, duplicateWorld.HarvestActions.Count);
            Assert.Equal(0, duplicateWorld.HarvestActionCosts.Count);
            Assert.False(duplicateReport.IsComplete);
        }
        finally
        {
            global::IdScriptableObject.RuntimeLookup.Remove(
                KnownEntities.ActiveHarvestActions.Uuid);
        }
    }

    [Fact]
    public void OversizedCostListIsRejectedBeforePublication()
    {
        var resource = new global::ResourceSO();
        var action = new global::HarvestActionSO();
        var element = new global::HarvestElementSO();
        for (var index = 0; index < 4097; index++)
        {
            element.actionReference.actionCost.costs.Add(
                new global::ResourceTuple(resource, new global::BigDouble(1, 0)));
        }
        var active = new global::HarvestActionInstanceListVariable();
        active.value.Add(new global::HarvestActionInstance(element, action));
        global::IdScriptableObject.RuntimeLookup[
            KnownEntities.ActiveHarvestActions.Uuid] = active;
        try
        {
            var collector = new GameWorldCollector();
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var report = collector.Collect(frame);
            var world = GameWorldFrameDeriver.Build(frame);

            Assert.Equal(
                WorldHarvestActionCaptureState.LimitExceeded,
                world.HarvestActionCaptureState);
            Assert.Equal(0, world.HarvestActions.Count);
            Assert.Equal(0, world.HarvestActionCosts.Count);
            Assert.False(report.IsComplete);
        }
        finally
        {
            global::IdScriptableObject.RuntimeLookup.Remove(
                KnownEntities.ActiveHarvestActions.Uuid);
        }
    }

    [Fact]
    public void PublishedSnapshotSurvivesReusableFrameOverwrite()
    {
        var action = new global::HarvestActionSO();
        var element = new global::HarvestElementSO();
        var active = new global::HarvestActionInstanceListVariable();
        active.value.Add(new global::HarvestActionInstance(element, action));
        global::IdScriptableObject.RuntimeLookup[
            KnownEntities.ActiveHarvestActions.Uuid] = active;
        try
        {
            var collector = new GameWorldCollector();
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            collector.Collect(frame);
            var first = GameWorldFrameDeriver.Build(frame);

            active.value.Clear();
            collector.Collect(frame);
            var second = GameWorldFrameDeriver.Build(frame);

            Assert.Equal(1, first.HarvestActions.Count);
            Assert.Equal(0, second.HarvestActions.Count);
        }
        finally
        {
            global::IdScriptableObject.RuntimeLookup.Remove(
                KnownEntities.ActiveHarvestActions.Uuid);
        }
    }
}
