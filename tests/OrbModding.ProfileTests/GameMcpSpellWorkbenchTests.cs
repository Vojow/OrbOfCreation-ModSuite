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

public sealed class GameMcpSpellWorkbenchTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("36375616-7476-4748-8c20-ba628933bea5");
    private static readonly Guid FirstGlyphId =
        Guid.Parse("81894d9f-4e91-43da-9f47-2a97d77a2294");
    private static readonly Guid SecondGlyphId =
        Guid.Parse("0f38b02c-b81a-4fcd-9e07-73e09bd38dee");
    private static readonly Guid ResourceId =
        Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");

    [Fact]
    public void OutputFirstWorkbenchToolIsAbsentFromThePlayerSurface()
    {
        Assert.DoesNotContain(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_spell_workbench");
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(
            new[] { "mode" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "preview", "confirm", "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.NotNull(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["detail"]);
        Assert.Null(schema["properties"]!["verbosity"]);
    }

    [Fact]
    public void ConfirmRequiresSurfaceAndComponentsNotAnOutputRecipe()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject { ["mode"] = "confirm" },
            }));

        Assert.Equal(-32602, (int)response.Body!["error"]!["code"]!);
        var errors = response.Body["error"]!["data"]!["validationErrors"]!
            .Values<JObject>().ToArray();
        Assert.Equal(new[] { "surface", "components" },
            errors.Select(error => (string?)error["field"]));
        Assert.All(errors, error => Assert.Equal("missing_required", (string?)error["code"]));
    }

    [Fact]
    public void ListAndGetExposeTheSameNamedPreDecisionEconomicsBeforeSelection()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: false,
            selected: false,
            discoveryAffordable: true,
            creationAffordable: true,
            hasEmptySlot: true));

        var list = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "spell-recipes", 0, 10));
        var listed = Assert.Single(list["rows"]!.Values<JObject>())!;
        var get = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D"), "SpellRecipeSO"));
        var exact = (JObject)get["row"]!;

        Assert.Equal(
            listed.Properties().Select(property => property.Name),
            exact.Properties().Select(property => property.Name));
        Assert.Equal("Gather Knowledge", (string?)listed["name"]);
        Assert.Equal("spell-recipes", (string?)listed["category"]);
        Assert.True((bool)listed["select"]!["available"]!);
        Assert.False((bool)listed["discover"]!["available"]!);
        Assert.Equal("selection_required", (string?)listed["discover"]!["reasonCode"]);
        var glyphs = listed["coreGlyphs"]!.Values<JObject>().ToArray();
        Assert.Equal(new[] { "Brew", "Insight" },
            glyphs.Select(glyph => (string?)glyph!["glyph"]!["name"]));
        Assert.Equal(new[] { "7", "3" },
            glyphs.Select(glyph => (string?)glyph!["ownedLevel"]));
        var cost = Assert.Single(listed["discover"]!["costs"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("4.4e3", (string?)cost["cost"]);
        Assert.Equal("9e6", (string?)cost["amount"]);
    }

    [Fact]
    public void SelectedRecipePublishesTheExactPaidStepAndCurrentHoldings()
    {
        var context = GameMcpTestHarness.Context(World(
            discovered: true,
            selected: true,
            discoveryAffordable: true,
            creationAffordable: true,
            hasEmptySlot: true,
            equipped: true));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "spell-recipes", RecipeId.ToString("D"), "SpellRecipeSO"));
        var row = response["row"]!;

        Assert.True((bool)row["selected"]!);
        Assert.Null(row["select"]);
        Assert.True((bool)row["create"]!["available"]!);
        var cost = Assert.Single(row["create"]!["costs"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("7.5e2", (string?)cost["cost"]);
        Assert.Equal("9e6", (string?)cost["amount"]);
        Assert.Equal(1, (int)row["loadBudget"]!["used"]!);
        Assert.Equal(3, (int)row["loadBudget"]!["maximum"]!);
        Assert.True((bool)row["loadBudget"]!["fitsAnotherSpell"]!);
        var equipped = Assert.Single(row["equipped"]!.Values<JObject>())!;
        Assert.Equal(0, (int)equipped["slot"]!);
        Assert.Equal("Gather Knowledge", (string?)equipped["spellInstance"]!["name"]);
    }

    [Fact]
    public void SuccessIsOnlyNamedPostStateWhileFailuresRetainEarnedEvidence()
    {
        var before = new SpellWorkbenchState(
            RecipeId, false, new[] { FirstGlyphId, SecondGlyphId },
            Array.Empty<Guid>(), Array.Empty<Guid>());
        var after = new SpellWorkbenchState(
            RecipeId, true, Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>());
        var evidence = new SpellWorkbenchEvidence(
            true, in before, in after, paymentInvoked: true);
        var submission = new SpellWorkbenchSubmission(
            SpellWorkbenchPreflight.Proceeded,
            SpellWorkbenchNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            in evidence,
            "the requested recipe is discovered");
        var mapped = SpellWorkbenchActionResultMapper.Map(in submission);
        var command = Command("discover");
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpSpellWorkbenchProjection.Project(in submission));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectPostState(
            GameMcpTestHarness.Context(World(
                discovered: true,
                selected: false,
                discoveryAffordable: true,
                creationAffordable: true,
                hasEmptySlot: true)),
            "spell-recipes",
            RecipeId));

        var success = GameMcpTestHarness.Json(terminal.Project(command));
        Assert.Equal(new[]
            {
                "status", "uuid", "name", "internalName", "category", "nativeType", "discovered",
                "masteryLevel", "selected", "coreGlyphs", "loadBudget", "select", "create",
            },
            success.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)success["status"]);
        Assert.Equal("Gather Knowledge", (string?)success["name"]);
        Assert.Equal("selection_required", (string?)success["create"]!["reasonCode"]);
        Assert.Null(success["preflight"]);
        Assert.Null(success["before"]);
        Assert.Null(success["after"]);
        Assert.Null(success["receipt"]);
        Assert.DoesNotContain("payment", success.ToString(), StringComparison.OrdinalIgnoreCase);

        var refusedSubmission = SpellWorkbenchSubmission.Reject(
            SpellWorkbenchPreflight.WrongSelection,
            "the selected recipe changed");
        var refusal = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.Project(in refusedSubmission));
        Assert.Empty(refusal.Properties());

        var failedSubmission = new SpellWorkbenchSubmission(
            SpellWorkbenchPreflight.VerificationFailed,
            SpellWorkbenchNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0),
            in evidence,
            "the target was not discovered");
        var failure = GameMcpTestHarness.Json(
            GameMcpSpellWorkbenchProjection.Project(in failedSubmission));
        Assert.Null(failure["preflight"]);
        Assert.True((bool)failure["paymentInvoked"]!);
        Assert.Equal("Gather Knowledge", (string?)failure["before"]!["resolvedRecipe"]!["name"]);
        Assert.Equal("Brew", (string?)failure["before"]!["coreGlyphs"]![0]!["name"]);
        Assert.Null(failure["quarantined"]);
    }

    [Fact]
    public void ReadAdmissionAndActionUseOneSpellRecipeCapability()
    {
        var world = World(
            discovered: false,
            selected: false,
            discoveryAffordable: true,
            creationAffordable: true,
            hasEmptySlot: true);

        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world,
            RecipeId,
            GameMcpCommandKind.SpellWorkbench,
            out var reason), reason);
        Assert.True(GameMcpEntityCapabilityMap.Supports(
            "spell-recipes",
            GameMcpCommandKind.SpellWorkbench));
    }

    [Fact]
    public void LocalhostMcpOwnsSpellWorkbenchOnlyForTheCurrentOperation()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.False(ownership.TryCaptureSpellWorkbenchMutationPermit());

        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.SpellWorkbench,
            "select",
            out var scope,
            out var reason), reason);
        using (scope)
            Assert.True(ownership.TryCaptureSpellWorkbenchMutationPermit());
        Assert.False(ownership.TryCaptureSpellWorkbenchMutationPermit());
        Assert.Equal(
            "action_family_unavailable",
            GameMcpActionResultCodeNames.Name(
                SpellWorkbenchActionResultCodes.MutationPermitUnavailable,
                GameMcpCommandKind.SpellWorkbench));
    }

    private static GameMcpCommand Command(string mode) => new(
        1,
        GameMcpCommandKind.SpellWorkbench,
        9,
        3,
        mode,
        RecipeId,
        Guid.Empty,
        "SpellRecipeSO",
        string.Empty,
        1,
        string.Empty,
        string.Empty,
        false,
        false);

    private static GameWorldState World(
        bool discovered,
        bool selected,
        bool discoveryAffordable,
        bool creationAffordable,
        bool hasEmptySlot,
        bool equipped = false)
    {
        var glyphs = PublicationTable<WorldSpellRecipeGlyph>.Create(new[]
        {
            new WorldSpellRecipeGlyph(0, FirstGlyphId),
            new WorldSpellRecipeGlyph(1, SecondGlyphId),
        });
        var discoveryCosts = PublicationTable<WorldDiscoverableCost>.Create(new[]
        {
            new WorldDiscoverableCost(
                ResourceId,
                new BigDouble(4.4d, 3),
                new BigDouble(9d, 6)),
        });
        var selectedGlyphs = selected
            ? PublicationTable<WorldSpellWorkbenchGlyph>.Create(new[]
            {
                new WorldSpellWorkbenchGlyph(0, FirstGlyphId),
                new WorldSpellWorkbenchGlyph(1, SecondGlyphId),
            })
            : PublicationTable<WorldSpellWorkbenchGlyph>.Empty;
        var creationCosts = PublicationTable<WorldSpellWorkbenchCost>.Create(new[]
        {
            new WorldSpellWorkbenchCost(
                ResourceId,
                new BigDouble(7.5d, 2),
                new BigDouble(9d, 6)),
        });

        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell-recipes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                new WorldSpellRecipe(
                    RecipeId,
                    discovered,
                    0,
                    BigDouble.Zero,
                    0,
                    false,
                    false,
                    false,
                    0,
                    1d,
                    1,
                    false,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    BigDouble.One,
                    false,
                    glyphs,
                    discoveryCosts,
                    discoveryAffordable),
            }),
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                Glyph(SecondGlyphId, 3),
                Glyph(FirstGlyphId, 7),
            }),
            SpellWorkbench = new WorldSpellWorkbench(
                selectedGlyphs,
                PublicationTable<WorldSpellWorkbenchGlyph>.Empty,
                creationCosts,
                creationAffordable,
                equipped ? 1 : 0,
                3,
                hasEmptySlot),
            SpellSlots = equipped
                ? PublicationTable<WorldSpellSlot>.Create(new[]
                {
                    new WorldSpellSlot(
                        0,
                        Guid.NewGuid(),
                        RecipeId,
                        true,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        true,
                        true,
                        true,
                        1,
                        1,
                        BigDouble.Zero),
                })
                : PublicationTable<WorldSpellSlot>.Empty,
        };
    }

    private static WorldGlyph Glyph(Guid id, int level) => new(
        id,
        level,
        0,
        0,
        false,
        true,
        false,
        false,
        false,
        false,
        0,
        BigDouble.Zero,
        BigDouble.Zero,
        BigDouble.Zero);
}
