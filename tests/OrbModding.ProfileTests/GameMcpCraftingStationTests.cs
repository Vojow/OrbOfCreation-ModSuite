using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCraftingStationTests
{
    private static readonly Guid StationId = Guid.Parse("fd000000-0000-0000-0000-000000000001");
    private static readonly Guid StructureId = Guid.Parse("fd000000-0000-0000-0000-000000000002");
    private static readonly Guid RecipeId = Guid.Parse("fd000000-0000-0000-0000-000000000003");
    private static readonly Guid FirstId = Guid.Parse("fd000000-0000-0000-0000-000000000004");
    private static readonly Guid SecondId = Guid.Parse("fd000000-0000-0000-0000-000000000005");
    private static readonly Guid OutputId = Guid.Parse("fd000000-0000-0000-0000-000000000006");
    private static readonly Guid ResourceId = Guid.Parse("fd000000-0000-0000-0000-000000000007");

    [Fact]
    public void Tool_exposes_exactly_the_three_selectors_level_and_activation_controls()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_brewing_station");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode", "uuid" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[] { "set_ingredient", "set_output", "set_level", "start", "stop" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        Assert.NotNull(tool["inputSchema"]!["properties"]!["selectionUuid"]);
        Assert.NotNull(tool["inputSchema"]!["properties"]!["slot"]);
        Assert.NotNull(tool["inputSchema"]!["properties"]!["level"]);

        var operation = GameMcpProtocolRouter.BuildOperation("game_brewing_station", new JObject
        {
            ["mode"] = "set_ingredient",
            ["uuid"] = StationId.ToString("D"),
            ["selectionUuid"] = FirstId.ToString("D"),
            ["slot"] = 0,
        });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
        Assert.Equal(1, operation.Amount);
    }

    [Fact]
    public void Mode_specific_fields_are_required_and_inapplicable_fields_are_rejected()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject
            {
                ["name"] = "game_brewing_station",
                ["arguments"] = new JObject
                {
                    ["mode"] = "set_ingredient",
                    ["uuid"] = StationId.ToString("D"),
                    ["slot"] = 0,
                },
            }));
        var extra = router.Handle(GameMcpAcceptanceFixture.Request(2, "tools/call",
            new JObject
            {
                ["name"] = "game_brewing_station",
                ["arguments"] = new JObject
                {
                    ["mode"] = "start",
                    ["uuid"] = StationId.ToString("D"),
                    ["level"] = 2,
                },
            }));

        Assert.Contains(missing.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "missing_required" &&
                     (string?)error["field"] == "selectionUuid");
        Assert.Contains(extra.Body!["error"]!["data"]!["validationErrors"]!.Values<JObject>(),
            error => (string?)error["code"] == "unexpected_for_mode" &&
                     (string?)error["field"] == "level");
    }

    [Fact]
    public void Detail_publishes_the_screen_selection_state_named_options_and_current_drain()
    {
        var world = World(FirstId, active: false, level: 3);
        var response = Json(GameMcpWorldQuery.GetRow(
            Context(world, generation: 901),
            "crafting-stations", StationId.ToString("D"), "CraftingStructure").Freeze(), world);

        var row = response["row"]!;
        Assert.True(row["name"]?.Type == JTokenType.String, row.ToString());
        Assert.Equal(StationId.ToString("D"), (string?)row["uuid"]);
        Assert.Equal("Brewing Station", (string?)row["name"]);
        Assert.Equal("Water", (string?)row["firstIngredient"]!["name"]);
        Assert.Equal("Leaf", (string?)row["secondIngredient"]!["name"]);
        Assert.Equal("Tonic", (string?)row["output"]!["name"]);
        Assert.True((bool)row["loaded"]!);
        Assert.True((bool)row["start"]!["available"]!);
        Assert.Equal("Water", (string?)row["ingredientOptions"]![0]![0]!["name"]);
        Assert.Equal("Leaf", (string?)row["ingredientOptions"]![1]![0]!["name"]);
        Assert.Equal("Tonic", (string?)row["outputOptions"]![0]!["name"]);
        Assert.Equal("Arcane Dust", (string?)row["drain"]![0]!["resource"]!["name"]);
        Assert.Equal("3", (string?)row["drain"]![0]!["amount"]);
        Assert.Equal("20", (string?)row["drain"]![0]!["spendableAmount"]);
    }

    [Fact]
    public void Settled_delta_uses_the_observed_station_and_includes_the_next_decision()
    {
        var before = World(Guid.Empty, active: false, level: 3);
        var after = World(FirstId, active: false, level: 3);
        var command = new GameMcpCommand(1, GameMcpCommandKind.CraftingStation,
            9, 3, "set_ingredient", StationId, FirstId, "CraftingStructure", string.Empty,
            1, string.Empty, string.Empty, false, false,
            frameContext: Context(before, generation: 91));

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            Context(after, generation: 92), command,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Null(delta["ingredient"]!["before"]);
        Assert.Equal("Water", (string?)delta["ingredient"]!["after"]!["name"]);
        Assert.Equal(0, (int)delta["ingredient"]!["slot"]!);
        Assert.True((bool)delta["next"]!["loaded"]!);
        Assert.True((bool)delta["next"]!["start"]!["available"]!);
        Assert.Equal("Tonic", (string?)delta["next"]!["outputOptions"]![0]!["name"]);
    }

    private static GameWorldState World(Guid first, bool active, int level)
    {
        var station = new WorldCraftingStation(StationId, StructureId, RecipeId,
            first, SecondId, OutputId, loaded: true, active, level, 1, 8);
        var options = new[]
        {
            new WorldCraftingStationOption(StationId,
                WorldCraftingStationOptionKind.FirstIngredient, FirstId, true),
            new WorldCraftingStationOption(StationId,
                WorldCraftingStationOptionKind.SecondIngredient, SecondId, true),
            new WorldCraftingStationOption(StationId,
                WorldCraftingStationOptionKind.Output, OutputId, true),
        };
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(ResourceId, new BigDouble(20), new BigDouble(100),
            true, BigDouble.Zero, BigDouble.Zero, new BigDouble(100),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false, false,
            false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(80), 0.2, false,
            new BigDouble(20), BigDouble.Zero);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(StructureId, "CraftingStructureSO", "Brewing Station", "brewing_station"),
            new EntityIdentityName(RecipeId, "Recipe", "Tonic Recipe", "tonic_recipe"),
            new EntityIdentityName(FirstId, "ResourceSO", "Water", "water"),
            new EntityIdentityName(SecondId, "ResourceSO", "Leaf", "leaf"),
            new EntityIdentityName(OutputId, "ResourceSO", "Tonic", "tonic"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Arcane Dust", "arcane_dust"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            CraftingStations = PublicationTable<WorldCraftingStation>.Create(new[] { station }),
            CraftingStationOptions = PublicationTable<WorldCraftingStationOption>.Create(options),
            CraftingStationDrains = PublicationTable<WorldCraftingStationDrain>.Create(new[]
            {
                new WorldCraftingStationDrain(StationId, ResourceId, new BigDouble(3)),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("crafting stations", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
                new WorldCollectionCategoryStatus("resources", WorldCategoryOutcome.Collected,
                    1, 0, string.Empty),
            }),
        };
    }

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));

    private static GameMcpFrameContext Context(GameWorldState world, ulong generation)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(generation));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }
}
