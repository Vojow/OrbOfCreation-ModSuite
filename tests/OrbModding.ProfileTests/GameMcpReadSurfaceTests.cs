using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpStreamableHttpProtocolTests
{
    [Fact]
    public void RecordEqualityMetadataProjectsAsBoundedTypeName()
    {
        var projected = GameMcpObjectProjector.Project(new SuiteRuntimeConfiguration());

        Assert.True(projected.ToString().Length < 20_000);
        Assert.Equal(
            typeof(SuiteRuntimeConfiguration).FullName,
            (string?)projected["equalityContract"]);
    }

    [Fact]
    public void RouterImplementsInitializeDiscoveryResourceAndToolSemantics()
    {
        var router = new GameMcpProtocolRouter(
            new GameMcpStateStore(),
            new GameMcpCommandBus());

        var initialize = router.Handle(Request(
            1,
            "initialize",
            new JObject
            {
                ["protocolVersion"] = GameMcpProtocolRouter.LatestProtocolVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = "profile-test",
                    ["version"] = "1",
                },
            }));
        Assert.Equal(200, initialize.StatusCode);
        Assert.Equal(
            GameMcpProtocolRouter.LatestProtocolVersion,
            (string?)initialize.Body?["result"]?["protocolVersion"]);
        Assert.Equal(
            GameMcpProtocolRouter.ServerName,
            (string?)initialize.Body?["result"]?["serverInfo"]?["name"]);

        var tools = router.Handle(Request(2, "tools/list", new JObject()));
        var toolNames = tools.Body!["result"]!["tools"]!
            .Values<JObject>()
            .Select(static tool => (string?)tool!["name"])
            .ToArray();
        Assert.Contains("world_overview", toolNames);
        Assert.Contains("world_get", toolNames);
        Assert.Contains("suite_health", toolNames);
        Assert.Contains("trace_health", toolNames);
        Assert.DoesNotContain("decision_journal", toolNames);
        Assert.Contains("game_purchase", toolNames);
        Assert.Contains("game_cast", toolNames);
        Assert.Contains("game_concept", toolNames);
        Assert.Contains("game_harvest", toolNames);
        Assert.Contains("game_spell_level", toolNames);
        Assert.Contains("game_action_queue_recover", toolNames);
        Assert.DoesNotContain("action_receipt", toolNames);
        Assert.Contains("game_screenshot", toolNames);
        Assert.Contains("game_screen_catalog", toolNames);
        Assert.Contains("game_navigate", toolNames);
        Assert.Contains("game_tooltips", toolNames);
        Assert.Contains("game_tooltip", toolNames);
        Assert.Contains("game_probe", toolNames);

        var resources = router.Handle(Request(3, "resources/list", new JObject()));
        Assert.Contains(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://world/overview");
        Assert.Contains(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://trace/health");
        Assert.DoesNotContain(
            resources.Body!["result"]!["resources"]!.Values<JObject>(),
            resource => (string?)resource!["uri"] == "orb://journal/status");

        var call = router.Handle(Request(
            4,
            "tools/call",
            new JObject
            {
                ["name"] = "world_overview",
                ["arguments"] = new JObject(),
            }));
        Assert.Equal(
            "not_available",
            (string?)call.Body?["result"]?["structuredContent"]?["status"]);
        Assert.Equal(
            "world_not_published",
            (string?)call.Body?["result"]?["structuredContent"]?["code"]);

        var initialized = router.Handle(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized",
        });
        Assert.Equal(202, initialized.StatusCode);
        Assert.Null(initialized.Body);
    }

    [Fact]
    public void WrongUuidIsRejectedByToolBoundary()
    {
        var router = new GameMcpProtocolRouter(
            new GameMcpStateStore(),
            new GameMcpCommandBus());
        var response = router.Handle(Request(
            8,
            "tools/call",
            new JObject
            {
                ["name"] = "game_purchase",
                ["arguments"] = new JObject
                {
                    ["worldGeneration"] = 1,
                    ["kind"] = "structure",
                    ["uuid"] = "not-a-uuid",
                    ["expectedNativeType"] = "StructureSO",
                },
            }));

        Assert.Equal(-32602, (int?)response.Body?["error"]?["code"]);
        Assert.Contains(
            "canonical D-format UUID",
            (string?)response.Body?["error"]?["message"]);
    }

    [Fact]
    public async Task HttpTransportIsLoopbackOnlyAndEnforcesStreamableHttpHeaders()
    {
        var port = FreeLoopbackPort();
        var messages = new System.Collections.Generic.List<string>();
        using var server = GameMcpHttpServer.TryStart(
            new GameMcpStateStore(),
            new GameMcpCommandBus(),
            messages.Add,
            messages.Add,
            port);
        Assert.NotNull(server);
        Assert.True(server!.IsListening);
        Assert.Equal("http://127.0.0.1:" + port + "/mcp", server.Endpoint);

        using var client = new HttpClient();
        using var initialize = Post(
            server.Endpoint,
            Request(
                1,
                "initialize",
                new JObject
                {
                    ["protocolVersion"] = GameMcpProtocolRouter.LatestProtocolVersion,
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject
                    {
                        ["name"] = "http-profile-test",
                        ["version"] = "1",
                    },
                }));
        using var initializeResponse = await client.SendAsync(initialize);
        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        Assert.Equal(
            GameMcpProtocolRouter.LatestProtocolVersion,
            initializeResponse.Headers.GetValues("MCP-Protocol-Version").Single());
        var initializeJson = JObject.Parse(await initializeResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            GameMcpProtocolRouter.ServerName,
            (string?)initializeJson["result"]?["serverInfo"]?["name"]);

        using var notification = Post(
            server.Endpoint,
            new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized",
            });
        notification.Headers.Add(
            "MCP-Protocol-Version",
            GameMcpProtocolRouter.LatestProtocolVersion);
        using var notificationResponse = await client.SendAsync(notification);
        Assert.Equal(HttpStatusCode.Accepted, notificationResponse.StatusCode);
        Assert.Equal(0, notificationResponse.Content.Headers.ContentLength);

        using var hostile = Post(server.Endpoint, Request(2, "ping", new JObject()));
        hostile.Headers.Add("Origin", "https://example.com");
        hostile.Headers.Add(
            "MCP-Protocol-Version",
            GameMcpProtocolRouter.LatestProtocolVersion);
        using var hostileResponse = await client.SendAsync(hostile);
        Assert.Equal(HttpStatusCode.Forbidden, hostileResponse.StatusCode);

        using var get = new HttpRequestMessage(HttpMethod.Get, server.Endpoint);
        using var getResponse = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);
    }

    private static HttpRequestMessage Post(string uri, JObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static JObject Request(int id, string method, JObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters,
    };
}

public sealed class GameMcpWorldEnvelopeTests
{
    [Fact]
    public void ActionQueueMembersAreDiscoverableFromThePinnedWorld()
    {
        var queueId = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[] { Clean("action queues") },
                1),
            ActionQueueMembers = PublicationTable<WorldActionQueueMember>.Create(
                new[]
                {
                    new WorldActionQueueMember(
                        queueId,
                        index: 0,
                        structureId,
                        WorldActionQueueMemberKind.Structure,
                        stackCount: 4,
                        nativeQueuedCount: 0,
                        actionTime: new BigDouble(-1d),
                        actionTimeTotal: new BigDouble(2d),
                        buildSpeed: new BigDouble(100d),
                        timingReadable: true),
                },
                1),
            CollectedAtEpoch = 16,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(900));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpWorldQuery.ListCategories(state);
        var category = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "action-queue-members")!;
        Assert.True((bool)category["available"]!);
        Assert.Equal("StructureSO|UpgradeSO", (string?)category["expectedNativeType"]);
        Assert.Equal("composite_guid_fields", (string?)category["identityMode"]);

        var listed = GameMcpWorldQuery.ListRows(state, "action-queue-members", 0, 10);
        Assert.Equal("available", (string?)listed["status"]);
        Assert.Equal(1, (int)listed["total"]!);
        var row = Assert.Single(listed["rows"]!.Values<JObject>());
        Assert.Equal(queueId.ToString("D"), (string?)row["queueId"]);
        Assert.Equal(structureId.ToString("D"), (string?)row["actionableId"]);
        Assert.Equal("Structure", (string?)row["kind"]);
        Assert.Equal(4, (int)row["stackCount"]!);
        Assert.Equal(0, (int)row["nativeQueuedCount"]!);
        Assert.Equal("ExcessStacks", (string?)row["consistency"]);
    }

    [Fact]
    public void OneResponseUsesOnePublishedWorldAndCarriesCollectionEvidence()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources",
                        WorldCategoryOutcome.Collected,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: string.Empty),
                    new WorldCollectionCategoryStatus(
                        "rituals",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "the RitualSO registry was unreadable"),
                },
                2),
            CollectedAtEpoch = 17,
            CollectedAtUtcTicks = new DateTime(2026, 7, 30, 0, 30, 0, DateTimeKind.Utc).Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(901));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpWorldQuery.ListCategories(state);
        Assert.Equal("available", (string?)categories["status"]);
        Assert.Equal((ulong)901, (ulong)categories["worldGeneration"]!);
        Assert.Equal(17, (long)categories["structuralEpoch"]!);
        Assert.Equal(17, (long)categories["collectedEpoch"]!);
        Assert.Equal(
            "2026-07-30T00:30:00.0000000Z",
            (string?)categories["collectedAtUtc"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)categories["respondedAtUtc"]));

        var resource = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "resources")!;
        Assert.True((bool)resource["available"]!);
        Assert.Equal(0, (int)resource["count"]!);

        var rituals = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "rituals")!;
        Assert.False((bool)rituals["available"]!);
        Assert.Contains("registry was unreadable", (string?)rituals["reason"]);
    }

    [Fact]
    public void UnknownAndUncollectedQueriesReturnTypedNotAvailableAnswers()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "ResourceSO.quantity was not bound"),
                },
                1),
            CollectedAtEpoch = 18,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(902));
        var state = Snapshot(publisher.ReadLatest());

        var unknown = GameMcpWorldQuery.ListRows(state, "does-not-exist", 0, 10);
        Assert.Equal("not_available", (string?)unknown["status"]);
        Assert.Equal("unknown_category", (string?)unknown["code"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)unknown["reason"]));

        var unavailable = GameMcpWorldQuery.ListRows(state, "resources", 0, 10);
        Assert.Equal("not_available", (string?)unavailable["status"]);
        Assert.Equal("category_not_collected", (string?)unavailable["code"]);
        Assert.Contains("quantity was not bound", (string?)unavailable["reason"]);

        var wrongType = GameMcpWorldQuery.GetRow(
            state,
            "resources",
            Guid.NewGuid().ToString("D"),
            "UpgradeSO");
        Assert.Equal("not_available", (string?)wrongType["status"]);
        Assert.Equal("native_type_mismatch", (string?)wrongType["code"]);
    }

    [Fact]
    public void PartiallyCollectedCategoryIsNotPresentedAsAuthoritative()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources",
                        WorldCategoryOutcome.Collected,
                        sampled: 3,
                        skipped: 1,
                        firstFailure: "one ResourceSO quantity was unreadable"),
                },
                1),
            CollectedAtEpoch = 19,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(903));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpWorldQuery.ListCategories(state);
        var resources = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "resources")!;
        Assert.False((bool)resources["available"]!);
        Assert.Contains("collection is partial", (string?)resources["reason"]);
        Assert.Contains("quantity was unreadable", (string?)resources["reason"]);

        var rows = GameMcpWorldQuery.ListRows(state, "resources", 0, 10);
        Assert.Equal("not_available", (string?)rows["status"]);
        Assert.Equal("category_not_collected", (string?)rows["code"]);

        var search = GameMcpWorldQuery.Search(state, "resource", 10);
        Assert.Equal("not_available", (string?)search["status"]);
        Assert.Equal("world_search_incomplete", (string?)search["code"]);
        Assert.NotEmpty(search["unavailableCategories"]!.Values<JObject>());
        Assert.NotNull(search["partialMatches"]);
    }

    [Fact]
    public void CompositeWorldRowsCannotMatchAnUnrelatedGuidField()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "structure-costs",
                        WorldCategoryOutcome.Collected,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: string.Empty),
                },
                1),
            CollectedAtEpoch = 20,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(904));
        var state = Snapshot(publisher.ReadLatest());

        var result = GameMcpWorldQuery.GetRow(
            state,
            "purchase-costs",
            Guid.NewGuid().ToString("D"),
            "StructureSO|UpgradeSO");

        Assert.Equal("not_available", (string?)result["status"]);
        Assert.Equal("composite_identity_required", (string?)result["code"]);
        Assert.Contains("world_list", (string?)result["reason"]);
    }

    [Fact]
    public void MergedWorldTablesRequireEveryProducerReport()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("structures"),
                    Clean("upgrades"),
                    Clean("resources"),
                    Clean("modifier-variables"),
                    Clean("int-variables"),
                    Clean("structure-costs"),
                    new WorldCollectionCategoryStatus(
                        "upgrade-costs",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "UpgradeSO cost capture failed"),
                    Clean("spell-recipes"),
                    Clean("alchemy-recipes"),
                    Clean("equipment"),
                },
                10),
            CollectedAtEpoch = 21,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(905));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpWorldQuery.ListCategories(state);
        var purchaseCosts = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "purchase-costs")!;
        var mastery = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "mastery-experience")!;

        Assert.False((bool)purchaseCosts["available"]!);
        Assert.Contains("UpgradeSO cost capture failed", (string?)purchaseCosts["reason"]);
        Assert.True((bool)mastery["available"]!);
        Assert.Equal(string.Empty, (string?)mastery["reason"]);
    }

    [Fact]
    public void DerivedWorldTablesRequireEveryUpstreamReport()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("structures"),
                    Clean("upgrades"),
                    Clean("resources"),
                    new WorldCollectionCategoryStatus(
                        "modifier-variables",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "modifier variables failed"),
                    Clean("int-variables"),
                    Clean("structure-costs"),
                    Clean("upgrade-costs"),
                    Clean("plot-nodes"),
                    new WorldCollectionCategoryStatus(
                        "plot-node-actions",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "plot node actions failed"),
                    Clean("plot-actions"),
                },
                10),
            CollectedAtEpoch = 22,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(906));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpWorldQuery.ListCategories(state);
        var purchaseCosts = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "purchase-costs")!;
        var plotActions = categories["categories"]!
            .Values<JObject>()
            .Single(item => (string?)item!["name"] == "plot-actions")!;

        Assert.False((bool)purchaseCosts["available"]!);
        Assert.Equal("modifier variables failed", (string?)purchaseCosts["reason"]);
        Assert.False((bool)plotActions["available"]!);
        Assert.Equal("plot node actions failed", (string?)plotActions["reason"]);
    }

    [Fact]
    public void ModifierFoldingDegradationInvalidatesEveryDependentDerivedTable()
    {
        var world = new GameWorldState
        {
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    Clean("resources"),
                    Clean("harvest-resources"),
                    Clean("structures"),
                    Clean("upgrades"),
                    Clean("modifier-variables"),
                    Clean("int-variables"),
                    Clean("structure-costs"),
                    Clean("upgrade-costs"),
                    new WorldCollectionCategoryStatus(
                        "modifier-folding",
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "frame-global modifier reconstruction failed"),
                },
                9),
            CollectedAtEpoch = 23,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(907));
        var state = Snapshot(publisher.ReadLatest());

        var categories = GameMcpWorldQuery.ListCategories(state);
        foreach (var name in new[] { "resources", "harvest-resources", "purchase-costs" })
        {
            var category = categories["categories"]!
                .Values<JObject>()
                .Single(item => (string?)item!["name"] == name)!;
            Assert.False((bool)category["available"]!);
            Assert.Equal(
                "frame-global modifier reconstruction failed",
                (string?)category["reason"]);
        }
    }

    private static GameMcpStateSnapshot Snapshot(
        WorldPublication<GameWorldState> publication) =>
        new(
            publication,
            new ConfigGeneration(12),
            lifecycleGeneration: 17,
            DateTime.UtcNow.Ticks,
            "{}",
            "[]",
            "{}",
            "{}",
            runtimeAvailable: true,
            runtimeNotAvailableReason: string.Empty);

    private static WorldCollectionCategoryStatus Clean(string category) =>
        new(
            category,
            WorldCategoryOutcome.Collected,
            sampled: 0,
            skipped: 0,
            firstFailure: string.Empty);
}
