using System;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpSpellLoadoutTests
{
    private static readonly Guid FirstRecipeId =
        Guid.Parse("36375616-7476-4748-8c20-ba628933bea5");
    private static readonly Guid SecondRecipeId =
        Guid.Parse("02f55f76-bdba-4fa4-841b-da3a62b0d6db");
    private static readonly Guid FirstInstanceId =
        Guid.Parse("13b37dd5-44f7-4eb5-af6b-168454578466");
    private static readonly Guid SecondInstanceId =
        Guid.Parse("f40dfa54-2b96-4aee-97ec-5a8e8392a771");

    [Fact]
    public void ToolUsesOneAddRemoveMoveShapeAndBakesGlyphsOnlyOnAdd()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_spell_loadout");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(
            new[] { "mode" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "add", "remove", "move" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.NotNull(schema["properties"]!["spellRecipeUuid"]);
        Assert.NotNull(schema["properties"]!["glyphs"]);
        Assert.NotNull(schema["properties"]!["destination"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["detail"]);
        Assert.Null(schema["properties"]!["receipt"]);
    }

    [Fact]
    public void ConditionalDestinationValidationNamesTheExactField()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_spell_loadout",
                ["arguments"] = new JObject
                {
                    ["mode"] = "move",
                    ["spellInstanceUuid"] = FirstInstanceId.ToString("D"),
                },
            }));
        var unexpected = router.Handle(GameMcpAcceptanceFixture.Request(
            2,
            "tools/call",
            new JObject
            {
                ["name"] = "game_spell_loadout",
                ["arguments"] = new JObject
                {
                    ["mode"] = "remove",
                    ["spellInstanceUuid"] = FirstInstanceId.ToString("D"),
                    ["destination"] = 1,
                },
            }));

        Assert.Equal("destination", (string?)missing.Body!["error"]!["data"]!["validationErrors"]![0]!["field"]);
        Assert.Equal("missing_required", (string?)missing.Body["error"]!["data"]!["validationErrors"]![0]!["code"]);
        Assert.Equal("destination", (string?)unexpected.Body!["error"]!["data"]!["validationErrors"]![0]!["field"]);
        Assert.Equal("unexpected_for_mode", (string?)unexpected.Body["error"]!["data"]!["validationErrors"]![0]!["code"]);
    }

    [Fact]
    public void AddRequiresARecipeAndExplicitPossiblyEmptyGlyphLayout()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_spell_loadout",
                ["arguments"] = new JObject { ["mode"] = "add" },
            }));

        var fields = missing.Body!["error"]!["data"]!["validationErrors"]!
            .Values<JObject>()
            .Select(error => (string?)error["field"])
            .ToArray();
        Assert.Equal(new[] { "spellRecipeUuid", "glyphs" }, fields);
    }

    [Fact]
    public void SpellSlotReadCarriesNamedHoldingsAndEveryNextLoadoutDecision()
    {
        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(World()),
            "spell-slots",
            0,
            10));
        var rows = response["rows"]!.Values<JObject>().ToArray();
        Assert.Equal(3, rows.Length);

        var first = Assert.IsType<JObject>(rows[0]);
        var second = Assert.IsType<JObject>(rows[1]);
        var empty = Assert.IsType<JObject>(rows[2]);
        Assert.Equal(0, (int)first["slot"]!);
        Assert.True((bool)first["occupied"]!);
        Assert.Equal(FirstInstanceId.ToString("D"), (string?)first["spellInstance"]!["uuid"]);
        Assert.Equal("Gather Knowledge", (string?)first["spellInstance"]!["name"]);
        Assert.Equal("Gather Knowledge", (string?)first["spellRecipe"]!["name"]);
        Assert.True((bool)first["remove"]!["available"]!);
        Assert.True((bool)first["move"]!["available"]!);
        var destinations = response["moveDestinations"]!.Values<JObject>().ToArray();
        Assert.Equal(
            new[] { 0, 1, 2 },
            destinations.Select(row => (int)row["slot"]!));
        var occupiedDestination = destinations[1];
        var emptyDestination = destinations[2];
        Assert.Equal("Whirling Sorcery", (string?)occupiedDestination["occupant"]!["name"]);
        Assert.True((bool)emptyDestination["empty"]!);

        Assert.False((bool)second["remove"]!["available"]!);
        Assert.Equal("native_remove_refused", (string?)second["remove"]!["reasonCode"]);
        Assert.True((bool)second["casting"]!);
        Assert.False((bool)empty["occupied"]!);
        Assert.Null(empty["spellInstance"]);
    }

    [Fact]
    public void CommittedMutationReturnsOnlyTheCompleteNamedLoadoutPostState()
    {
        var submission = new SpellLoadoutSubmission(
            SpellLoadoutPreflight.Proceeded,
            SpellLoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(2, 1, 1),
            "the exact requested swap is observable");
        var mapped = SpellLoadoutActionResultMapper.Map(in submission);
        var command = Command("move", destinationSlot: 1);
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpSpellLoadoutProjection.Project(in submission));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectSpellLoadoutPostState(
            GameMcpTestHarness.Context(World(moved: true))));

        var success = GameMcpTestHarness.Json(terminal.Project(command));

        Assert.Equal(new[] { "status", "code", "loadout", "augmentOptions", "moveDestinations" },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("committed", (string?)success["code"]);
        Assert.Equal(2, (int)success["loadout"]!["loadBudget"]!["used"]!);
        Assert.Equal(3, (int)success["loadout"]!["loadBudget"]!["maximum"]!);
        Assert.True((bool)success["loadout"]!["loadBudget"]!["fitsAnotherSpell"]!);
        var slots = success["loadout"]!["slots"]!.Values<JObject>().ToArray();
        var firstSlot = Assert.IsType<JObject>(slots[0]);
        var secondSlot = Assert.IsType<JObject>(slots[1]);
        Assert.Equal("Whirling Sorcery", (string?)firstSlot["spellInstance"]!["name"]);
        Assert.Equal("Gather Knowledge", (string?)secondSlot["spellInstance"]!["name"]);
        Assert.NotNull(success["moveDestinations"]);
        Assert.NotNull(firstSlot["remove"]);
        Assert.Null(success["preflight"]);
        Assert.Null(success["before"]);
        Assert.Null(success["receipt"]);
        Assert.DoesNotContain("payment", success.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt", success.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailureNamesTheMissingOutcomeWithoutPersistentState()
    {
        var submission = new SpellLoadoutSubmission(
            SpellLoadoutPreflight.VerificationFailed,
            SpellLoadoutNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(2, 1, 0),
            "the requested swap was not observable");

        var failure = GameMcpTestHarness.Json(
            GameMcpSpellLoadoutProjection.Project(in submission));

        Assert.Equal("requested spell slot state", (string?)failure["missingOutcome"]);
        Assert.Single(failure.Properties());
    }

    [Fact]
    public void ReadAdmissionAndOperationOwnershipUseOneLoadoutCapability()
    {
        var world = World();
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world,
            FirstInstanceId,
            GameMcpCommandKind.SpellLoadout,
            out var admissionReason), admissionReason);
        Assert.True(GameMcpEntityCapabilityMap.Supports(
            "spell-slots", GameMcpCommandKind.SpellLoadout));

        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.False(ownership.TryCaptureSpellLoadoutMutationPermit());
        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.SpellLoadout,
            "move",
            out var scope,
            out var reason), reason);
        using (scope)
            Assert.True(ownership.TryCaptureSpellLoadoutMutationPermit());
        Assert.False(ownership.TryCaptureSpellLoadoutMutationPermit());
    }

    private static GameMcpCommand Command(string mode, int destinationSlot = 0) => new(
        1,
        GameMcpCommandKind.SpellLoadout,
        9,
        3,
        mode,
        FirstInstanceId,
        Guid.Empty,
        "Spell",
        string.Empty,
        destinationSlot + 1,
        string.Empty,
        string.Empty,
        false,
        false);

    private static GameWorldState World(bool moved = false)
    {
        var first = Slot(
            moved ? 1 : 0,
            FirstInstanceId,
            FirstRecipeId,
            canRemove: true,
            casting: false);
        var second = Slot(
            moved ? 0 : 1,
            SecondInstanceId,
            SecondRecipeId,
            canRemove: false,
            casting: true);
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell slots", WorldCategoryOutcome.Collected, 3, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "spell workbench", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            SpellWorkbench = new WorldSpellWorkbench(
                PublicationTable<WorldSpellWorkbenchGlyph>.Empty,
                PublicationTable<WorldSpellWorkbenchGlyph>.Empty,
                PublicationTable<WorldSpellWorkbenchCost>.Empty,
                true,
                2,
                3,
                true,
                4,
                12),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                moved ? second : first,
                moved ? first : second,
                new WorldSpellSlot(
                    2, Guid.Empty, Guid.Empty, false, false, false, false, false,
                    false, false, false, false, false, 0, 0, BigDouble.Zero),
            }),
        };
    }

    private static WorldSpellSlot Slot(
        int slot,
        Guid instance,
        Guid recipe,
        bool canRemove,
        bool casting) => new(
            slot,
            instance,
            recipe,
            true,
            casting,
            false,
            false,
            false,
            false,
            false,
            true,
            true,
            canRemove,
            true,
            1,
            1,
            BigDouble.Zero,
            4,
            4,
            0,
            4,
            false,
            true,
            PublicationTable<WorldSpellSlotGlyph>.Empty);
}
