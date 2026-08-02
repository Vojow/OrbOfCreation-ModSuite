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

public sealed class GameMcpChallengeTests
{
    private static readonly Guid First = Guid.Parse("f5000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("f5000000-0000-0000-0000-000000000002");
    private static readonly Guid Third = Guid.Parse("f5000000-0000-0000-0000-000000000003");

    [Fact]
    public void Tool_has_one_mode_conditioned_uuid_shape_and_no_generation_or_receipt_inputs()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_challenge");
        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode" }, schema["required"]!.Values<string>());
        Assert.Equal(new[] { "select", "activate", "abandon", "fetch_time", "fetch_prestige" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
    }

    [Fact]
    public void Validation_names_uuid_missing_or_forbidden_by_mode()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_challenge",
                ["arguments"] = new JObject { ["mode"] = "select" },
            }));
        var forbidden = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_challenge",
                ["arguments"] = new JObject
                {
                    ["mode"] = "fetch_time",
                    ["uuid"] = First.ToString("D"),
                },
            }));

        Assert.Contains(missing.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error?["code"] == "missing_required" && (string?)error?["field"] == "uuid");
        Assert.Contains(forbidden.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error?["code"] == "unexpected_for_mode" && (string?)error?["field"] == "uuid");
    }

    [Fact]
    public void Challenge_world_list_is_a_named_decision_complete_surface()
    {
        var world = World();
        var response = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 2501), "challenges", 0, 50).Freeze(), world);

        var state = response["challengeState"]!;
        Assert.Equal(2, (int)state["rerollsLeft"]!);
        Assert.Equal(3, (int)state["selectionMaximum"]!);
        Assert.Equal("Prismatic Trial", (string?)state["selected"]![0]!["name"]);
        Assert.Equal("Expanding Trial", (string?)state["timeOffers"]![1]!["name"]);
        var first = Assert.Single(response["rows"]!.Values<JObject>(),
            row => (string?)row?["uuid"] == First.ToString("D"))!;
        Assert.Equal("Prismatic Trial", (string?)first["name"]);
        Assert.Equal("queued", (string?)first["state"]);
        Assert.True((bool)first["select"]!["available"]!);
        Assert.True((bool)first["activate"]!["available"]!);
        Assert.Equal("1.2e1", (string?)first["nextDifficulty"]);
        Assert.Equal("3e1", (string?)first["nextReward"]);
        Assert.Null(first["receipt"]);
        Assert.Null(first["payment"]);
    }

    [Fact]
    public void Committed_poststate_eliminates_name_joins_and_readbacks_for_target_and_fetch_modes()
    {
        var world = World();
        var context = GameMcpTestHarness.Context(world, generation: 2502);
        var target = Json(GameMcpWorldQuery.ProjectChallengePostState(context, First), world);
        var fetch = Json(GameMcpWorldQuery.ProjectChallengePostState(context, Guid.Empty), world);

        Assert.Equal("Prismatic Trial", (string?)target["challenge"]!["name"]);
        Assert.Equal("Expanding Trial",
            (string?)target["challengeState"]!["timeOffers"]![1]!["name"]);
        Assert.Null(target["receipt"]);
        Assert.NotNull(fetch["challengeState"]);
        Assert.Null(fetch["challenge"]);
    }

    [Fact]
    public void Failure_names_the_missing_outcome_while_success_yields_to_newer_poststate()
    {
        var failure = new ChallengeSubmission(ChallengePreflight.VerificationFailed,
            ChallengeNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), "state unchanged");
        var success = new ChallengeSubmission(ChallengePreflight.Proceeded,
            ChallengeNativeStage.Verification, NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1), "state changed");

        var failed = Json(GameMcpChallengeProjection.Project(in failure), World());
        var committed = Json(GameMcpChallengeProjection.Project(in success), World());

        Assert.Equal("requested challenge transition", (string?)failed["missingOutcome"]);
        Assert.Single(failed.Properties());
        Assert.Empty(committed.Properties());
    }

    private static GameWorldState World()
    {
        var rows = new[]
        {
            new WorldChallenge(First, 1, 1, true, false, 5, 10, 12, 30,
                true, true, false, new BigDouble(12), new BigDouble(30)),
            new WorldChallenge(Second, 0, 0, true, false, 5, 10, 15, 40,
                true, false, false, new BigDouble(15), new BigDouble(40)),
            new WorldChallenge(Third, 2, 2, true, false, 5, 10, 20, 50,
                true, true, false, new BigDouble(20), new BigDouble(50)),
        };
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(First, "ChallengeSO", "Prismatic Trial", "prismaticTrial"),
            new EntityIdentityName(Second, "ChallengeSO", "Expanding Trial", "expandingTrial"),
            new EntityIdentityName(Third, "ChallengeSO", "Temporal Trial", "temporalTrial"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 21,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(21, identities),
            Challenges = PublicationTable<WorldChallenge>.Create(rows),
            ChallengeContext = new WorldChallengeContext(true, string.Empty, true, true, 2, 3, 3,
                PublicationTable<WorldChallengeReference>.Create(new[]
                {
                    new WorldChallengeReference(0, First),
                }),
                PublicationTable<WorldChallengeReference>.Create(new[]
                {
                    new WorldChallengeReference(0, First),
                    new WorldChallengeReference(1, Second),
                }),
                PublicationTable<WorldChallengeReference>.Create(new[]
                {
                    new WorldChallengeReference(0, Third),
                })),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("challenges", WorldCategoryOutcome.Collected, 3, 0, string.Empty),
                new WorldCollectionCategoryStatus("challenge decisions", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
