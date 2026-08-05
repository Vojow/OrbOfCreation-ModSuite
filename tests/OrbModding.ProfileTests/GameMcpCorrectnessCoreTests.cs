using System;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCorrectnessCoreTests
{
    [Fact]
    public void Skipped_unaffordable_purchase_names_the_short_resource_and_amounts()
    {
        var target = Guid.Parse("31111111-1111-4111-8111-111111111111");
        var resource = Guid.Parse("32222222-2222-4222-8222-222222222222");
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(resource, "ResourceSO", "Knowledge", "knowledge"),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                new WorldPurchaseCost(
                    target, resource, new BigDouble(100), new BigDouble(100), 1,
                    new BigDouble(100),
                    PublicationTable<WorldPurchaseCostModifierSource>.Empty,
                    affordabilityEvaluated: true,
                    availableAmount: new BigDouble(2),
                    combinedEffectiveAmount: new BigDouble(100),
                    resourceAffordable: false,
                    resourceAffordabilityReasonCode: "insufficient_resource",
                    affordable: false,
                    affordabilityReasonCode: "unaffordable"),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false, false);
        var action = ServiceActionResult.Skipped(CommonActionResultCodes.Skipped);

        var result = AutomataServiceCycleRuntime.ProjectPurchaseRefusal(
            command, world, in action, 1, 1);

        Assert.NotNull(result);
        Assert.Equal("unaffordable", result!.Code);
        Assert.Equal("Needs 100 Knowledge, but only 2 is spendable.", result.Reason);
    }

    [Fact]
    public void ActionRegistrationDependsOnlyOnRuntimeAdmission()
    {
        Assert.True(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: true));
        Assert.False(GameMcpActionRegistrationPolicy.ShouldCompose(
            runtimeActivationAllowed: false));
    }

    [Fact]
    public void PostStateSettlementRejectsAWorldCapturedBeforeTheActionEvenIfPublishedLater()
    {
        Assert.Equal(1f, GameMcpPostStateSettlement.MaximumWaitSeconds);
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(41, 41));
        Assert.False(GameMcpPostStateSettlement.IsStrictlyNewer(40, 41));
        Assert.True(GameMcpPostStateSettlement.IsStrictlyNewer(42, 41));

        var actionCompleted = DateTime.UtcNow.Ticks;
        var staleCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted - 1 },
            generation: 42);
        var settledCapture = GameMcpTestHarness.Context(
            new GameWorldState { CollectedAtUtcTicks = actionCompleted + 1 },
            generation: 42);

        Assert.False(GameMcpPostStateSettlement.HasSettledWorld(
            staleCapture, 41, actionCompleted));
        Assert.True(GameMcpPostStateSettlement.HasSettledWorld(
            settledCapture, 41, actionCompleted));
    }

    [Fact]
    public void TimedOutPostStateCarriesExceptionalEvidenceInsteadOfAnEmptyCommit()
    {
        var value = GameMcpPostStateSettlement.TimedOut(
            GameMcpAcceptanceFixture.NativeCommand(),
            latest: null);

        Assert.Equal(
            "{\"postStateUnavailable\":{\"reasonCode\":\"post_state_timeout\",\"reason\":\"no world captured after the action exposed its committed post-state within one second\"}}",
            GameMcpTestHarness.Json(value).ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void ConceptSettlementWaitsForTheExactRequestedAmount()
    {
        var recipe = Guid.Parse("39999999-9999-4999-8999-999999999999");
        var completedAt = DateTime.UtcNow.Ticks;
        var before = new GameWorldState
        {
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 440, 440, true, BigDouble.One),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Concept, 9, 3, "add", recipe, Guid.Empty,
            "AlchemyRecipeSO", 5, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 41));
        var partial = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 442, 442, true, BigDouble.One),
            }),
        };
        var exact = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 445, 445, true, BigDouble.One),
            }),
        };

        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(partial, generation: 42), 41, completedAt, command));
        Assert.True(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(exact, generation: 42), 41, completedAt, command));
    }

    [Fact]
    public void PlotPostStateReportsTheObservedRequestedPairQuantityChange()
    {
        var plotId = KnownEntities.FruitTreePlot.Uuid;
        var actionId = KnownEntities.FruitTreeCollect.Uuid;
        var before = new GameWorldState
        {
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[]
            {
                PlotAction(plotId, actionId, instanceCount: 1,
                    maximumRemainingInstances: 4),
            }),
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(new[]
            {
                new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                    0, false, plotId, actionId, 2, true),
            }),
        };
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Harvest,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "add_plot_action",
            targetId: plotId,
            secondaryId: actionId,
            derivedNativeType: "PlotNodeSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            capture: false,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));
        var committed = GameMcpCommandResult.Committed("committed", 9, 3);
        var activeWorld = new GameWorldState
        {
            PlotActions = PublicationTable<WorldPlotAction>.Create(new[]
            {
                PlotAction(plotId, actionId, instanceCount: 1,
                    maximumRemainingInstances: 3),
            }),
            ActionQueueSlots = PublicationTable<WorldActionQueueSlot>.Create(new[]
            {
                new WorldActionQueueSlot(PlotLifecycleNativeBindings.ActiveActionsId,
                    0, false, plotId, actionId, 3, true),
            }),
        };

        var active = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(activeWorld), command, committed));
        Assert.Equal(2, (int)active["active"]!["before"]!);
        Assert.Equal(3, (int)active["active"]!["after"]!);
        Assert.Equal(plotId.ToString("D"), (string?)active["plot"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)active["plot"]!["name"]));
        Assert.Equal(actionId.ToString("D"), (string?)active["action"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)active["action"]!["name"]));
        Assert.True((bool)active["next"]!["available"]!);

        var fastActionDetails = new GameMcpObjectBuilder
        {
            ["active"] = new GameMcpObjectBuilder
            {
                ["before"] = 0,
                ["after"] = 1,
            }.Freeze(),
        }.Freeze();
        var fastAction = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(new GameWorldState
            {
                PlotActions = activeWorld.PlotActions,
            }),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3, fastActionDetails)));
        Assert.Equal(0, (int)fastAction["active"]!["before"]!);
        Assert.Equal(1, (int)fastAction["active"]!["after"]!);
        Assert.Equal(plotId.ToString("D"), (string?)fastAction["plot"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)fastAction["plot"]!["name"]));
        Assert.Equal(actionId.ToString("D"), (string?)fastAction["action"]!["uuid"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)fastAction["action"]!["name"]));
        Assert.Null(fastAction["postStateUnavailable"]);
    }

    [Fact]
    public void PlayerFacingAttributePurchaseUsesTheStructureCapabilityAndSettledLevelDelta()
    {
        var attributeId = Guid.Parse("f2000000-0000-0000-0000-000000000001");
        var before = new GameWorldState
        {
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(attributeId, 632),
            }),
        };
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            before,
            attributeId,
            GameMcpCommandKind.Purchase,
            out var reason), reason);

        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "structure",
            targetId: attributeId,
            secondaryId: Guid.Empty,
            derivedNativeType: "StructureSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            capture: false,
            saveCapture: false,
            frameContext: GameMcpTestHarness.Context(before));
        var after = new GameWorldState
        {
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(attributeId, 633),
            }),
        };

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(attributeId.ToString("D"), (string?)delta["uuid"]);
        Assert.Equal("632", (string?)delta["level"]!["before"]);
        Assert.Equal("633", (string?)delta["level"]!["after"]);
        Assert.Equal(2, delta.Count);
    }

    [Fact]
    public void ActionFailureProjectionCarriesOnlyStableCodeAndActionableReason()
    {
        var context = GameMcpTestHarness.Context(
            GameWorldStateDefaults.Empty,
            generation: 77);
        var operation = new GameMcpFrameOperation(
            1,
            new GameMcpOperationRequestBuilder
            {
                ToolName = "game_purchase",
                Classification = GameMcpOperationClass.Gameplay,
            }.Freeze());
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Purchase,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 3,
            mode: "purchase",
            targetId: Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd"),
            secondaryId: Guid.Empty,
            derivedNativeType: "StructureSO",
            amount: 1,
            payloadKey: string.Empty,
            payloadValue: string.Empty,
            capture: false,
            saveCapture: false,
            sourceOperation: operation,
            frameContext: context);

        var response = GameMcpTestHarness.Json(GameMcpCommandResult.Rejected(
            "native_rejected",
            "live native admission refused",
            observedLifecycleGeneration: 0,
            observedConfigurationGeneration: 0).Project(command));

        Assert.Equal("refused", (string?)response["status"]);
        Assert.Equal("native_rejected", (string?)response["code"]);
        Assert.Equal("live native admission refused", (string?)response["reason"]);
        Assert.Equal(3, response.Count);
        Assert.Null(response["worldGeneration"]);
        Assert.Null(response["readWith"]);
        Assert.Null(response["lifecycleGenerationMismatch"]);
        Assert.Null(response["configurationGenerationMismatch"]);
    }

    private static WorldStructure Structure(Guid id, int level)
    {
        var modifiers = new RawStructureModifiers(
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero);
        var reading = new RawStructureSample(
            id,
            Guid.Empty,
            new BigDouble(level),
            BigDouble.Zero,
            unlocked: true,
            queuedEchos: 0,
            completedEchos: 0,
            selfBonusLevels: 0,
            queueTimeLeft: BigDouble.Zero,
            currentBuildTime: BigDouble.Zero,
            flagged: false,
            baseLevel: 0,
            queueTimeTotal: 0,
            quantity: level,
            debugStructure: false,
            disabled: false,
            observableId: 0,
            insufficientReqPenaltyActive: false,
            bufferDevelopedQuantity: 0,
            costPerQuantityId: Guid.Empty,
            in modifiers);
        return new WorldStructure(
            in reading,
            new BigDouble(level),
            hasWorkInFlight: false,
            new BigDouble(level),
            developmentProgress: 0);
    }

    private static WorldPlotAction PlotAction(
        Guid plotId,
        Guid actionId,
        int instanceCount,
        int maximumRemainingInstances) =>
        new(
            new RawPlotAction(
                plotId,
                actionId,
                offeredCount: 1,
                instanceCount,
                PlotActionPrerequisiteEvidence.NativeLatchedTrue),
            elementCost: 2,
            elementCostKnown: true,
            hasEnoughForOneInstance: true,
            maximumRemainingInstances);
}
