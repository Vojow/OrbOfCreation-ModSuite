using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbChronicle;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpPublicationConsistencyTests
{
    [Fact]
    public void QueryRemainsPinnedWhenANewerWorldPublishes()
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(GameMcpAcceptanceFixture.SpellWorld(3, 31), new WorldGeneration(1001));
        var pinned = GameMcpAcceptanceFixture.Snapshot(publisher.ReadLatest());
        publisher.Publish(GameMcpAcceptanceFixture.SpellWorld(9, 32), new WorldGeneration(1002));

        var result = GameMcpWorldQuery.GetRow(
            pinned,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D"),
            string.Empty);

        Assert.Equal((ulong)1001, (ulong)result["worldGeneration"]!);
        Assert.Equal(31, (long)result["collectedEpoch"]!);
        Assert.Equal(3, (int)result["row"]!["masteryLevel"]!);
    }
}

public sealed class GameMcpWorldQueryTests
{
    [Fact]
    public void OverviewIsCompactAndExactReadDerivesNativeType()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);
        var overview = GameMcpWorldQuery.Overview(state);
        Assert.Equal("available", (string?)overview["status"]);
        Assert.NotNull(overview["economy"]);
        Assert.NotNull(overview["progression"]);
        Assert.NotNull(overview["running"]);
        Assert.Null(overview["unlocks"]);
        Assert.Null(overview["harvest"]);

        var exact = GameMcpWorldQuery.GetRow(
            state,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D"),
            string.Empty);
        Assert.Equal("available", (string?)exact["status"]);
        Assert.Equal("SpellRecipeSO", (string?)exact["expectedNativeType"]);
        Assert.Equal(4, (int)exact["row"]!["masteryLevel"]!);
    }

    [Fact]
    public void OptionalNativeTypeAssertionFailsClosedOnlyOnMismatch()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);
        var mismatch = GameMcpWorldQuery.GetRow(
            state,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D"),
            "AlchemyRecipeSO");
        Assert.Equal("not_available", (string?)mismatch["status"]);
        Assert.Equal("native_type_mismatch", (string?)mismatch["code"]);
    }

    [Fact]
    public void ListRowsAreScanProjectionsAndGetRetainsTheCompleteRecord()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);

        var list = GameMcpWorldQuery.ListRows(state, "spell-recipes", 0, 10);
        var scan = Assert.Single(list["rows"]!.Values<JObject>())!;
        Assert.Equal(GameMcpAcceptanceFixture.SpellId.ToString("D"), (string?)scan["entityId"]);
        Assert.Equal(4, (int)scan["masteryLevel"]!);
        Assert.Null(scan["spellPowerMod"]);
        Assert.Null(scan["mcpCategory"]);

        var exact = GameMcpWorldQuery.GetRow(
            state,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D"),
            string.Empty);
        Assert.NotNull(exact["row"]!["spellPowerMod"]);
        Assert.Equal("spell-recipes", (string?)exact["row"]!["mcpCategory"]);
    }
}

public sealed class GameMcpActionAdmissionTests
{
    [Fact]
    public void DecisionWorldGenerationNeverCreatesAnAgeRejection()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand(decisionGeneration: 1);
        Assert.False(GameMcpNativeActionAdmission.TryReject(
            command,
            currentWorldGeneration: 1_000_000,
            command.ExpectedLifecycleGeneration,
            command.ExpectedConfigurationGeneration,
            emergencyStopEngaged: false,
            out _));
    }

    [Fact]
    public void LifecycleConfigurationAndEmergencyStopRemainLiveGates()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand(null);
        Assert.True(GameMcpNativeActionAdmission.TryReject(
            command,
            500,
            command.ExpectedLifecycleGeneration + 1,
            command.ExpectedConfigurationGeneration,
            false,
            out var lifecycle));
        Assert.Equal("lifecycle_replaced", lifecycle.Code);

        Assert.True(GameMcpNativeActionAdmission.TryReject(
            command,
            500,
            command.ExpectedLifecycleGeneration,
            command.ExpectedConfigurationGeneration,
            true,
            out var stop));
        Assert.Equal("emergency_stop", stop.Code);
    }
}

public sealed class GameMcpActionFailureReasonTests
{
    [Fact]
    public void MissingHarvestPrerequisiteEvidenceDoesNotClaimTheNativeCheckFailed()
    {
        var reason = AutomataServiceCycleRuntime.HarvestPrerequisiteEvidenceReason(
            "fruit_tree",
            PlotActionPrerequisiteEvidence.Unknown);

        Assert.NotNull(reason);
        Assert.Contains("no plot-action prerequisite latch evidence", reason);
        Assert.DoesNotContain("unmet", reason);
        Assert.Null(AutomataServiceCycleRuntime.HarvestPrerequisiteEvidenceReason(
            "fruit_tree",
            PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation));
    }
}

public sealed class GameMcpInlineCompletionTests
{
    [Fact]
    public void TerminalCompletionCarriesInlineNativeProofAndOptionalAuditGeneration()
    {
        var commands = new GameMcpCommandBus();
        var submitted = GameMcpAcceptanceFixture.SubmitPurchase(commands, 51);
        Assert.True(commands.TryDequeue(out var command));
        var evidence = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));
        var native = ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        commands.Complete(
            command,
            GameMcpCommandResult.FromAction(
                in native,
                GameMcpCommandKind.Purchase,
                900,
                12,
                7));

        Assert.True(submitted.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var terminal));
        var projected = terminal.Project(submitted);
        Assert.Equal("committed", (string?)projected["status"]);
        Assert.Equal((ulong)51, (ulong)projected["decisionWorldGeneration"]!);
        Assert.Equal((ulong)900, (ulong)projected["observedWorldGeneration"]!);
        Assert.Equal(1, (int)projected["nativeCallsAttempted"]!);
        Assert.Equal(1, (int)projected["verifiedMutations"]!);
        Assert.Null(projected["receiptId"]);
    }

    [Fact]
    public void QueueOverflowReturnsAnImmediateTerminalRejection()
    {
        var commands = new GameMcpCommandBus();
        for (var index = 0; index < GameMcpCommandBus.MaximumPending; index++)
            GameMcpAcceptanceFixture.SubmitHarvest(commands);
        var overflow = GameMcpAcceptanceFixture.SubmitHarvest(commands);

        Assert.True(overflow.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var terminal));
        Assert.Equal("command_queue_full", terminal.Code);
        Assert.Equal(GameMcpCommandBus.MaximumPending, commands.PendingCount);
    }
}

public sealed class GameMcpProtocolSurfaceTests
{
    [Fact]
    public void DiscoveryHasNoReceiptToolAndExposesGenericVisualTools()
    {
        var names = GameMcpAcceptanceFixture.ToolNames();
        Assert.DoesNotContain("action_receipt", names);
        Assert.DoesNotContain("decision_journal", names);
        Assert.Contains("trace_health", names);
        Assert.Contains("game_screen_catalog", names);
        Assert.Contains("game_navigate", names);
        Assert.Contains("game_tooltips", names);
        Assert.Contains("game_tooltip", names);
        Assert.Contains("game_screenshot", names);
    }

    [Fact]
    public void ActionSchemasRequireIdentityButNotGenerationKindOrNativeType()
    {
        var tools = GameMcpAcceptanceFixture.Tools();
        var purchase = Assert.Single(
            tools,
            tool => (string?)tool["name"] == "game_purchase");
        var required = purchase["inputSchema"]!["required"]!.Values<string>().ToArray();
        Assert.Equal(new[] { "uuid" }, required);
        var properties = (JObject)purchase["inputSchema"]!["properties"]!;
        Assert.NotNull(properties["worldGeneration"]);
        Assert.NotNull(properties["expectedNativeType"]);
        Assert.Null(properties["kind"]);

        var screenshot = Assert.Single(
            tools,
            tool => (string?)tool["name"] == "game_screenshot");
        Assert.Null(screenshot["inputSchema"]!["required"]);
    }

    [Fact]
    public void TraceHealthIsWriterHealthOnly()
    {
        var result = GameMcpAcceptanceFixture.Call(
            new GameMcpProtocolRouter(
                GameMcpAcceptanceFixture.ConfiguredStore(),
                new GameMcpCommandBus()),
            "trace_health");
        Assert.NotNull(result["traceWriterStatus"]);
        Assert.Equal("not_available", (string?)result["events"]);
        Assert.Null(result["mcpEvents"]);
        Assert.Null(result["cursor"]);
    }

    [Fact]
    public void CompactHealthAndExactServiceDetailAreSeparateQuestions()
    {
        var health = new JObject
        {
            ["runtimeAvailable"] = true,
            ["runtimeNotAvailableReason"] = string.Empty,
            ["scene"] = "Main",
            ["nativeContractsAvailable"] = true,
            ["configurationGeneration"] = 3,
            ["lifecycleGeneration"] = 9,
            ["runtimeLifecycle"] = 9,
            ["emergencyStopEngaged"] = false,
            ["acceptedFrameCount"] = 42,
            ["features"] = new JArray
            {
                new JObject
                {
                    ["key"] = new JObject { ["featureId"] = "AutoBuy" },
                    ["displayName"] = "Auto Buy",
                    ["configuredEnabled"] = false,
                    ["state"] = "ConfigurationDisabled",
                    ["reason"] = new JObject
                    {
                        ["code"] = "ConfigurationDisabled",
                        ["summary"] = "detail-only feature reason",
                    },
                },
            },
            ["services"] = new JArray
            {
                new JObject
                {
                    ["serviceId"] = "orbautomata.world-collection",
                    ["displayName"] = "World collection",
                    ["hasRunner"] = true,
                    ["runner"] = new JObject
                    {
                        ["phase"] = "Waiting",
                        ["hasInFlightCycle"] = false,
                        ["hasWakeDue"] = true,
                        ["committedCount"] = 1,
                        ["fault"] = new JObject
                        {
                            ["isValid"] = false,
                            ["occurrenceCount"] = 0,
                        },
                        ["deepExactEvidence"] = "detail-only",
                    },
                },
            },
        };
        var store = GameMcpAcceptanceFixture.StoreWithHealth(health);
        var router = new GameMcpProtocolRouter(store, new GameMcpCommandBus());

        var compact = GameMcpAcceptanceFixture.Call(router, "suite_health");
        Assert.Equal("situational", (string?)compact["scope"]);
        Assert.NotNull(compact["mailbox"]);
        var feature = Assert.Single(compact["features"]!.Values<JObject>())!;
        Assert.Equal("AutoBuy", (string?)feature["featureId"]);
        Assert.Equal("ConfigurationDisabled", (string?)feature["state"]);
        Assert.Null(feature["displayName"]);
        Assert.Null(feature["reason"]);
        var summary = Assert.Single(compact["services"]!.Values<JObject>())!;
        Assert.Equal("Waiting", (string?)summary["state"]);
        Assert.Null(summary["runner"]);
        Assert.Null(summary["deepExactEvidence"]);

        var detail = GameMcpAcceptanceFixture.Call(
            router,
            "suite_health",
            new JObject { ["detail"] = "orbautomata.world-collection" });
        Assert.Equal("exact_service_detail", (string?)detail["scope"]);
        Assert.Equal(
            "detail-only",
            (string?)detail["service"]!["runner"]!["deepExactEvidence"]);

        var featureDetail = GameMcpAcceptanceFixture.Call(
            router,
            "suite_health",
            new JObject { ["detail"] = "AutoBuy" });
        Assert.Equal("exact_feature_detail", (string?)featureDetail["scope"]);
        Assert.Equal(
            "detail-only feature reason",
            (string?)featureDetail["feature"]!["reason"]!["summary"]);
    }
}

public sealed class GameMcpCommandPrimitiveTests
{
    [Fact]
    public void ImmutableCommandCrossesNoJsonOrUnityObjects()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand(null);
        var properties = typeof(GameMcpCommand).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.DoesNotContain(
            properties,
            property =>
                typeof(JToken).IsAssignableFrom(property.PropertyType) ||
                property.PropertyType.FullName?.StartsWith("UnityEngine.", StringComparison.Ordinal) == true);
        GameMcpNativeActionAdmission.AssertNativeType(command, "StructureSO");
        Assert.Throws<ArgumentException>(() =>
            GameMcpNativeActionAdmission.AssertNativeType(command, "UpgradeSO"));
    }
}

public sealed class GameMcpConfigurationTests
{
    [Fact]
    public void QueryReturnsOneCommittedGenerationAndWritableCatalog()
    {
        var router = new GameMcpProtocolRouter(
            GameMcpAcceptanceFixture.ConfiguredStore(
                "[{\"section\":\"AutoCast\",\"key\":\"Mode\",\"settingType\":\"Mode\",\"serializedValue\":\"Disabled\"}]"),
            new GameMcpCommandBus());
        var result = GameMcpAcceptanceFixture.Call(router, "suite_configuration");
        Assert.Equal((ulong)3, (ulong)result["configurationGeneration"]!);
        Assert.Single(result["writableSettings"]!.Values<JObject>());
    }

    [Fact]
    public void ValidWritePublishesOnceAndStaleWriteDoesNot()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var publications = 0;
        var store = new AutomataConfigurationStore(configuration, (_, _) => publications++);
        var before = store.CurrentGeneration;
        Assert.True(store.TrySetGameMcp(
            configuration.AutoCastMode.Definition.Section,
            configuration.AutoCastMode.Definition.Key,
            "Active",
            before,
            out _));
        Assert.False(store.TrySetGameMcp(
            configuration.AutoCastMode.Definition.Section,
            configuration.AutoCastMode.Definition.Key,
            "Disabled",
            before,
            out _));
        Assert.Equal(1, publications);
    }
}

public sealed class GameMcpEmergencyStopTests
{
    [Fact]
    public void AcceptedStopOwnsHeadOfLineAndClosesGameplayAdmission()
    {
        var commands = new GameMcpCommandBus();
        GameMcpAcceptanceFixture.SubmitHarvest(commands);
        var stop = commands.SubmitEmergencyStop(1, engaged: true);
        var blocked = GameMcpAcceptanceFixture.SubmitHarvest(commands);
        Assert.True(blocked.Completion.TryWait(TimeSpan.FromMilliseconds(50), out var rejection));
        Assert.Equal("emergency_stop_pending", rejection.Code);
        Assert.True(commands.TryDequeue(out var first));
        Assert.Same(stop, first);
    }
}

public sealed class GameMcpChronicleTests
{
    [Fact]
    public void StatusReadsTheImmutableCapturedSnapshot()
    {
        var router = new GameMcpProtocolRouter(
            GameMcpAcceptanceFixture.ConfiguredStore(),
            new GameMcpCommandBus());

        var result = GameMcpAcceptanceFixture.Call(router, "chronicle_status");

        Assert.Equal("Dormant", (string?)result["state"]);
        Assert.Equal("orb-major-v1", (string?)result["milestoneSchemaId"]);
        Assert.Equal(
            "orb-feature-resource-discoveries-v2",
            (string?)result["resourceSchemaId"]);
        Assert.Equal(8, result["milestones"]!.Count());
        Assert.Equal(7, result["resourceSections"]!.Count());
        Assert.Equal("magic", (string?)result["resourceSections"]![0]!["id"]);
        Assert.Equal(
            "spell-output",
            (string?)result["resourceSections"]![0]!["relationship"]);
        Assert.Equal("first-visible", (string?)result["resourceSections"]![0]!["captureMode"]);
        Assert.Equal(11, result["resourceSections"]![0]!["resources"]!.Count());
        Assert.Equal(
            "ResourceSO",
            (string?)result["resourceSections"]![0]!["resources"]![0]!["expectedNativeType"]);
    }

    [Fact]
    public async Task StartCrossesTheMailboxAndReturnsOneTerminalResult()
    {
        var commands = new GameMcpCommandBus();
        var router = new GameMcpProtocolRouter(
            GameMcpAcceptanceFixture.ConfiguredStore(),
            commands);
        var call = Task.Run(() =>
            GameMcpAcceptanceFixture.Call(router, "chronicle_start"));

        Assert.True(SpinWait.SpinUntil(() => commands.PendingCount == 1, 500));
        Assert.True(commands.TryDequeue(out var command));
        Assert.Equal(GameMcpCommandKind.ChronicleStart, command.Kind);
        commands.Complete(
            command,
            GameMcpCommandResult.Committed(
                "chronicle_started",
                "Chronicle run started",
                0,
                9,
                3));

        var result = await call;
        Assert.Equal("committed", (string?)result["status"]);
        Assert.Equal("chronicle_started", (string?)result["resultCode"]);
        Assert.Equal(0, (int?)result["nativeCallsAttempted"]);
        Assert.Equal(0, (int?)result["mutationAttempts"]);
    }

    [Fact]
    public void AbandonIsDeclaredDestructiveButClosedWorld()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "chronicle_abandon");
        Assert.True((bool?)tool["annotations"]?["destructiveHint"]);
        Assert.False((bool?)tool["annotations"]?["openWorldHint"]);
    }

    [Fact]
    public async Task ComparisonSelectionUsesTheBoundedMainThreadMailbox()
    {
        var commands = new GameMcpCommandBus();
        var router = new GameMcpProtocolRouter(
            GameMcpAcceptanceFixture.ConfiguredStore(),
            commands);
        var call = Task.Run(() => GameMcpAcceptanceFixture.Call(
            router,
            "chronicle_select_comparison",
            new JObject { ["mode"] = "Previous" }));

        Assert.True(SpinWait.SpinUntil(() => commands.PendingCount == 1, 500));
        Assert.True(commands.TryDequeue(out var command));
        Assert.Equal(GameMcpCommandKind.ChronicleSelectComparison, command.Kind);
        Assert.Equal("Previous", command.Mode);
        Assert.Empty(command.PayloadValue);
        commands.Complete(command, GameMcpCommandResult.Committed(
            "chronicle_comparison_selected",
            "comparison changed",
            0,
            9,
            3));

        var result = await call;
        Assert.Equal("committed", (string?)result["status"]);
        Assert.Equal("chronicle_comparison_selected", (string?)result["resultCode"]);
    }
}

public sealed class GameMcpForbiddenSurfaceTests
{
    [Fact]
    public void SurfaceContainsNoArbitraryInputOrSaveResetTools()
    {
        var names = GameMcpAcceptanceFixture.ToolNames();
        var combined = string.Join("|", names).ToLowerInvariant();
        Assert.DoesNotContain("save", combined);
        Assert.DoesNotContain("reset", combined);
        Assert.DoesNotContain("keyboard", combined);
        Assert.DoesNotContain("mouse", combined);
        Assert.DoesNotContain("invoke_native", combined);
    }
}

internal static class GameMcpAcceptanceFixture
{
    internal static readonly Guid SpellId =
        Guid.Parse("01234567-89ab-4cde-8f01-23456789abcd");

    internal static JObject Request(int id, string method, JObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters,
    };

    internal static IReadOnlyList<JObject> Tools()
    {
        var router = new GameMcpProtocolRouter(new GameMcpStateStore(), new GameMcpCommandBus());
        var response = router.Handle(Request(1, "tools/list", new JObject()));
        return response.Body!["result"]!["tools"]!.Values<JObject>().OfType<JObject>().ToArray();
    }

    internal static string[] ToolNames() =>
        Tools().Select(tool => (string)tool["name"]!).ToArray();

    internal static JObject Call(
        GameMcpProtocolRouter router,
        string tool,
        JObject? arguments = null)
    {
        var response = router.Handle(Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = tool,
                ["arguments"] = arguments ?? new JObject(),
            }));
        Assert.Equal(200, response.StatusCode);
        Assert.Null(response.Body?["error"]);
        return (JObject)response.Body!["result"]!["structuredContent"]!;
    }

    internal static GameMcpStateStore ConfiguredStore(string writableConfiguration = "[]")
    {
        var store = new GameMcpStateStore();
        store.Capture(
            new SuiteRuntimeConfiguration(),
            new ConfigGeneration(3),
            writableConfiguration,
            lifecycleGeneration: 9,
            sceneName: "Main",
            nativeContractsAvailable: true,
            Array.Empty<FeatureStatusSnapshot>(),
            DecisionJournalStatus.Unavailable,
            journalRevision: 2,
            runtime: null,
            chronicle: new ChronicleRunTracker().Snapshot);
        return store;
    }

    internal static GameMcpStateStore StoreWithHealth(JObject health)
    {
        var store = new GameMcpStateStore();
        var snapshot = new GameMcpStateSnapshot(
            (ServiceWorldPublication?)null,
            new ConfigGeneration(3),
            lifecycleGeneration: 9,
            DateTime.UtcNow.Ticks,
            "{}",
            "[]",
            health.ToString(Newtonsoft.Json.Formatting.None),
            "{}",
            runtimeAvailable: true,
            runtimeNotAvailableReason: string.Empty);
        typeof(GameMcpStateStore)
            .GetField("_latest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(store, snapshot);
        return store;
    }

    internal static GameMcpStateSnapshot SpellSnapshot(int masteryLevel) =>
        Snapshot(SpellWorld(masteryLevel, 30));

    internal static GameMcpStateSnapshot Snapshot(GameWorldState world)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(1001));
        return Snapshot(publisher.ReadLatest());
    }

    internal static GameMcpStateSnapshot Snapshot(
        WorldPublication<GameWorldState> publication) =>
        new(
            publication,
            new ConfigGeneration(3),
            lifecycleGeneration: 9,
            DateTime.UtcNow.Ticks,
            "{}",
            "[]",
            "{}",
            "{}",
            runtimeAvailable: true,
            runtimeNotAvailableReason: string.Empty);

    internal static GameWorldState SpellWorld(int masteryLevel, long epoch) => new()
    {
        CollectedAtEpoch = epoch,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
            new[]
            {
                new WorldCollectionCategoryStatus(
                    "spell-recipes",
                    WorldCategoryOutcome.Collected,
                    sampled: 1,
                    skipped: 0,
                    firstFailure: string.Empty),
            },
            1),
        SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(
            new[]
            {
                new WorldSpellRecipe(
                    SpellId,
                    discovered: true,
                    discRarityLevel: 0,
                    masteryXp: new BigDouble(12),
                    masteryLevel,
                    masteryLevelReady: true,
                    hiddenDiscovery: false,
                    isRequiredDiscovery: true,
                    penaltyUsageCost: 1,
                    castSpeed: 1,
                    baseCharges: 1,
                    repeatInstantEffects: false,
                    spellPowerMod: new BigDouble(1),
                    spellCostMod: new BigDouble(1),
                    spellCdSpeedMod: new BigDouble(1),
                    spellDurationMod: new BigDouble(1),
                    spellSpecialMod: new BigDouble(1),
                    spellXpMod: new BigDouble(1),
                    hasAlertedThisMastery: false),
            },
            1),
    };

    internal static GameMcpCommand NativeCommand(ulong? decisionGeneration)
    {
        var commands = new GameMcpCommandBus();
        var command = SubmitPurchase(commands, decisionGeneration);
        Assert.True(commands.TryDequeue(out var dequeued));
        Assert.Same(command, dequeued);
        return command;
    }

    internal static GameMcpCommand SubmitPurchase(
        GameMcpCommandBus commands,
        ulong? decisionGeneration) =>
        commands.Submit(
            GameMcpCommandKind.Purchase,
            decisionGeneration,
            expectedLifecycleGeneration: 12,
            expectedConfigurationGeneration: 7,
            mode: "structure",
            Guid.NewGuid(),
            Guid.Empty,
            derivedNativeType: "StructureSO",
            expectedNativeType: string.Empty,
            amount: 1);

    internal static GameMcpCommand SubmitHarvest(GameMcpCommandBus commands) =>
        commands.Submit(
            GameMcpCommandKind.Harvest,
            decisionWorldGeneration: null,
            expectedLifecycleGeneration: 1,
            expectedConfigurationGeneration: 1,
            mode: "fruit_tree",
            KnownEntities.FruitTreePlot.Uuid,
            Guid.Empty,
            derivedNativeType: "PlotNodeSO",
            expectedNativeType: string.Empty,
            amount: 1);
}
