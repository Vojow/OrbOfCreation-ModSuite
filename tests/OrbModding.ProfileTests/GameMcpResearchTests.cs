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
    public void Validation_names_missing_uuid_and_removed_generation_argument()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_research",
                ["arguments"] = new JObject { ["mode"] = "develop" },
            }));
        var unexpected = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
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
        var unexpectedErrors = Assert.IsType<JArray>(unexpected.Body!["error"]!["data"]!["validationErrors"]);
        Assert.Contains(missingErrors.Values<JObject>(), error => (string?)error!["code"] == "missing_required" &&
                (string?)error["field"] == "uuid");
        Assert.Contains(unexpectedErrors.Values<JObject>(), error => (string?)error!["code"] == "unexpected_field" &&
                (string?)error["field"] == "worldGeneration");
    }

    [Fact]
    public void Research_row_is_a_named_complete_queue_decision_with_cost_holdings_and_progress()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.GetRow(GameMcpTestHarness.Context(world, 2801),
            "research", ResearchId.ToString("D"), "ResearchSO").Freeze(), world);
        var row = response["row"]!;

        Assert.Equal("Improved Casting", (string?)row["name"]);
        Assert.Equal("active", (string?)row["state"]);
        Assert.Equal("3e0", (string?)row["queuedLevels"]);
        Assert.Equal("queue", (string?)row["develop"]!["route"]);
        Assert.Equal("3e0", (string?)row["develop"]!["maximumBatch"]);
        Assert.Equal("2e0", (string?)row["develop"]!["levels"]);
        Assert.True((bool)row["develop"]!["affordable"]!);
        var cost = Assert.Single(row["develop"]!["costs"]!).Value<JObject>()!;
        Assert.Equal("Arcana", (string?)cost["resource"]!["name"]);
        Assert.Equal("2e1", (string?)cost["cost"]);
        Assert.Equal("8e1", (string?)cost["amount"]);
        Assert.Equal("4e1", (string?)row["investment"]![0]!["invested"]);
        Assert.Equal("Insight", (string?)row["researchTypes"]![0]!["researchType"]!["name"]);
        Assert.Equal("2e0", (string?)row["researchTypes"]![0]!["remainingBonusLevels"]);
    }

    [Fact]
    public void Failure_keeps_decomposed_state_while_success_yields_to_fresh_world_poststate()
    {
        var before = State(active: false, developing: false, queued: 0, selfBonus: 0);
        var after = State(active: false, developing: false, queued: 0, selfBonus: 0);
        var receipt = new ResearchReceipt(ResearchActionKind.Develop, before, after);
        var failed = new ResearchSubmission(ResearchPreflight.VerificationFailed,
            ResearchNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), in receipt, "unchanged");
        var success = new ResearchSubmission(ResearchPreflight.Proceeded,
            ResearchNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), in receipt, "changed");

        var failure = Json(GameMcpResearchProjection.Project(in failed), World());
        var committed = Json(GameMcpResearchProjection.Project(in success), World());

        Assert.Equal("verification_failed", (string?)failure["preflight"]);
        Assert.Equal("develop", (string?)failure["requestedMode"]);
        Assert.True((bool)failure["quarantined"]!);
        Assert.NotNull(failure["before"]);
        Assert.Null(failure["payment"]);
        Assert.Null(failure["receipt"]);
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

    private static ResearchState State(bool active, bool developing, int queued, int selfBonus) =>
        new(true, true, 3, 1, 0, queued, 0, selfBonus, active, developing,
            1, 0, 1, 1, new BigDouble(0.5), true, true, true, 2, true, 10, 2);

    private static GameWorldState World()
    {
        var decision = new WorldResearchDecision(true, 3, 3, 2, 1,
            new BigDouble(30), new BigDouble(30), new BigDouble(0.5), true, 2, true,
            PublicationTable<WorldResearchCost>.Create(new[]
            {
                new WorldResearchCost(ResourceId, new BigDouble(20), new BigDouble(80)),
            }),
            PublicationTable<WorldResearchInvestment>.Create(new[]
            {
                new WorldResearchInvestment(ResourceId, new BigDouble(40),
                    new BigDouble(100), new BigDouble(60)),
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
