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

public sealed class GameMcpGenericDiscoveryTests
{
    private static readonly Guid GlyphId =
        Guid.Parse("f3000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId =
        Guid.Parse("f3000000-0000-0000-0000-000000000002");

    [Fact]
    public void Tool_advertises_one_uuid_without_generation_or_receipt_inputs()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "uuid" }, schema["required"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.NotNull(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["amount"]);
    }

    [Fact]
    public void Validation_names_missing_uuid_and_removed_generation_argument()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject(),
            }));
        var unexpected = router.Handle(GameMcpAcceptanceFixture.Request(
            2,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject
                {
                    ["uuid"] = GlyphId.ToString("D"),
                    ["worldGeneration"] = 17,
                },
            }));

        var missingErrors = Assert.IsType<JArray>(
            missing.Body!["error"]!["data"]!["validationErrors"]);
        var unexpectedErrors = Assert.IsType<JArray>(
            unexpected.Body!["error"]!["data"]!["validationErrors"]);
        Assert.Contains(
            missingErrors.Values<JObject>(),
            error => (string?)error!["code"] == "missing_required" &&
                     (string?)error["field"] == "uuid");
        Assert.Contains(
            unexpectedErrors.Values<JObject>(),
            error => (string?)error!["code"] == "unexpected_field" &&
                     (string?)error["field"] == "worldGeneration");
    }

    [Fact]
    public void Predecision_and_poststate_are_named_and_carry_the_exact_next_cost()
    {
        var row = Json(GameMcpWorldQuery.GetRow(
            Context(), "glyphs", GlyphId.ToString("D"), "GlyphSO"));
        var postState = Json(GameMcpWorldQuery.ProjectPostState(
            Context(), "glyphs", GlyphId));

        Assert.Equal("available", (string?)row["status"]);
        Assert.Equal((ulong)2301, (ulong)row["worldGeneration"]!);
        var glyph = row["row"]!;
        Assert.Equal(GlyphId.ToString("D"), (string?)glyph["uuid"]);
        Assert.Equal("Amplify", (string?)glyph["name"]);
        Assert.True((bool)glyph["discover"]!["available"]!);
        Assert.True((bool)glyph["discover"]!["required"]!);
        var cost = Assert.Single(glyph["discover"]!["costs"]!).Value<JObject>()!;
        Assert.Equal(ResourceId.ToString("D"), (string?)cost["resource"]!["uuid"]);
        Assert.Equal("Arcane Dust", (string?)cost["resource"]!["name"]);
        Assert.Equal("5e0", (string?)cost["cost"]);
        Assert.Equal("8e0", (string?)cost["amount"]);
        Assert.True((bool)cost["affordable"]!);

        Assert.Equal("Amplify", (string?)postState["name"]);
        Assert.NotNull(postState["discover"]!["costs"]);
        Assert.Null(postState["receipt"]);
        Assert.Null(postState["payment"]);
        Assert.Null(postState["worldGeneration"]);
    }

    [Fact]
    public void Success_yields_to_poststate_while_failure_keeps_named_decomposed_evidence()
    {
        var before = new GenericDiscoveryState(
            "GlyphSO", true, true, false, true);
        var after = new GenericDiscoveryState(
            "GlyphSO", true, true, false, true);
        var receipt = new GenericDiscoveryMutationReceipt(
            true,
            true,
            true,
            false,
            in before,
            in after,
            new[]
            {
                new GenericDiscoveryCostReceipt(
                    ResourceId,
                    new BigDouble(5),
                    new BigDouble(8),
                    new BigDouble(3)),
            });
        var failure = new GenericDiscoverySubmission(
            GenericDiscoveryPreflight.VerificationFailed,
            GenericDiscoveryNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(2, 1, 0),
            in receipt,
            "target remained undiscovered");
        var success = new GenericDiscoverySubmission(
            GenericDiscoveryPreflight.Proceeded,
            GenericDiscoveryNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(2, 1, 1),
            in receipt,
            "target discovered");

        var failed = Json(GameMcpGenericDiscoveryProjection.Project(in failure));
        var committed = Json(GameMcpGenericDiscoveryProjection.Project(in success));

        Assert.Equal("verification_failed", (string?)failed["preflight"]);
        Assert.True((bool)failed["quarantined"]!);
        var cost = Assert.Single(failed["receipt"]!["costs"]!).Value<JObject>()!;
        Assert.Equal("Arcane Dust", (string?)cost["resource"]!["name"]);
        Assert.Equal("5e0", (string?)cost["observedDelta"]);
        Assert.Empty(committed.Properties());
    }

    private static GameMcpFrameContext Context()
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(World(), new WorldGeneration(2301));
        return GameMcpTestHarness.Context(
            publisher.ReadLatest(), configurationGeneration: 8, lifecycleGeneration: 15);
    }

    private static GameWorldState World()
    {
        var costs = PublicationTable<WorldDiscoverableCost>.Create(new[]
        {
            new WorldDiscoverableCost(ResourceId, new BigDouble(5), new BigDouble(8)),
        });
        var decision = new WorldDiscoverableDecision(
            visible: true,
            canDiscover: true,
            discovered: false,
            required: true,
            affordable: true,
            costs);
        var glyph = new WorldGlyph(
            GlyphId,
            level: 0,
            freeLevels: 0,
            discoveryRarityLevel: 1,
            discovered: false,
            discoverable: true,
            discoveryRequired: true,
            augmentsSpells: false,
            requiresDuration: false,
            requiresToggleable: false,
            masteryReqCount: 0,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.One,
            available: true,
            maximumUsages: 1,
            discovery: decision);
        return new GameWorldState
        {
            CollectedAtEpoch = 15,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = Identities(),
            Glyphs = PublicationTable<WorldGlyph>.Create(new[] { glyph }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "glyphs", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }

    private static EntityIdentityCatalogSnapshot Identities()
    {
        var rows = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(GlyphId, "GlyphSO", "Amplify", "amplify"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Arcane Dust", "arcaneDust"),
        }).OrderBy(row => row.EntityId).ToArray();
        return EntityIdentityCatalogSnapshot.Bound(15, rows);
    }

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, Identities()));

    private static JObject Json(GameMcpObjectBuilder value) => Json(value.Freeze());
}
