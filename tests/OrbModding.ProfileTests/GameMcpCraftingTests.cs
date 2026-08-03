using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCraftingTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("f2000000-0000-0000-0000-000000000001");
    private static readonly Guid QueueId =
        Guid.Parse("f2000000-0000-0000-0000-000000000002");
    private static readonly Guid AutomationQueueId =
        Guid.Parse("f2000000-0000-0000-0000-000000000004");
    private static readonly Guid ResourceId =
        Guid.Parse("f2000000-0000-0000-0000-000000000003");

    [Fact]
    public void ToolAdvertisesOneRecipeUuidMutationWithoutGenerationOrReceiptInputs()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_craft");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "uuid" }, schema["required"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.NotNull(schema["properties"]!["mode"]);
        Assert.Equal(
            new[] { "craft", "automate", "cancel_manual", "cancel_automation" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["amount"]);
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_craft",
            new JObject
            {
                ["mode"] = "automate",
                ["uuid"] = RecipeId.ToString("D"),
            });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
        Assert.Equal("automate", operation.Mode);
    }

    [Fact]
    public void ValidationNamesMissingUuidAndRejectsRemovedGenerationMetadata()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_craft",
                ["arguments"] = new JObject(),
            }));
        var accepted = router.Handle(GameMcpAcceptanceFixture.Request(
            2,
            "tools/call",
            new JObject
            {
                ["name"] = "game_craft",
                ["arguments"] = new JObject
                {
                    ["uuid"] = RecipeId.ToString("D"),
                    ["worldGeneration"] = 17,
                },
            }));

        var missingErrors = Assert.IsType<JArray>(
            missing.Body!["error"]!["data"]!["validationErrors"]);
        Assert.Contains(
            missingErrors.Values<JObject>(),
            error => (string?)error!["code"] == "missing_required" &&
                     (string?)error["field"] == "uuid");
        Assert.Equal(-32602, (int?)accepted.Body?["error"]?["code"]);
        Assert.Contains(
            accepted.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error!["code"] == "unexpected_field" &&
                     (string?)error["field"] == "worldGeneration");
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void PreDecisionRowCarriesNamedExactCostsHoldingsAffordabilityAndQueueState()
    {
        var result = Json(GameMcpWorldQuery.GetRow(
            Context(),
            "crafting-recipes",
            RecipeId.ToString("D"),
            "CraftingRecipeSO"));

        Assert.Equal("available", (string?)result["status"]);
        Assert.Null(result["worldGeneration"]);
        var row = result["row"]!;
        Assert.Equal("Craft Sigils", (string?)row["name"]);
        Assert.Equal(RecipeId.ToString("D"), (string?)row["uuid"]);
        Assert.Equal("queue_stack", (string?)row["execution"]);
        Assert.Equal(2, (int)row["purchaseAmount"]!);
        Assert.Equal(4, (int)row["queuedAmount"]!);
        Assert.True((bool)row["canStart"]!);
        Assert.Equal("Sigil Queue", (string?)row["queue"]!["queue"]!["name"]);
        Assert.Equal(1, (int)row["queue"]!["used"]!);
        Assert.Equal(3, (int)row["queue"]!["maximum"]!);
        var cost = Assert.Single(row["nextCosts"]!).Value<JObject>()!;
        Assert.Equal("Arcane Dust", (string?)cost["resource"]!["name"]);
        Assert.Equal("75", (string?)cost["cost"]);
        Assert.Equal("900", (string?)cost["amount"]);
        Assert.True((bool)cost["affordable"]!);
        Assert.True((bool)row["cancelManual"]!["available"]!);
        Assert.Equal(3, (int)row["automation"]!["amount"]!);
        Assert.True((bool)row["automation"]!["available"]!);
        Assert.True((bool)row["automation"]!["canCancel"]!);
    }

    [Fact]
    public void QueueContentsAreOrderedNamedAndOmitManualAutomationFields()
    {
        var result = Json(GameMcpWorldQuery.ListRows(
            Context(),
            "crafting-queue-entries",
            0,
            10));

        Assert.Equal("available", (string?)result["status"]);
        var rows = result["rows"]!.Values<JObject>().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("Sigil Queue", (string?)rows[0]?["queue"]?["name"]);
        Assert.Equal("Craft Sigils", (string?)rows[0]?["recipe"]?["name"]);
        Assert.Equal(0, (int?)rows[0]?["slot"]);
        Assert.Equal("4", (string?)rows[0]?["amount"]);
        Assert.False((bool?)rows[0]?["automatic"]);
        Assert.Null(rows[0]?["repetitions"]);
        Assert.Equal("Auto Sigil Queue", (string?)rows[1]?["queue"]?["name"]);
        Assert.Equal(1, (int?)rows[1]?["slot"]);
        Assert.Equal("3", (string?)rows[1]?["amount"]);
        Assert.True((bool?)rows[1]?["automatic"]);
        Assert.Equal(3, (int?)rows[1]?["repetitions"]);
    }

    [Fact]
    public void CommittedPostStateIsTheNamedQueueDeltaWithoutAuditCeremony()
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Crafting, 15, 8, "craft", RecipeId, Guid.Empty,
            "CraftingRecipeSO", string.Empty, 1, string.Empty, string.Empty,
            false, false, frameContext: Context(queuedAmount: 4));
        var committed = GameMcpCommandResult.Committed("committed", 15, 8);
        var postState = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(queuedAmount: 6), command, committed));

        Assert.Equal("Craft Sigils", (string?)postState["name"]);
        Assert.Equal("4", (string?)postState["queued"]!["before"]!);
        Assert.Equal("6", (string?)postState["queued"]!["after"]!);
        Assert.Null(postState["nextCosts"]);
        Assert.Null(postState["queue"]);
        Assert.Null(postState["receipt"]);
        Assert.Null(postState["payment"]);
        Assert.Null(postState["worldGeneration"]);
    }

    [Theory]
    [InlineData("automate", 3, 5)]
    [InlineData("cancel_automation", 5, 2)]
    public void Automated_modes_return_only_the_settled_quantity_change(
        string mode,
        int before,
        int after)
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Crafting, 15, 8, mode, RecipeId, Guid.Empty,
            "CraftingRecipeSO", string.Empty, 1, string.Empty, string.Empty,
            false, false, frameContext: Context(automationQuantity: before));
        var committed = GameMcpCommandResult.Committed("committed", 15, 8);

        var postState = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(automationQuantity: after), command, committed));

        Assert.Equal("Craft Sigils", (string?)postState["name"]);
        Assert.Equal(before, (int)postState["automation"]!["before"]!);
        Assert.Equal(after, (int)postState["automation"]!["after"]!);
        Assert.Null(postState["receipt"]);
    }

    [Fact]
    public void SuccessDetailsYieldToPostStateWhileFaultNamesTheMissingOutcome()
    {
        var fault = new CraftingPlayerSubmission(
            RecipeId,
            CraftingPlayerPreflight.VerificationFailed,
            CraftingPlayerNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(2, 1, 0),
            "queue did not change");
        var committed = new CraftingPlayerSubmission(
            RecipeId,
            CraftingPlayerPreflight.Proceeded,
            CraftingPlayerNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(2, 1, 1),
            "queue changed");

        var failure = Json(GameMcpCraftingProjection.Project(in fault));
        var success = Json(GameMcpCraftingProjection.Project(in committed));

        Assert.Equal("requested craft completion", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
        Assert.Empty(success.Properties());
    }

    private static GameMcpFrameContext Context(
        int queuedAmount = 4,
        int automationQuantity = 3)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(World(queuedAmount, automationQuantity), new WorldGeneration(2201));
        return GameMcpTestHarness.Context(
            publisher.ReadLatest(), configurationGeneration: 8, lifecycleGeneration: 15);
    }

    private static GameWorldState World(int queuedAmount = 4, int automationQuantity = 3)
    {
        var reading = new RawCraftingRecipeSample(
            RecipeId,
            visible: true,
            canBuyAtStartingQuantity: true,
            startingQuantity: BigDouble.One,
            useQuantityAsLevel: false,
            timeToComplete: 5,
            outputWithinCapacity: true,
            typeCount: 0,
            authoredInputCount: 1,
            generatedOutputCount: 0,
            consumableOutputCount: 0,
            engagementEffectCount: 0,
            completionEffectCount: 0);
        var recipe = new WorldCraftingRecipe(
            in reading,
            PublicationTable<WorldCraftingRecipeTypeLink>.Empty,
            PublicationTable<WorldCraftingRecipeResource>.Empty,
            PublicationTable<WorldCraftingRecipeConsumableOutput>.Empty,
            PublicationTable<WorldCraftingRecipeDrainBlock>.Empty);
        return new GameWorldState
        {
            CollectedAtEpoch = 15,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = Identities(),
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[] { recipe }),
            CraftingDecisions = PublicationTable<WorldCraftingDecision>.Create(new[]
            {
                new WorldCraftingDecision(
                    RecipeId,
                    WorldCraftingPipeline.QueueStack,
                    new BigDouble(2),
                    new BigDouble(queuedAmount),
                    QueueId,
                    queueUsed: 1,
                    queueMaximum: 3,
                    canStart: true,
                    reasonCode: "ready",
                    automationQuantity: automationQuantity,
                    automationUsed: 1,
                    automationMaximum: 3,
                    canAutomate: true),
            }),
            CraftingQueueEntries = PublicationTable<WorldCraftingQueueEntry>.Create(new[]
            {
                new WorldCraftingQueueEntry(
                    QueueId, 0, RecipeId, new BigDouble(queuedAmount), false, 0),
                new WorldCraftingQueueEntry(
                    AutomationQueueId, 1, RecipeId,
                    new BigDouble(automationQuantity), true, automationQuantity),
            }),
            CraftingDecisionCosts = PublicationTable<WorldCraftingDecisionCost>.Create(new[]
            {
                new WorldCraftingDecisionCost(
                    RecipeId, ResourceId, new BigDouble(75), new BigDouble(900)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Clean("crafting-recipes"),
                Clean("crafting-recipe-state"),
                Clean("crafting-decisions"),
                Clean("resources"),
            }),
        };
    }

    private static EntityIdentityCatalogSnapshot Identities()
    {
        var rows = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(
                RecipeId, "CraftingRecipeSO", "Craft Sigils", "craftSigils"),
            new EntityIdentityName(
                QueueId, "CraftingInstanceListVariable", "Sigil Queue", "sigilQueue"),
            new EntityIdentityName(
                AutomationQueueId, "CraftingInstanceListVariable",
                "Auto Sigil Queue", "autoSigilQueue"),
            new EntityIdentityName(
                ResourceId, "ResourceSO", "Arcane Dust", "arcaneDust"),
        }).OrderBy(row => row.EntityId).ToArray();
        return EntityIdentityCatalogSnapshot.Bound(15, rows);
    }

    private static WorldCollectionCategoryStatus Clean(string category) =>
        new(category, WorldCategoryOutcome.Collected, 1, 0, string.Empty);

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, Identities()));

    private static JObject Json(GameMcpObjectBuilder value) => Json(value.Freeze());
}
