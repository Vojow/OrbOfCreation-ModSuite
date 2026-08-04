using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpRitualLifecycleTests
{
    private static readonly Guid RitualId =
        Guid.Parse("fa000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId =
        Guid.Parse("fa000000-0000-0000-0000-000000000002");

    [Fact]
    public void Tool_exposes_only_the_live_ritual_list_controls()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_ritual");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode", "uuid" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(
            new[] { "select", "deselect", "set_level", "activate", "cancel_duration", "end" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        var operation = GameMcpProtocolRouter.BuildOperation("game_ritual", new JObject
        {
            ["mode"] = "activate",
            ["uuid"] = RitualId.ToString("D"),
        });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
    }

    [Fact]
    public void Set_level_requires_level_and_other_modes_reject_it()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_ritual",
                ["arguments"] = new JObject
                {
                    ["mode"] = "set_level",
                    ["uuid"] = RitualId.ToString("D"),
                },
            }));
        var extra = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_ritual",
                ["arguments"] = new JObject
                {
                    ["mode"] = "select",
                    ["uuid"] = RitualId.ToString("D"),
                    ["level"] = 2,
                },
            }));

        Assert.Contains(missing.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "missing_required" &&
                     (string?)error["field"] == "level");
        Assert.Contains(extra.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "unexpected_for_mode" &&
                     (string?)error["field"] == "level");
    }

    [Fact]
    public void Selected_ritual_detail_carries_only_the_live_next_decisions_and_named_costs()
    {
        var world = World(selected: true, level: 4, activeInstances: 1);
        var response = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 801),
            "rituals", RitualId.ToString("D")).Freeze(), world);

        var row = response["row"]!;
        Assert.Equal("Moon Rite", (string?)row["name"]);
        Assert.True((bool)row["selected"]!);
        Assert.True((bool)row["setLevel"]!["available"]!);
        Assert.Equal(8, (int)row["setLevel"]!["maximum"]!);
        Assert.True((bool)row["activate"]!["available"]!);
        var cost = Assert.Single(row["activate"]!["costs"]!.Values<JObject>());
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("5", (string?)cost["amount"]);
        Assert.Equal("80", (string?)cost["spendableAmount"]);
        Assert.True((bool)row["cancelDuration"]!["available"]!);
    }

    [Fact]
    public void Unselected_ritual_has_no_speculative_price_ledger()
    {
        var world = World(selected: false, level: 0, activeInstances: 0);
        var response = Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 802),
            "rituals", RitualId.ToString("D")).Freeze(), world);

        var activate = response["row"]!["activate"]!;
        Assert.False((bool)activate["available"]!);
        Assert.Equal("not_selected", (string?)activate["reasonCode"]);
        Assert.Null(activate["affordable"]);
        Assert.Null(activate["costs"]);
        Assert.Null(activate["completionCosts"]);
    }

    [Fact]
    public void Settled_select_delta_uses_the_new_world_and_returns_the_next_decision()
    {
        var before = World(selected: false, level: 0, activeInstances: 0);
        var after = World(selected: true, level: 3, activeInstances: 0);
        var command = new GameMcpCommand(1, GameMcpCommandKind.RitualLifecycle,
            9, 3, "select", RitualId, Guid.Empty, "RitualSO",
            1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 91));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 92), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.False((bool)delta["selected"]!["before"]!);
        Assert.True((bool)delta["selected"]!["after"]!);
        Assert.True((bool)delta["next"]!["activate"]!["available"]!);
        Assert.Equal("5", (string?)delta["next"]!["activate"]!["costs"]![0]!["amount"]);
    }

    [Fact]
    public void Settled_end_delta_reports_the_observed_active_battle_clear()
    {
        var before = World(selected: true, level: 4, activeInstances: 0, inBattle: true);
        var after = World(selected: true, level: 4, activeInstances: 0, inBattle: false);
        var command = new GameMcpCommand(1, GameMcpCommandKind.RitualLifecycle,
            9, 3, "end", RitualId, Guid.Empty, "RitualSO",
            1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 93));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 94), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal(RitualId.ToString("D"), (string?)delta["uuid"]);
        Assert.True((bool)delta["activeBattle"]!["before"]!);
        Assert.False((bool)delta["activeBattle"]!["after"]!);
    }

    private static GameWorldState World(
        bool selected,
        int level,
        int activeInstances,
        bool inBattle = false)
    {
        var activation = selected
            ? PublicationTable<WorldRitualCost>.Create(new[]
                { new WorldRitualCost(ResourceId, new BigDouble(5)) })
            : PublicationTable<WorldRitualCost>.Empty;
        var completion = selected
            ? PublicationTable<WorldRitualCost>.Create(new[]
                { new WorldRitualCost(ResourceId, new BigDouble(2)) })
            : PublicationTable<WorldRitualCost>.Empty;
        var decision = new WorldRitualDecision(selected, 8, true, true,
            activation, completion);
        var modifiers = default(RawRitualModifiers);
        var ritual = new WorldRitual(RitualId, true, inBattle, activeInstances,
            6, 5, level, 0, 0, 0, 0, 0, 1, BigDouble.Zero, in modifiers,
            false, false, false, 0, 1, 20, 1d, 0, decision: decision);
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var resourceModifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ResourceId, new BigDouble(80), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in resourceModifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(20), 0.8, false,
            new BigDouble(80), BigDouble.Zero);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(RitualId, "RitualSO", "Moon Rite", "moon_rite"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Knowledge", "knowledge"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            Rituals = PublicationTable<WorldRitual>.Create(new[] { ritual }),
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("rituals", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("resources", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}
