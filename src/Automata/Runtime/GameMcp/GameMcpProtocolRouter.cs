#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>Stateless MCP 2025-11-25 JSON-RPC application layer.</summary>
internal sealed class GameMcpProtocolRouter
{
    internal const string LatestProtocolVersion = "2025-11-25";
    internal const string ServerName = "orb-of-creation-modsuite";
    internal const string ServerVersion = "0.5.0-game-mcp";
    internal const int TerminalWaitMilliseconds = 2_000;

    private static readonly HashSet<string> SupportedProtocolVersions = new(StringComparer.Ordinal)
    {
        "2025-11-25",
        "2025-06-18",
        "2025-03-26",
    };

    private readonly GameMcpStateStore _state;
    private readonly GameMcpCommandBus _commands;

    internal GameMcpProtocolRouter(GameMcpStateStore state, GameMcpCommandBus commands)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    internal GameMcpProtocolResponse Handle(JObject request)
    {
        if ((string?)request["jsonrpc"] != "2.0")
            return GameMcpProtocolResponse.Json(Error(null, -32600, "jsonrpc must be exactly '2.0'"));

        var method = (string?)request["method"];
        if (string.IsNullOrWhiteSpace(method))
            return GameMcpProtocolResponse.Json(Error(request["id"], -32600, "method is required"));

        var hasId = request.TryGetValue("id", out var id);
        if (!hasId)
            return GameMcpProtocolResponse.Accepted();

        try
        {
            var result = method switch
            {
                "initialize" => Initialize(request),
                "ping" => new JObject(),
                "tools/list" => ListTools(),
                "tools/call" => CallTool(request),
                "resources/list" => ListResources(),
                "resources/templates/list" => ListResourceTemplates(),
                "resources/read" => ReadResource(request),
                _ => null,
            };
            return result is null
                ? GameMcpProtocolResponse.Json(Error(id, -32601, "method not found: " + method))
                : GameMcpProtocolResponse.Json(Success(id, result));
        }
        catch (GameMcpInvalidParamsException exception)
        {
            return GameMcpProtocolResponse.Json(Error(id, -32602, exception.Message));
        }
        catch (Exception exception)
        {
            return GameMcpProtocolResponse.Json(
                Error(id, -32603, "internal MCP failure: " + exception.GetBaseException().Message));
        }
    }

    internal static bool IsSupportedProtocolVersion(string? value) =>
        value is not null && SupportedProtocolVersions.Contains(value);

    private static JObject Initialize(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var requested = RequireString(parameters, "protocolVersion");
        var negotiated = SupportedProtocolVersions.Contains(requested)
            ? requested
            : LatestProtocolVersion;
        RequireObject(parameters, "capabilities");
        RequireObject(parameters, "clientInfo");

        return new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject
                {
                    ["subscribe"] = false,
                    ["listChanged"] = false,
                },
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["title"] = "Orb Of Creation strategist server",
                ["version"] = ServerVersion,
                ["description"] =
                    "Local perf-debug access to one published world, suite health, and audited player commands.",
            },
            ["instructions"] =
                "Every read names one published world. Actions are live-revalidated on Unity's " +
                "main thread and return their terminal outcome inline; worldGeneration is optional " +
                "decision-audit metadata, never an age gate.",
        };
    }

    private JObject CallTool(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var name = RequireString(parameters, "name");
        var arguments = parameters["arguments"] as JObject ?? new JObject();
        var state = _state.ReadLatest();

        var execution = name switch
        {
            "world_overview" => GameMcpToolExecution.Read(GameMcpWorldQuery.Overview(state)),
            "world_categories" => GameMcpToolExecution.Read(GameMcpWorldQuery.ListCategories(state)),
            "world_list" => GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
                state,
                RequireString(arguments, "category"),
                OptionalInt(arguments, "offset", 0),
                OptionalInt(arguments, "limit", GameMcpWorldQuery.DefaultLimit))),
            "world_get" => GameMcpToolExecution.Read(GameMcpWorldQuery.GetRow(
                state,
                RequireString(arguments, "category"),
                RequireString(arguments, "uuid"),
                OptionalString(arguments, "expectedNativeType"))),
            "world_search" => GameMcpToolExecution.Read(GameMcpWorldQuery.Search(
                state,
                RequireString(arguments, "query"),
                OptionalInt(arguments, "limit", GameMcpWorldQuery.DefaultLimit))),
            "suite_health" => GameMcpToolExecution.Read(Health(
                state,
                OptionalString(arguments, "detail"))),
            "suite_configuration" => GameMcpToolExecution.Read(Configuration(state)),
            "trace_health" => GameMcpToolExecution.Read(TraceHealth(state)),
            "game_purchase" => SubmitPurchase(state, arguments),
            "game_cast" => SubmitCast(state, arguments),
            "game_concept" => SubmitConcept(state, arguments),
            "game_harvest" => SubmitHarvest(state, arguments),
            "game_spell_level" => SubmitSpellLevel(state, arguments),
            "suite_config_set" => SubmitConfiguration(state, arguments),
            "suite_emergency_stop" => SubmitEmergencyStop(state, arguments),
            "game_screenshot" => SubmitScreenshot(state, arguments),
            "game_continue" => SubmitGadget(
                state,
                GameMcpCommandKind.ContinueRun,
                "continue",
                Guid.Empty,
                1,
                string.Empty,
                capture: false,
                saveCapture: false),
            "game_screen_catalog" => SubmitGadget(
                state,
                GameMcpCommandKind.ScreenCatalog,
                "catalog",
                Guid.Empty,
                1,
                string.Empty,
                capture: false,
                saveCapture: false),
            "game_navigate" => SubmitNavigation(state, arguments),
            "game_tooltips" => SubmitTooltipCatalog(state, arguments),
            "game_tooltip" => SubmitGadget(
                state,
                GameMcpCommandKind.TooltipRead,
                "read",
                Guid.Empty,
                1,
                RequireString(arguments, "path"),
                OptionalBool(arguments, "capture", false),
                saveCapture: false),
            "game_probe" => SubmitProbe(state, arguments),
            _ => GameMcpToolExecution.Error(GameMcpWorldQuery.WithEnvelope(
                state,
                new JObject
                {
                    ["status"] = "not_available",
                    ["code"] = "unknown_tool",
                    ["reason"] = "unknown tool '" + name + "'; call tools/list",
                })),
        };
        return execution.ToProtocolResult();
    }

    private JObject Health(GameMcpStateSnapshot state, string detail)
    {
        var captured = ParseObject(state.HealthJson);
        var features = captured["features"] as JArray ?? new JArray();
        var services = captured["services"] as JArray ?? new JArray();
        if (detail.Length > 0)
        {
            var serviceMatches = services
                .OfType<JObject>()
                .Where(service => string.Equals(
                    (string?)service["serviceId"],
                    detail,
                    StringComparison.Ordinal))
                .ToArray();
            var featureMatches = features
                .OfType<JObject>()
                .Where(feature => string.Equals(
                    (string?)feature["key"]?["featureId"],
                    detail,
                    StringComparison.Ordinal))
                .ToArray();
            if (serviceMatches.Length + featureMatches.Length != 1)
            {
                return GameMcpWorldQuery.WithEnvelope(
                    state,
                    new JObject
                    {
                        ["status"] = "not_available",
                        ["code"] = "health_detail_match_failed",
                        ["reason"] = "exact health detail selector '" + detail +
                            "' matched " + serviceMatches.Length + " services and " +
                            featureMatches.Length +
                            " features; choose one serviceId or featureId from suite_health",
                        ["detail"] = detail,
                    });
            }
            var exact = new JObject
            {
                ["status"] = "available",
                ["scope"] = serviceMatches.Length == 1
                    ? "exact_service_detail"
                    : "exact_feature_detail",
                ["detail"] = detail,
                ["capturedAtUtc"] = FormatTicks(state.CapturedAtUtcTicks),
                ["configurationGeneration"] = captured["configurationGeneration"],
                ["lifecycleGeneration"] = captured["lifecycleGeneration"],
            };
            if (serviceMatches.Length == 1)
                exact["service"] = serviceMatches[0].DeepClone();
            else
                exact["feature"] = featureMatches[0].DeepClone();
            return GameMcpWorldQuery.WithEnvelope(state, exact);
        }

        var health = new JObject
        {
            ["status"] = "available",
            ["scope"] = "situational",
            ["capturedAtUtc"] = FormatTicks(state.CapturedAtUtcTicks),
            ["runtimeAvailable"] = captured["runtimeAvailable"],
            ["runtimeNotAvailableReason"] = captured["runtimeNotAvailableReason"],
            ["scene"] = captured["scene"],
            ["nativeContractsAvailable"] = captured["nativeContractsAvailable"],
            ["configurationGeneration"] = captured["configurationGeneration"],
            ["lifecycleGeneration"] = captured["lifecycleGeneration"],
            ["emergencyStopEngaged"] = captured["emergencyStopEngaged"],
            ["features"] = CompactFeatures(features),
            ["services"] = CompactServices(services),
            ["mailbox"] = new JObject
            {
                ["pending"] = _commands.PendingCount,
                ["ordinaryCapacity"] = GameMcpCommandBus.MaximumPending,
                ["priorityCapacity"] = GameMcpCommandBus.MaximumPriorityPending,
                ["emergencyStopSlots"] = GameMcpCommandBus.EmergencyStopPrioritySlots,
                ["totalCapacity"] = GameMcpCommandBus.MaximumTotalPending,
            },
            ["detailSelector"] = "exact featureId or serviceId",
        };
        if (((string?)captured["runtimeNotAvailableReason"] ?? string.Empty).Length > 0)
            health["runtimeNotAvailableReason"] = captured["runtimeNotAvailableReason"];
        return GameMcpWorldQuery.WithEnvelope(state, health);
    }

    private static JArray CompactFeatures(JArray? captured)
    {
        var result = new JArray();
        if (captured is null) return result;
        foreach (var feature in captured.OfType<JObject>())
        {
            var key = feature["key"] as JObject;
            var reason = feature["reason"] as JObject;
            result.Add(new JObject
            {
                ["featureId"] = key?["featureId"],
                ["state"] = feature["state"],
                ["reasonCode"] = reason?["code"],
            });
        }
        return result;
    }

    private static JArray CompactServices(JArray captured)
    {
        var result = new JArray();
        foreach (var service in captured.OfType<JObject>())
        {
            var runner = service["runner"] as JObject;
            var fault = runner?["fault"] as JObject;
            result.Add(new JObject
            {
                ["serviceId"] = service["serviceId"],
                ["state"] = (bool?)service["hasRunner"] == true
                    ? runner?["phase"]
                    : "Unavailable",
                ["faulted"] = fault?["isValid"],
            });
        }
        return result;
    }

    private static JObject Configuration(GameMcpStateSnapshot state) =>
        GameMcpWorldQuery.WithEnvelope(
            state,
            new JObject
            {
                ["status"] = state.ConfigurationGeneration.IsValid
                    ? "available"
                    : "not_available",
                ["code"] = state.ConfigurationGeneration.IsValid
                    ? string.Empty
                    : "configuration_not_published",
                ["reason"] = state.ConfigurationGeneration.IsValid
                    ? string.Empty
                    : "the suite has not published configuration yet",
                ["configurationGeneration"] = state.ConfigurationGeneration.Value,
                ["configuration"] = ParseObject(state.ConfigurationJson),
                ["writableSettings"] = ParseArray(state.WritableConfigurationJson),
            });

    private static JObject TraceHealth(GameMcpStateSnapshot state) =>
        GameMcpWorldQuery.WithEnvelope(
            state,
            new JObject
            {
                ["status"] = "available",
                ["traceWriterStatus"] = ParseObject(state.TraceHealthJson),
                ["scope"] =
                    "writer health, retained segment counts, record totals, and byte volume only",
                ["events"] = "not_available",
                ["eventsNotAvailableReason"] =
                    "individual decisions belong to the trace folder and offline analysis; " +
                    "the strategist surface does not duplicate them",
            });

    private GameMcpToolExecution SubmitPurchase(
        GameMcpStateSnapshot state,
        JObject arguments)
    {
        var target = RequireUuid(arguments, "uuid");
        if (!TryResolvePurchase(state, target, out var mode, out var nativeType, out var reason))
            return TerminalRejection(state, arguments, "unknown_purchase_target", reason);
        return SubmitAction(
            state,
            arguments,
            GameMcpCommandKind.Purchase,
            mode,
            target,
            Guid.Empty,
            nativeType,
            OptionalString(arguments, "expectedNativeType"),
            OptionalIntInRange(arguments, "count", 1, 1, 1000));
    }

    private GameMcpToolExecution SubmitCast(GameMcpStateSnapshot state, JObject arguments)
    {
        var target = RequireUuid(arguments, "spellRecipeUuid");
        if (!TryResolveEntity(state, target, "spell-recipes", out var reason))
            return TerminalRejection(state, arguments, "unknown_spell_recipe", reason);
        var slotIndex = RequiredInt(arguments, "slotIndex", 0, 255);
        return SubmitAction(
            state,
            arguments,
            GameMcpCommandKind.Cast,
            RequireOneOf(arguments, "mode", "fire", "release"),
            target,
            Guid.Empty,
            "SpellRecipeSO",
            OptionalString(arguments, "expectedNativeType"),
            checked(slotIndex + 1));
    }

    private GameMcpToolExecution SubmitConcept(GameMcpStateSnapshot state, JObject arguments)
    {
        var target = RequireUuid(arguments, "recipeUuid");
        if (!TryResolveEntity(state, target, "alchemy-recipes", out var reason))
            return TerminalRejection(state, arguments, "unknown_alchemy_recipe", reason);
        var mode = RequireOneOf(arguments, "mode", "add", "remove_owned", "rotate_out");
        var replacement = OptionalUuid(arguments, "replacementUuid");
        if (mode == "rotate_out" && replacement == Guid.Empty)
            throw new GameMcpInvalidParamsException("replacementUuid is required for rotate_out");
        if (replacement != Guid.Empty &&
            !TryResolveEntity(state, replacement, "alchemy-recipes", out var replacementReason))
        {
            return TerminalRejection(
                state,
                arguments,
                "unknown_replacement_recipe",
                replacementReason);
        }
        return SubmitAction(
            state,
            arguments,
            GameMcpCommandKind.Concept,
            mode,
            target,
            replacement,
            "AlchemyRecipeSO",
            OptionalString(arguments, "expectedNativeType"),
            OptionalIntInRange(arguments, "amount", 1, 1, 1_000_000));
    }

    private GameMcpToolExecution SubmitHarvest(
        GameMcpStateSnapshot state,
        JObject arguments)
    {
        var target = RequireUuid(arguments, "plotNodeUuid");
        if (!TryResolveHarvest(state, target, out var mode, out var reason))
            return TerminalRejection(state, arguments, "unsupported_harvest_target", reason);
        return SubmitAction(
            state,
            arguments,
            GameMcpCommandKind.Harvest,
            mode,
            target,
            Guid.Empty,
            "PlotNodeSO",
            OptionalString(arguments, "expectedNativeType"),
            1);
    }

    private GameMcpToolExecution SubmitSpellLevel(
        GameMcpStateSnapshot state,
        JObject arguments)
    {
        var target = RequireUuid(arguments, "spellRecipeUuid");
        if (!TryResolveEntity(state, target, "spell-recipes", out var reason))
            return TerminalRejection(state, arguments, "unknown_spell_recipe", reason);
        return SubmitAction(
            state,
            arguments,
            GameMcpCommandKind.SpellLevel,
            RequireOneOf(arguments, "mode", "single", "all"),
            target,
            Guid.Empty,
            "SpellRecipeSO",
            OptionalString(arguments, "expectedNativeType"),
            1);
    }

    private GameMcpToolExecution SubmitAction(
        GameMcpStateSnapshot state,
        JObject arguments,
        GameMcpCommandKind kind,
        string mode,
        Guid targetId,
        Guid secondaryId,
        string derivedNativeType,
        string expectedNativeType,
        int amount)
    {
        if (state.World is null)
            return TerminalRejection(state, arguments, "world_not_available", state.RuntimeNotAvailableReason);
        if (state.LifecycleGeneration <= 0)
            return TerminalRejection(
                state,
                arguments,
                "lifecycle_not_available",
                "the main-thread state has no valid lifecycle generation");
        if (!state.ConfigurationGeneration.IsValid)
            return TerminalRejection(
                state,
                arguments,
                "configuration_not_available",
                "the main thread has not published a configuration");
        if (expectedNativeType.Length > 0 &&
            !string.Equals(expectedNativeType, derivedNativeType, StringComparison.Ordinal))
        {
            return TerminalRejection(
                state,
                arguments,
                "native_type_mismatch",
                "the server derived " + derivedNativeType +
                " from the target UUID, but expectedNativeType asserted " + expectedNativeType);
        }

        return WaitForTerminal(
            state,
            _commands.Submit(
                kind,
                OptionalUlong(arguments, "worldGeneration"),
                state.LifecycleGeneration,
                state.ConfigurationGeneration.Value,
                mode,
                targetId,
                secondaryId,
                derivedNativeType,
                expectedNativeType,
                amount));
    }

    private GameMcpToolExecution SubmitConfiguration(
        GameMcpStateSnapshot state,
        JObject arguments)
    {
        var expected = RequiredUlong(arguments, "configurationGeneration");
        if (!state.ConfigurationGeneration.IsValid)
            return GameMcpToolExecution.Read(ConfigurationNotAvailable(state));
        if (expected != state.ConfigurationGeneration.Value)
            return GameMcpToolExecution.Read(StaleConfiguration(state, expected));
        return WaitForTerminal(
            state,
            _commands.SubmitConfiguration(
                expected,
                RequireString(arguments, "section"),
                RequireString(arguments, "key"),
                RequireRawString(arguments, "serializedValue")));
    }

    private GameMcpToolExecution SubmitEmergencyStop(
        GameMcpStateSnapshot state,
        JObject arguments)
    {
        var expected = RequiredUlong(arguments, "configurationGeneration");
        if (!state.ConfigurationGeneration.IsValid)
            return GameMcpToolExecution.Read(ConfigurationNotAvailable(state));
        if (expected != state.ConfigurationGeneration.Value)
            return GameMcpToolExecution.Read(StaleConfiguration(state, expected));
        var mode = RequireOneOf(arguments, "mode", "engage", "resume");
        return WaitForTerminal(
            state,
            _commands.SubmitEmergencyStop(expected, mode == "engage"));
    }

    private GameMcpToolExecution SubmitScreenshot(
        GameMcpStateSnapshot state,
        JObject arguments) =>
        SubmitGadget(
            state,
            GameMcpCommandKind.Screenshot,
            "capture",
            Guid.Empty,
            1,
            string.Empty,
            capture: true,
            saveCapture: OptionalBool(arguments, "save", false));

    private GameMcpToolExecution SubmitNavigation(
        GameMcpStateSnapshot state,
        JObject arguments)
    {
        var payload = new JObject
        {
            ["tab"] = RequireSelector(arguments, "tab"),
        };
        if (arguments.TryGetValue("subtab", out _))
            payload["subtab"] = RequireSelector(arguments, "subtab");
        var plotNode = OptionalUuid(arguments, "plotNodeUuid");
        if (plotNode != Guid.Empty &&
            !TryResolveEntity(state, plotNode, "plot-nodes", out var reason))
        {
            return TerminalRejection(
                state,
                arguments,
                "unknown_plot_node",
                reason);
        }
        return SubmitGadget(
            state,
            GameMcpCommandKind.Navigation,
            "navigate",
            plotNode,
            1,
            payload.ToString(Formatting.None),
            OptionalBool(arguments, "capture", false),
            saveCapture: false);
    }

    private GameMcpToolExecution SubmitProbe(
        GameMcpStateSnapshot state,
        JObject arguments) =>
        SubmitGadget(
            state,
            GameMcpCommandKind.Probe,
            RequireOneOf(arguments, "probe", "runtime", "action_queue_room", "navigation"),
            Guid.Empty,
            1,
            string.Empty,
            capture: false,
            saveCapture: false);

    private GameMcpToolExecution SubmitTooltipCatalog(
        GameMcpStateSnapshot state,
        JObject arguments)
    {
        var offset = OptionalInt(arguments, "offset", 0);
        var limit = OptionalInt(arguments, "limit", GameMcpWorldQuery.DefaultLimit);
        if (offset < 0)
            return TerminalRejection(
                state,
                arguments,
                "tooltip_offset_invalid",
                "tooltip catalog offset must be non-negative");
        if (limit is < 1 or > 200)
            return TerminalRejection(
                state,
                arguments,
                "tooltip_limit_invalid",
                "tooltip catalog limit must be between 1 and 200");
        return SubmitGadget(
            state,
            GameMcpCommandKind.TooltipCatalog,
            "catalog",
            Guid.Empty,
            limit,
            offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            capture: false,
            saveCapture: false);
    }

    private GameMcpToolExecution SubmitGadget(
        GameMcpStateSnapshot state,
        GameMcpCommandKind kind,
        string mode,
        Guid targetId,
        int amount,
        string payloadValue,
        bool capture,
        bool saveCapture) =>
        WaitForTerminal(
            state,
            _commands.SubmitGadget(
                kind,
                mode,
                targetId,
                amount,
                payloadValue,
                capture,
                saveCapture));

    private GameMcpToolExecution WaitForTerminal(
        GameMcpStateSnapshot state,
        GameMcpCommand command)
    {
        if (!command.Completion.TryWait(
                TimeSpan.FromMilliseconds(TerminalWaitMilliseconds),
                out var terminal))
        {
            var timeout = GameMcpCommandResult.Faulted(
                "terminal_wait_timeout",
                "server defect: MCP command " + command.Sequence + " (" + command.Kind +
                ") did not finish within " + TerminalWaitMilliseconds +
                " ms; no pending state or polling fallback exists");
            return GameMcpToolExecution.Error(
                GameMcpWorldQuery.WithEnvelope(state, timeout.Project(command)));
        }
        return new GameMcpToolExecution(
            GameMcpWorldQuery.WithEnvelope(state, terminal.Project(command)),
            terminal.InlinePng,
            isError: string.Equals(terminal.Status, "faulted", StringComparison.Ordinal));
    }

    private static GameMcpToolExecution TerminalRejection(
        GameMcpStateSnapshot state,
        JObject arguments,
        string code,
        string reason)
    {
        var payload = new JObject
        {
            ["status"] = "rejected",
            ["disposition"] = "Rejected",
            ["resultCode"] = code,
            ["resultCodeName"] = code,
            ["reason"] = reason,
            ["nativeCallsAttempted"] = 0,
            ["mutationAttempts"] = 0,
            ["mutationsCommitted"] = 0,
            ["verifiedMutations"] = 0,
        };
        var generation = OptionalUlong(arguments, "worldGeneration");
        if (generation.HasValue)
            payload["decisionWorldGeneration"] = generation.Value;
        return GameMcpToolExecution.Read(GameMcpWorldQuery.WithEnvelope(state, payload));
    }

    private static bool TryResolvePurchase(
        GameMcpStateSnapshot state,
        Guid target,
        out string mode,
        out string nativeType,
        out string reason)
    {
        mode = string.Empty;
        nativeType = string.Empty;
        if (state.World is null)
        {
            reason = "the published world is unavailable";
            return false;
        }
        var world = state.World.Snapshot;
        var structure = WorldLookup.TryFind(world.Structures, target, out _);
        var upgrade = WorldLookup.TryFind(world.Upgrades, target, out _);
        if (structure == upgrade)
        {
            reason = structure
                ? "UUID " + target.ToString("D") +
                  " ambiguously identifies both a structure and an upgrade"
                : "UUID " + target.ToString("D") +
                  " is absent from published structures and upgrades";
            return false;
        }
        mode = structure ? "structure" : "upgrade";
        nativeType = structure ? "StructureSO" : "UpgradeSO";
        reason = string.Empty;
        return true;
    }

    private static bool TryResolveHarvest(
        GameMcpStateSnapshot state,
        Guid target,
        out string mode,
        out string reason)
    {
        mode = string.Empty;
        if (!TryResolveEntity(state, target, "plot-nodes", out reason))
            return false;
        if (target == KnownEntities.FruitTreePlot.Uuid)
        {
            mode = "fruit_tree";
            return true;
        }
        if (target == KnownEntities.TreasureTreePlot.Uuid)
        {
            mode = "treasure_tree";
            return true;
        }
        reason = "plot " + target.ToString("D") +
            " is published but is not one of the two audited native harvest pairs";
        return false;
    }

    private static bool TryResolveEntity(
        GameMcpStateSnapshot state,
        Guid target,
        string category,
        out string reason)
    {
        if (state.World is null)
        {
            reason = "the published world is unavailable";
            return false;
        }
        var world = state.World.Snapshot;
        var found = category switch
        {
            "spell-recipes" => WorldLookup.TryFind(world.SpellRecipes, target, out _),
            "alchemy-recipes" => WorldLookup.TryFind(world.AlchemyRecipes, target, out _),
            "plot-nodes" => WorldLookup.TryFind(world.PlotNodes, target, out _),
            _ => false,
        };
        reason = found
            ? string.Empty
            : "UUID " + target.ToString("D") +
              " is absent from published category " + category;
        return found;
    }

    private JObject ReadResource(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var uri = RequireString(parameters, "uri");
        var state = _state.ReadLatest();
        JObject value;
        if (uri == "orb://world/overview")
            value = GameMcpWorldQuery.Overview(state);
        else if (uri == "orb://world/categories")
            value = GameMcpWorldQuery.ListCategories(state);
        else if (uri == "orb://suite/health")
            value = Health(state, string.Empty);
        else if (uri == "orb://suite/configuration")
            value = Configuration(state);
        else if (uri == "orb://trace/health")
            value = TraceHealth(state);
        else if (uri.StartsWith("orb://world/category/", StringComparison.Ordinal))
        {
            var category = Uri.UnescapeDataString(uri.Substring("orb://world/category/".Length));
            value = GameMcpWorldQuery.ListRows(
                state,
                category,
                0,
                GameMcpWorldQuery.DefaultLimit);
        }
        else
        {
            throw new GameMcpInvalidParamsException(
                "unknown resource URI '" + uri + "'; call resources/list");
        }

        return new JObject
        {
            ["contents"] = new JArray
            {
                new JObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = "application/json",
                    ["text"] = value.ToString(Formatting.None),
                },
            },
        };
    }

    private static JObject ListTools() => new()
    {
        ["tools"] = new JArray
        {
            Tool(
                "world_overview",
                "Published world overview",
                "Read the compact facts a strategist normally needs before choosing a detailed query.",
                ObjectSchema()),
            Tool(
                "world_categories",
                "Discover world categories",
                "List every collected world table, native type, row count, and exact availability reason.",
                ObjectSchema()),
            Tool(
                "world_list",
                "List exact world rows",
                "Page through one discoverable category from one immutable published world.",
                ObjectSchema(
                    new JObject
                    {
                        ["category"] = StringSchema("Exact name returned by world_categories."),
                        ["offset"] = IntegerSchema(0, int.MaxValue),
                        ["limit"] = IntegerSchema(1, 200),
                    },
                    "category")),
            Tool(
                "world_get",
                "Get one exact world row",
                "Read a stable entity by UUID. expectedNativeType is an optional fail-closed assertion.",
                ObjectSchema(
                    new JObject
                    {
                        ["category"] = StringSchema("Exact name returned by world_categories."),
                        ["uuid"] = StringSchema("Canonical D-format stable UUID."),
                        ["expectedNativeType"] =
                            StringSchema("Optional exact assertion returned by world_categories."),
                    },
                    "category", "uuid")),
            Tool(
                "world_search",
                "Search the published world",
                "Search projected row values and UUIDs. Partial collection is reported explicitly.",
                ObjectSchema(
                    new JObject
                    {
                        ["query"] = StringSchema("Case-insensitive text or UUID fragment."),
                        ["limit"] = IntegerSchema(1, 200),
                    },
                    "query")),
            Tool(
                "suite_health",
                "Read suite runtime health",
                "Read compact lifecycle, STOP, feature, service, collector, and MCP mailbox health. Set detail to one exact returned featureId or serviceId for its complete record.",
                ObjectSchema(new JObject
                {
                    ["detail"] = StringSchema(
                        "Optional exact featureId or serviceId returned by the compact arrays."),
                })),
            Tool("suite_configuration", "Read committed configuration", "Read the single committed suite configuration and its generation.", ObjectSchema()),
            Tool(
                "trace_health",
                "Read trace-writer health",
                "Read bounded segment, record, and byte counters. Individual decisions remain in trace files for offline analysis.",
                ObjectSchema()),
            Tool(
                "game_purchase",
                "Purchase a structure or upgrade",
                "Live-revalidate and apply one UUID-addressed native purchase; the terminal result is returned inline.",
                ActionSchema(
                    new JObject
                    {
                        ["uuid"] = StringSchema("Canonical UUID from structures or upgrades; kind is derived."),
                        ["count"] = IntegerSchema(1, 1000),
                    },
                    "uuid")),
            Tool(
                "game_cast",
                "Cast an equipped spell",
                "Live-revalidate an equipped slot and apply the native cast or charge release inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("fire", "release"),
                        ["slotIndex"] = IntegerSchema(0, 255),
                        ["spellRecipeUuid"] = StringSchema("Spell recipe UUID currently occupying the slot."),
                    },
                    "mode", "slotIndex", "spellRecipeUuid")),
            Tool(
                "game_concept",
                "Assign or remove a concept",
                "Apply one exact concept assignment change and return its terminal native result inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("add", "remove_owned", "rotate_out"),
                        ["recipeUuid"] = StringSchema("Alchemy recipe UUID."),
                        ["replacementUuid"] = StringSchema("Required only for rotate_out."),
                        ["amount"] = IntegerSchema(1, 1_000_000),
                    },
                    "mode", "recipeUuid")),
            Tool(
                "game_harvest",
                "Harvest an audited plot",
                "Derive the audited harvest pair from a published plot UUID and return the terminal native result inline.",
                ActionSchema(
                    new JObject
                    {
                        ["plotNodeUuid"] = StringSchema("Published plot-node UUID."),
                    },
                    "plotNodeUuid")),
            Tool(
                "game_spell_level",
                "Buy spell mastery",
                "Apply one exact mastery purchase or the native level-all operation inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("single", "all"),
                        ["spellRecipeUuid"] = StringSchema("Published spell-recipe UUID."),
                    },
                    "mode", "spellRecipeUuid")),
            Tool(
                "suite_config_set",
                "Commit one suite setting",
                "Write one allowlisted setting through the single committed configuration-store publication path.",
                ObjectSchema(
                    new JObject
                    {
                        ["configurationGeneration"] = UlongSchema("Exact generation returned by suite_configuration."),
                        ["section"] = StringSchema("Exact BepInEx configuration section."),
                        ["key"] = StringSchema("Exact BepInEx configuration key."),
                        ["serializedValue"] = StringSchema("Ordinary BepInEx serialized value."),
                    },
                    "configurationGeneration", "section", "key", "serializedValue"),
                readOnly: false,
                idempotent: false),
            Tool(
                "suite_emergency_stop",
                "Engage or resume suite emergency stop",
                "Commit STOP or RESUME through the same safety configuration authority used in game.",
                ObjectSchema(
                    new JObject
                    {
                        ["configurationGeneration"] = UlongSchema("Exact generation returned by suite_configuration."),
                        ["mode"] = EnumSchema("engage", "resume"),
                    },
                    "configurationGeneration", "mode"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_screenshot",
                "Capture the game framebuffer",
                "Return a PNG as inline MCP image content. Set save=true to also write a generated name in the trace folder.",
                ObjectSchema(new JObject { ["save"] = BooleanSchema("Also save to the trace folder.") }),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_continue",
                "Continue the selected save",
                "On the Start scene only, invoke the game's audited native Continue action for the already selected save.",
                ObjectSchema(),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_screen_catalog",
                "Discover navigable screens",
                "Enumerate live top tabs and current subtabs by exact stable label and zero-based index.",
                ObjectSchema()),
            Tool(
                "game_navigate",
                "Navigate the live screen catalog",
                "Select one catalog tab, optional subtab, and optional published plot node; capture returns an inline PNG after arrival.",
                ObjectSchema(
                    new JObject
                    {
                        ["tab"] = SelectorSchema("Exact catalog label or zero-based index."),
                        ["subtab"] = SelectorSchema("Optional exact current-screen label or zero-based index."),
                        ["plotNodeUuid"] = StringSchema("Optional published plot UUID to select after navigation."),
                        ["capture"] = BooleanSchema("Return an inline PNG after arrival."),
                    },
                    "tab"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_tooltips",
                "Discover visible tooltips",
                "Page through active tooltip-bearing elements on the current screen by exact sibling-indexed native path.",
                ObjectSchema(new JObject
                {
                    ["offset"] = IntegerSchema(0, int.MaxValue),
                    ["limit"] = IntegerSchema(1, 200),
                })),
            Tool(
                "game_tooltip",
                "Read a visible tooltip",
                "Read core tooltip text and nested tooltip structure for one exact path; optionally capture the opened tooltip.",
                ObjectSchema(
                    new JObject
                    {
                        ["path"] = StringSchema("Exact path returned by game_tooltips."),
                        ["capture"] = BooleanSchema("Return an inline PNG with the tooltip open."),
                    },
                    "path"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_probe",
                "Run an allowlisted native value probe",
                "Read runtime/lifecycle, navigation, or live action-queue facts absent from the published world.",
                ObjectSchema(
                    new JObject
                    {
                        ["probe"] = EnumSchema("runtime", "action_queue_room", "navigation"),
                    },
                    "probe")),
        },
    };

    private static JObject ActionSchema(JObject properties, params string[] required)
    {
        properties["worldGeneration"] =
            UlongSchema("Optional audit metadata naming the world that motivated the decision.");
        properties["expectedNativeType"] =
            StringSchema("Optional exact assertion; the server derives the native type from UUID.");
        return ObjectSchema(properties, required);
    }

    private static JObject ListResources() => new()
    {
        ["resources"] = new JArray
        {
            Resource("orb://world/overview", "world-overview", "Compact published-world strategy overview."),
            Resource("orb://world/categories", "world-categories", "Discoverable world table inventory and collection status."),
            Resource("orb://suite/health", "suite-health", "Compact feature, service, emergency, collection, and MCP health."),
            Resource("orb://suite/configuration", "suite-configuration", "Committed suite configuration generation."),
            Resource("orb://trace/health", "trace-health", "Trace-writer health and retained volume."),
        },
    };

    private static JObject ListResourceTemplates() => new()
    {
        ["resourceTemplates"] = new JArray
        {
            new JObject
            {
                ["uriTemplate"] = "orb://world/category/{category}",
                ["name"] = "world-category",
                ["title"] = "Published world category",
                ["description"] = "First page of an exact category returned by world_categories.",
                ["mimeType"] = "application/json",
            },
        },
    };

    private static JObject Tool(
        string name,
        string title,
        string description,
        JObject inputSchema,
        bool readOnly = true,
        bool idempotent = true) => new()
    {
        ["name"] = name,
        ["title"] = title,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
        ["annotations"] = new JObject
        {
            ["readOnlyHint"] = readOnly,
            ["destructiveHint"] = false,
            ["idempotentHint"] = idempotent,
            ["openWorldHint"] = false,
        },
    };

    private static JObject Resource(string uri, string name, string description) => new()
    {
        ["uri"] = uri,
        ["name"] = name,
        ["title"] = name.Replace('-', ' '),
        ["description"] = description,
        ["mimeType"] = "application/json",
    };

    private static JObject ObjectSchema(JObject? properties = null, params string[] required)
    {
        var result = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties ?? new JObject(),
            ["additionalProperties"] = false,
        };
        if (required.Length > 0) result["required"] = new JArray(required);
        return result;
    }

    private static JObject SelectorSchema(string description) => new()
    {
        ["description"] = description,
        ["oneOf"] = new JArray
        {
            new JObject { ["type"] = "string", ["minLength"] = 1 },
            new JObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 63 },
        },
    };

    private static JObject StringSchema(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    private static JObject BooleanSchema(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
    };

    private static JObject IntegerSchema(int minimum, int maximum) => new()
    {
        ["type"] = "integer",
        ["minimum"] = minimum,
        ["maximum"] = maximum,
    };

    private static JObject UlongSchema(string description) => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["description"] = description,
    };

    private static JObject EnumSchema(params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JArray(values),
    };

    private static JObject RequireObject(JObject source, string name) =>
        source[name] as JObject ??
        throw new GameMcpInvalidParamsException(name + " must be an object");

    private static string RequireString(JObject source, string name)
    {
        if (source[name]?.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        var value = ((string?)source[name] ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new GameMcpInvalidParamsException(name + " must not be empty");
        return value;
    }

    private static string OptionalString(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token)) return string.Empty;
        if (token.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        var value = ((string?)token ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new GameMcpInvalidParamsException(name + " must not be empty when supplied");
        return value;
    }

    private static string RequireRawString(JObject source, string name)
    {
        if (source[name]?.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        return (string?)source[name] ?? string.Empty;
    }

    private static int OptionalInt(JObject source, string name, int fallback)
    {
        if (!source.TryGetValue(name, out var token)) return fallback;
        if (token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        try { return token.Value<int>(); }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported integer range");
        }
    }

    private static int RequiredInt(
        JObject source,
        string name,
        int minimum,
        int maximum)
    {
        if (!source.TryGetValue(name, out var token) || token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        int value;
        try { value = token.Value<int>(); }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported integer range");
        }
        if (value < minimum || value > maximum)
            throw new GameMcpInvalidParamsException(
                name + " must be between " + minimum + " and " + maximum);
        return value;
    }

    private static int OptionalIntInRange(
        JObject source,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!source.TryGetValue(name, out _)) return fallback;
        return RequiredInt(source, name, minimum, maximum);
    }

    private static bool OptionalBool(JObject source, string name, bool fallback)
    {
        if (!source.TryGetValue(name, out var token)) return fallback;
        if (token.Type != JTokenType.Boolean)
            throw new GameMcpInvalidParamsException(name + " must be a boolean");
        return token.Value<bool>();
    }

    private static ulong RequiredUlong(JObject source, string name)
    {
        var value = OptionalUlong(source, name);
        if (!value.HasValue)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        return value.Value;
    }

    private static ulong? OptionalUlong(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token)) return null;
        if (token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        try
        {
            var value = token.Value<ulong>();
            if (value == 0)
                throw new GameMcpInvalidParamsException(name + " must be greater than zero");
            return value;
        }
        catch (GameMcpInvalidParamsException) { throw; }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported unsigned range");
        }
    }

    private static Guid RequireUuid(JObject source, string name)
    {
        var value = OptionalUuid(source, name);
        if (value == Guid.Empty)
            throw new GameMcpInvalidParamsException(
                name + " must be a non-empty canonical D-format UUID");
        return value;
    }

    private static Guid OptionalUuid(JObject source, string name)
    {
        if (!source.TryGetValue(name, out _)) return Guid.Empty;
        var text = RequireString(source, name);
        if (!Guid.TryParseExact(text, "D", out var uuid) || uuid == Guid.Empty)
            throw new GameMcpInvalidParamsException(
                name + " must be a non-empty canonical D-format UUID");
        return uuid;
    }

    private static string RequireOneOf(
        JObject source,
        string name,
        params string[] allowed)
    {
        var value = RequireString(source, name);
        for (var index = 0; index < allowed.Length; index++)
            if (string.Equals(value, allowed[index], StringComparison.Ordinal))
                return value;
        throw new GameMcpInvalidParamsException(
            name + " must be one of: " + string.Join(", ", allowed));
    }

    private static JObject RequireSelector(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token))
            throw new GameMcpInvalidParamsException(name + " is required");
        if (token.Type == JTokenType.String)
        {
            var label = ((string?)token ?? string.Empty).Trim();
            if (label.Length == 0)
                throw new GameMcpInvalidParamsException(name + " must not be empty");
            return new JObject { ["kind"] = "name", ["value"] = label };
        }
        if (token.Type == JTokenType.Integer)
        {
            int index;
            try { index = token.Value<int>(); }
            catch (Exception)
            {
                throw new GameMcpInvalidParamsException(name + " index is outside the integer range");
            }
            if (index < 0 || index > 63)
                throw new GameMcpInvalidParamsException(name + " index must be between 0 and 63");
            return new JObject { ["kind"] = "index", ["value"] = index };
        }
        throw new GameMcpInvalidParamsException(name + " must be an exact string or integer index");
    }

    private static JObject ParseObject(string json)
    {
        try { return JObject.Parse(json); }
        catch (JsonException)
        {
            return new JObject
            {
                ["status"] = "not_available",
                ["reason"] = "the main-thread snapshot could not be decoded",
            };
        }
    }

    private static JArray ParseArray(string json)
    {
        try { return JArray.Parse(json); }
        catch (JsonException) { return new JArray(); }
    }

    private static string FormatTicks(long ticks) =>
        ticks <= 0 || ticks > DateTime.MaxValue.Ticks
            ? string.Empty
            : new DateTime(ticks, DateTimeKind.Utc).ToString("O");

    private static JObject ConfigurationNotAvailable(GameMcpStateSnapshot state) =>
        GameMcpWorldQuery.WithEnvelope(
            state,
            new JObject
            {
                ["status"] = "not_available",
                ["code"] = "configuration_not_available",
                ["reason"] = "the main thread has not published a configuration",
            });

    private static JObject StaleConfiguration(GameMcpStateSnapshot state, ulong expected) =>
        GameMcpWorldQuery.WithEnvelope(
            state,
            new JObject
            {
                ["status"] = "rejected",
                ["code"] = "stale_configuration_generation",
                ["reason"] =
                    "request names configuration generation " + expected +
                    " but the HTTP snapshot is generation " +
                    state.ConfigurationGeneration.Value,
            });

    private static JObject Success(JToken? id, JObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
        ["result"] = result,
    };

    internal static JObject Error(JToken? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
        ["error"] = new JObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };
}

internal sealed class GameMcpToolExecution
{
    internal GameMcpToolExecution(JObject payload, byte[]? inlinePng, bool isError)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        InlinePng = inlinePng;
        IsError = isError;
    }

    internal JObject Payload { get; }
    internal byte[]? InlinePng { get; }
    internal bool IsError { get; }

    internal static GameMcpToolExecution Read(JObject payload) => new(payload, null, false);
    internal static GameMcpToolExecution Error(JObject payload) => new(payload, null, true);

    internal JObject ToProtocolResult()
    {
        var content = new JArray
        {
            new JObject
            {
                ["type"] = "text",
                ["text"] = Payload.ToString(Formatting.None),
            },
        };
        if (InlinePng is not null)
        {
            content.Add(new JObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(InlinePng),
                ["mimeType"] = "image/png",
            });
        }
        return new JObject
        {
            ["content"] = content,
            ["structuredContent"] = Payload,
            ["isError"] = IsError,
        };
    }
}

internal readonly struct GameMcpProtocolResponse
{
    private GameMcpProtocolResponse(int statusCode, JObject? body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    internal int StatusCode { get; }
    internal JObject? Body { get; }
    internal static GameMcpProtocolResponse Accepted() => new(202, null);
    internal static GameMcpProtocolResponse Json(JObject body) => new(200, body);
}

internal sealed class GameMcpInvalidParamsException : Exception
{
    internal GameMcpInvalidParamsException(string message) : base(message) { }
}
#endif
