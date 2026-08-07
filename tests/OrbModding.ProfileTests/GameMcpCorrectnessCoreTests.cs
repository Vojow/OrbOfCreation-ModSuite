using System;
using Newtonsoft.Json.Linq;
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
    public void Player_facing_costs_apply_the_resources_quality_discount()
    {
        Assert.Equal("1.88e491", PlayerFacingCost(
            new BigDouble(3.1d, 523), new BigDouble(1.65d, 34)));
        Assert.Equal("1.56e508", PlayerFacingCost(
            new BigDouble(1.82d, 529), new BigDouble(1.17d, 23)));
        Assert.Equal("9.03e169", PlayerFacingCost(
            new BigDouble(9.96d, 205), new BigDouble(1.103d, 38)));
    }

    /// <summary>
    /// The four bandwidth/inverted quadrants, plus the live rows that exposed the old rule:
    /// <c>atCapacity</c> answers in the coordinate <c>amount</c> is published in, so an inverted
    /// counter is full when its displayed number reaches the ceiling. Potion Toxicity showing 0 of
    /// its tolerance is not at capacity; Stability showing its whole pool is.
    /// </summary>
    [Fact]
    public void ResourceCoordinatesCoverEveryBandwidthAndInvertedQuadrant()
    {
        var ordinaryId = Guid.Parse("36666666-6666-4666-8666-666666666666");
        var spellCapacityId = Guid.Parse("37777777-7777-4777-8777-777777777777");
        var potionToxicityId = Guid.Parse("38888888-8888-4888-8888-888888888888");
        var glyphUpgradesId = Guid.Parse("39999999-9999-4999-8999-999999999999");
        var stabilityId = Guid.Parse("3aaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var timeAdvancementId = Guid.Parse("3bbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var arcanumId = Guid.Parse("3ccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(ordinaryId, "ResourceSO", "Knowledge", "knowledge"),
                new EntityIdentityName(spellCapacityId, "ResourceSO", "Spell Capacity", "weightSpell"),
                new EntityIdentityName(potionToxicityId, "ResourceSO", "Potion Toxicity", "potionToxicity"),
                new EntityIdentityName(glyphUpgradesId, "ResourceSO", "Glyph Upgrades", "glyphUpgrades"),
                new EntityIdentityName(stabilityId, "ResourceSO", "Stability", "stability"),
                new EntityIdentityName(timeAdvancementId, "ResourceSO", "Time Advancement", "timeAdvancement"),
                new EntityIdentityName(arcanumId, "ResourceSO", "Arcanum", "arcanum"),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                Derived(ordinaryId, held: 3, bandwidth: false, inverted: false),
                Derived(spellCapacityId, held: 3, bandwidth: true, inverted: false),
                Derived(potionToxicityId, held: 10, bandwidth: false, inverted: true),
                Derived(glyphUpgradesId, held: 10, bandwidth: true, inverted: true),
                Derived(stabilityId, held: 0, bandwidth: false, inverted: true),
                Derived(timeAdvancementId, held: 4, bandwidth: false, inverted: true),
                Derived(arcanumId, held: 10, bandwidth: false, inverted: false),
            }),
            SpellCosts = PublicationTable<WorldSpellCost>.Create(new[]
            {
                new WorldSpellCost(0, WorldSpellCostKind.Immediate,
                    ordinaryId, new BigDouble(100)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "resources", WorldCategoryOutcome.Collected, 7, 0, string.Empty),
            }),
        };

        AssertCoordinates(
            world, 0, ordinaryId, display: "3", spendable: 3, cost: 50, atCapacity: false);
        AssertCoordinates(
            world, 1, spellCapacityId, display: "3", spendable: 7, cost: 100, atCapacity: false);
        AssertCoordinates(
            world, 2, potionToxicityId, display: "0", spendable: 10, cost: 50, atCapacity: false);
        AssertCoordinates(
            world, 3, glyphUpgradesId, display: "0", spendable: 0, cost: 100, atCapacity: false);
        AssertCoordinates(
            world, 4, stabilityId, display: "10", spendable: 0, cost: 50, atCapacity: true);
        AssertCoordinates(
            world, 5, timeAdvancementId, display: "6", spendable: 4, cost: 50, atCapacity: false);
        AssertCoordinates(
            world, 6, arcanumId, display: "10", spendable: 10, cost: 50, atCapacity: true);

        var costs = Assert.IsType<JArray>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.ProjectEquippedSpellCosts(
                world, 0, WorldSpellCostKind.Immediate).Freeze(),
            world.EntityIdentities));
        var cost = Assert.Single(costs.Values<JObject>());
        Assert.Equal("50", (string?)cost["cost"]);
        Assert.Equal("3", (string?)cost["spendableAmount"]);
        Assert.False((bool)cost["affordable"]!);
    }

    private static void AssertCoordinates(
        GameWorldState world,
        int index,
        Guid resourceId,
        string display,
        int spendable,
        int cost,
        bool atCapacity)
    {
        var row = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.ProjectResource(world, world.Resources[index]),
            world.EntityIdentities));
        Assert.Equal(display, (string?)row["amount"]);
        Assert.Equal("10", (string?)row["capacity"]);
        Assert.Equal(atCapacity, (bool)row["atCapacity"]!);
        Assert.Equal(new BigDouble(spendable),
            GameMcpWorldQuery.SpendableAmount(world, resourceId, BigDouble.Zero));
        Assert.Equal(new BigDouble(cost),
            GameMcpWorldQuery.PlayerFacingCost(world, resourceId, new BigDouble(100)));
    }

    [Fact]
    public void Skipped_unaffordable_purchase_names_the_short_resource_and_amounts()
    {
        var target = Guid.Parse("31111111-1111-4111-8111-111111111111");
        var resource = Guid.Parse("32222222-2222-4222-8222-222222222222");
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resource, new BigDouble(2), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
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
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                new WorldResource(in reading, true, new BigDouble(8), 0.2d, false,
                    new BigDouble(4), BigDouble.Zero),
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
        Assert.Equal("Needs 50 Knowledge, but only 2 is spendable.", result.Reason);
    }

    [Fact]
    public void Maxed_purchase_refusal_uses_the_same_semantic_reason_as_the_read()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444444");
        var reading = new RawUpgradeSample(
            target, level: 10, maxLevel: 10, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 10);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, true, true, 0, 10, false, 0d),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false, false);

        var result = AutomataServiceCycleRuntime.ProjectPurchaseRefusal(
            command, world, ServiceActionResult.Skipped(CommonActionResultCodes.Skipped),
            1, 1);

        Assert.NotNull(result);
        Assert.Equal("already_maxed", result!.Code);
        Assert.Contains("maximum level", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_tool_redirect_uses_the_created_equipments_player_action()
    {
        var target = Guid.Parse("35555555-5555-4555-8555-555555555555");
        var equipment = new WorldEquipment(
            target, isCreated: true, discRarityLevel: 0, masteryXp: BigDouble.Zero,
            masteryLevel: 0, isRequiredDiscovery: false, power: BigDouble.One,
            baseLevel: BigDouble.One, experienceRateMod: BigDouble.One,
            equippedLevel: 0, attuningLevel: 0, attunementTimeLeft: 0d,
            baseXpRate: BigDouble.Zero);
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(target, "EquipmentSO", "Static Gauntlets", "static"),
            }),
            Equipment = PublicationTable<WorldEquipment>.Create(new[] { equipment }),
        };

        Assert.True(GameMcpEntityCapabilityMap.TryOwningTool(
            world, target, out var category, out var nativeType, out var tool));
        Assert.Equal("equipment", category);
        Assert.Equal("EquipmentSO", nativeType);
        Assert.Equal("game_equipment", tool);
    }

    private static string PlayerFacingCost(BigDouble nominal, BigDouble quality)
    {
        var resource = Guid.NewGuid();
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resource, BigDouble.Zero, new BigDouble(-1), true,
            BigDouble.Zero, BigDouble.Zero, quality,
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                new WorldResource(in reading, true, BigDouble.Zero, 0d, false,
                    BigDouble.Zero, BigDouble.Zero),
            }),
        };

        return GameMcpNumberFormatter.Format(GameMcpWorldQuery.PlayerFacingCost(
            world, resource, nominal));
    }

    private static WorldResource Derived(Guid resourceId, int held, bool bandwidth, bool inverted)
    {
        var rateInputs = default(RawResourceRateInputs);
        var modifiers = default(RawResourceModifiers);
        var traits = Traits(bandwidth, inverted);
        var reading = new RawResourceSample(
            resourceId, new BigDouble(held), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty,
            in rateInputs, in traits, in modifiers);
        return GameWorldStateDeriver.Derive(in reading, default(WorldFrameGlobals));
    }

    private static RawResourceTraits Traits(bool bandwidth, bool inverted) => new(
        0d, 0d, 0d,
        false, false, false,
        bandwidth, inverted, false, false,
        BigDouble.Zero, 0, 0, 0d, false, 0d,
        BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);

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
    public void ConceptSettlementWaitsForTheActiveCountItsResponsePublishes()
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
        // The game takes the queue increase a settlement before it submits it.
        var queuedOnly = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 440, 445, true, BigDouble.One),
            }),
        };
        var submitted = new GameWorldState
        {
            CollectedAtUtcTicks = completedAt + 1,
            AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(new[]
            {
                new WorldAlchemyInstance(recipe, 445, 445, true, BigDouble.One),
            }),
        };

        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(queuedOnly, generation: 42), 41, completedAt, command));
        Assert.True(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(submitted, generation: 42), 41, completedAt, command));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(submitted, generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));
        Assert.Equal(440, (int)delta["activeCount"]!["before"]!);
        Assert.Equal(445, (int)delta["activeCount"]!["after"]!);

        var timeout = GameMcpTestHarness.Json(GameMcpPostStateSettlement.TimedOut(
            command, GameMcpTestHarness.Context(queuedOnly, generation: 42)));
        Assert.Equal("requested_state_not_reached",
            (string?)timeout["postStateUnavailable"]!["reasonCode"]);
        Assert.Contains("active count is 440",
            (string?)timeout["postStateUnavailable"]!["reason"]);
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
        Assert.Equal("available", (string?)active["next"]!["availability"]);

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
        Assert.Equal("632", (string?)delta["committedLevel"]!["before"]);
        Assert.Equal("633", (string?)delta["committedLevel"]!["after"]);
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
        Assert.Equal(command.TargetId.ToString("D"), (string?)response["uuid"]);
        Assert.Equal(4, response.Count);
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
