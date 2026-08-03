using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpResearchTests
{
    private static readonly Guid ResearchId = Guid.Parse("f8000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId = Guid.Parse("f8000000-0000-0000-0000-000000000002");
    private static readonly Guid TypeId = Guid.Parse("f8000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_requires_mode_and_published_research_uuid_only()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_research");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode", "uuid" }, schema["required"]!.Values<string>());
        Assert.Equal(new[] { "develop", "pause", "resume", "cancel", "bonus" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["amount"]);
    }

    [Fact]
    public void Validation_names_missing_uuid_and_rejects_removed_generation_metadata()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_research",
                ["arguments"] = new JObject { ["mode"] = "develop" },
            }));
        var rejected = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_research",
                ["arguments"] = new JObject
                {
                    ["mode"] = "develop",
                    ["uuid"] = ResearchId.ToString("D"),
                    ["worldGeneration"] = 9,
                },
            }));

        var missingErrors = Assert.IsType<JArray>(missing.Body!["error"]!["data"]!["validationErrors"]);
        Assert.Contains(missingErrors.Values<JObject>(), error => (string?)error!["code"] == "missing_required" &&
                (string?)error["field"] == "uuid");
        Assert.Equal(-32602, (int?)rejected.Body?["error"]?["code"]);
        Assert.Contains(
            rejected.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "unexpected_field" &&
                     (string?)error["field"] == "worldGeneration");
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void Research_row_is_a_named_complete_queue_decision_with_cost_holdings_and_progress()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2801),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var row = response["row"]!;

        Assert.Equal("Improved Casting", (string?)row["name"]);
        Assert.Equal("active", (string?)row["state"]);
        Assert.Equal(3, (int)row["queuedLevels"]!);
        Assert.Equal("queue", (string?)row["develop"]!["route"]);
        Assert.Equal(3, (int)row["develop"]!["maximumBatch"]!);
        Assert.Equal(2, (int)row["develop"]!["levels"]!);
        Assert.True((bool)row["develop"]!["affordable"]!);
        var cost = Assert.Single(row["develop"]!["costs"]!).Value<JObject>()!;
        Assert.Equal("Arcana", (string?)cost["resource"]!["name"]);
        Assert.Equal("20", (string?)cost["cost"]);
        Assert.Equal("80", (string?)cost["amount"]);
        Assert.Equal("40", (string?)row["investment"]![0]!["invested"]);
        Assert.Equal("Insight", (string?)row["researchTypes"]![0]!["researchType"]!["name"]);
        Assert.Equal(2, (int)row["researchTypes"]![0]!["remainingBonusLevels"]!);
    }

    [Fact]
    public void Research_cost_uses_spendable_amount_while_native_affordability_remains_authoritative()
    {
        var world = World(
            developmentCostAffordable: false,
            spendableAmount: 1,
            investmentRemaining: 100);
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2803),
            "research", ResearchId.ToString("D")).Freeze(), world);
        var develop = response["row"]!["develop"]!;
        var cost = Assert.Single(develop["costs"]!).Value<JObject>()!;

        Assert.Equal("1", (string?)cost["amount"]);
        Assert.False((bool)develop["affordable"]!);
        Assert.Equal("unaffordable", (string?)develop["reasonCode"]);
        Assert.Null(cost["lifetimeAmount"]);
    }

    [Fact]
    public void Failure_names_the_missing_outcome_while_success_yields_to_fresh_world_poststate()
    {
        var failed = new ResearchSubmission(ResearchPreflight.VerificationFailed,
            ResearchNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), "unchanged");
        var success = new ResearchSubmission(ResearchPreflight.Proceeded,
            ResearchNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), "changed");

        var failure = Json(GameMcpResearchProjection.Project(in failed), World());
        var committed = Json(GameMcpResearchProjection.Project(in success), World());

        Assert.Equal("requested research transition", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
        Assert.Empty(committed.Properties());
    }

    [Fact]
    public void Committed_poststate_is_the_same_named_research_row_with_next_decisions()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.ProjectPostState(
            GameMcpTestHarness.Context(world, 2802), "research", ResearchId), world);

        Assert.Equal("Improved Casting", (string?)response["name"]);
        Assert.NotNull(response["develop"]);
        Assert.Null(response["pause"]);
        Assert.NotNull(response["cancel"]);
        Assert.Null(response["receipt"]);
        Assert.Null(response["payment"]);
    }

    private static GameWorldState World(
        bool developmentCostAffordable = true,
        double spendableAmount = 80,
        double investmentRemaining = 60)
    {
        var decision = new WorldResearchDecision(
            true,
            3,
            3,
            developmentCostAffordable ? 2 : 0,
            1,
            new BigDouble(30), new BigDouble(30), new BigDouble(0.5), true, 2,
            developmentCostAffordable,
            PublicationTable<WorldResearchCost>.Create(new[]
            {
                new WorldResearchCost(
                    ResourceId,
                    new BigDouble(20),
                    new BigDouble(spendableAmount)),
            }),
            PublicationTable<WorldResearchInvestment>.Create(new[]
            {
                new WorldResearchInvestment(ResourceId, new BigDouble(40),
                    new BigDouble(100), new BigDouble(investmentRemaining)),
            }),
            PublicationTable<WorldResearchTypeDecision>.Create(new[]
            {
                new WorldResearchTypeDecision(TypeId, 2, 1, 5),
            }));
        var modifiers = new RawResearchModifiers(BigDouble.Zero, BigDouble.Zero,
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero);
        var research = new WorldResearch(ResearchId, 1, 2, 0, 0, 10, 60,
            true, true, false, true, true, false, true, true, true, true, true, true,
            1, 1, 0, 1, 10, false, 2, 1, new BigDouble(60), 1, 1,
            PublicationTable<WorldResearchRequirementAdjustment>.Empty, in modifiers, in decision);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(ResearchId, "ResearchSO", "Improved Casting", "improvedCasting"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Arcana", "arcana"),
            new EntityIdentityName(TypeId, "ResearchTypeSO", "Insight", "insight"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(41, identities),
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("research", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
