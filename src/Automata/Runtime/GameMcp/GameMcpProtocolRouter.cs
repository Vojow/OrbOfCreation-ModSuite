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
    private static readonly string[] ProtocolOnlyMethodNames =
    {
        "initialize",
        "ping",
        "tools/list",
        "resources/list",
        "resources/templates/list",
    };

    private readonly GameMcpFrameInbox _operations;

    internal GameMcpProtocolRouter(GameMcpFrameInbox operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    internal GameMcpProtocolResponse Handle(JObject request)
    {
        GameMcpFrameThreadBoundary.AssertTransportWorkAllowed("MCP protocol routing");
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
                "tools/call" => CallToolNew(request),
                "resources/list" => ListResources(),
                "resources/templates/list" => ListResourceTemplates(),
                "resources/read" => ReadResourceNew(request),
                _ => null,
            };
            return result is null
                ? GameMcpProtocolResponse.Json(Error(id, -32601, "method not found: " + method))
                : GameMcpProtocolResponse.Json(Success(id, result));
        }
        catch (GameMcpInvalidParamsException exception)
        {
            return GameMcpProtocolResponse.Json(
                Error(id, -32602, exception.Message, exception.DataObject));
        }
        catch (Exception exception)
        {
            return GameMcpProtocolResponse.Json(
                Error(id, -32603, "internal MCP failure: " + exception.GetBaseException().Message));
        }
    }

    internal static bool IsSupportedProtocolVersion(string? value) =>
        value is not null && SupportedProtocolVersions.Contains(value);

    internal static string[] ProtocolOnlyMethods() =>
        (string[])ProtocolOnlyMethodNames.Clone();

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
                "main thread and return their terminal outcome plus observed post-state inline.",
        };
    }

    private JObject CallToolNew(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var name = RequireString(parameters, "name");
        var argumentsToken = parameters["arguments"];
        var arguments = argumentsToken switch
        {
            null => new JObject(),
            JObject value => value,
            _ => throw new GameMcpInvalidParamsException("arguments must be an object"),
        };
        ValidateToolArguments(name, arguments);
        return SubmitAndWait(BuildOperation(name, arguments)).ToProtocolResult();
    }

    private JObject ReadResourceNew(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var uri = RequireString(parameters, "uri");
        if (!IsKnownResourceUri(uri))
            throw new GameMcpInvalidParamsException(
                "unknown resource URI '" + uri + "'; call resources/list");
        var operation = new GameMcpOperationRequestBuilder
        {
            ToolName = "resource_read",
            Classification = GameMcpOperationClass.ReadOnly,
            RequiredData = ResourceData(uri),
            ResourceUri = uri,
        }.Freeze();
        var execution = SubmitAndWait(operation);
        if (execution.TextContent is not null)
        {
            return new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "text/plain",
                        ["text"] = execution.TextContent,
                    },
                },
            };
        }
        var encoded = GameMcpDocumentJsonEncoder.Encode(
            execution.Payload!, execution.EntityIdentities);
        return new JObject
        {
            ["contents"] = new JArray
            {
                new JObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = "application/json",
                    ["text"] = encoded.ToString(Formatting.None),
                },
            },
        };
    }

    private GameMcpToolExecution SubmitAndWait(GameMcpOperationRequest request)
    {
        var operation = _operations.Submit(request);
        if (!operation.Completion.TryWait(
                TimeSpan.FromMilliseconds(TerminalWaitMilliseconds),
                out var terminal))
        {
            var canceled = GameMcpToolExecution.Error(new GameMcpObjectBuilder
            {
                ["status"] = "rejected",
                ["code"] = "request_canceled_before_claim",
                ["reason"] = "Unity did not claim " + request.ToolName +
                    " within " + TerminalWaitMilliseconds +
                    " ms; it was canceled before execution",
            }.Freeze());
            if (operation.Completion.TryCancelBeforeClaim(canceled)) return canceled;
            terminal = operation.Completion.WaitForClaimedTerminal();
        }
        return terminal;
    }

    internal static GameMcpOperationRequest BuildOperation(string name, JObject arguments)
    {
        var builder = new GameMcpOperationRequestBuilder
        {
            ToolName = name,
            Limit = GameMcpWorldQuery.DefaultLimit,
            Amount = 1,
        };
        switch (name)
        {
            case "world_overview":
            case "world_categories":
            case "suite_configuration":
            case "trace_health":
            case "game_continue":
            case "game_screen_catalog":
                break;
            case "world_list":
                builder.Category = RequireString(arguments, "category");
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                break;
            case "world_get":
                builder.Category = RequireString(arguments, "category");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                builder.Uuids = RequireStringArray(
                    arguments, "uuids", GameMcpWorldQuery.MaximumBatchSize);
                break;
            case "entity_catalog":
            case "world_search":
                builder.Query = RequireString(arguments, "query");
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                break;
            case "explain_entity":
                builder.Uuid = RequireUuid(arguments, "uuid");
                break;
            case "suite_health":
                break;
            case "game_purchase":
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                builder.Amount = OptionalIntInRange(arguments, "count", 1, 1, 1000);
                break;
            case "game_cast":
                builder.Uuid = RequireUuid(arguments, "spellRecipeUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                builder.Mode = RequireOneOf(arguments, "mode", "fire", "release");
                builder.SlotIndex = RequiredInt(arguments, "slotIndex", 0, 255);
                break;
            case "game_concept":
                builder.Uuid = RequireUuid(arguments, "recipeUuid");
                builder.SecondaryUuid = OptionalUuid(arguments, "replacementUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                builder.Mode = RequireOneOf(
                    arguments, "mode", "add", "remove_owned", "rotate_out");
                if (builder.Mode == "rotate_out" && builder.SecondaryUuid == Guid.Empty)
                    throw new GameMcpInvalidParamsException(
                        "replacementUuid is required for rotate_out");
                builder.Amount = OptionalIntInRange(
                    arguments, "amount", 1, 1, 1_000_000);
                break;
            case "game_harvest":
                builder.Uuid = RequireUuid(arguments, "plotNodeUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                break;
            case "game_spell_level":
                builder.Uuid = RequireUuid(arguments, "spellRecipeUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                builder.Mode = RequireOneOf(arguments, "mode", "single", "all");
                break;
            case "game_discovery_offer":
                builder.Uuid = RequireUuid(arguments, "treeUuid");
                builder.SecondaryUuid = OptionalUuid(arguments, "offerUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                builder.Mode = RequireOneOf(
                    arguments, "mode", "initiate", "select", "confirm", "reroll");
                if (builder.Mode is "select" or "confirm" &&
                    builder.SecondaryUuid == Guid.Empty)
                    throw new GameMcpInvalidParamsException(
                        "offerUuid is required for " + builder.Mode);
                if (builder.Mode is "initiate" or "reroll" &&
                    builder.SecondaryUuid != Guid.Empty)
                    throw new GameMcpInvalidParamsException(
                        "offerUuid is accepted only for select or confirm");
                break;
            case "game_spell_workbench":
                builder.Uuid = RequireUuid(arguments, "spellRecipeUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                builder.Mode = RequireOneOf(arguments, "mode", "select", "discover", "create");
                break;
            case "game_spell_composition":
                builder.Mode = RequireOneOf(arguments, "mode", "set_output_level", "set_augments");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                if (builder.Mode == "set_output_level")
                    builder.Amount = RequiredInt(arguments, "outputLevel", 1, int.MaxValue);
                else
                {
                    builder.Uuid = RequireUuid(arguments, "spellInstanceUuid");
                    builder.UuidCounts = RequireUuidCountArray(arguments, "augmentGlyphs", 64);
                }
                break;
            case "game_spell_loadout":
                builder.Mode = RequireOneOf(arguments, "mode", "remove", "move");
                builder.Uuid = RequireUuid(arguments, "spellInstanceUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                if (builder.Mode == "move")
                    builder.SlotIndex = RequiredInt(arguments, "destinationSlot", 0, 255);
                break;
            case "game_targeting":
                builder.Mode = RequireOneOf(arguments, "mode", "submit", "randomize", "cancel");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                if (builder.Mode == "submit") builder.Uuid = RequireUuid(arguments, "targetUuid");
                break;
            case "game_consumable":
                builder.Mode = RequireOneOf(
                    arguments,
                    "mode",
                    "use",
                    "cancel",
                    "discard",
                    "set_randomization",
                    "move");
                builder.Uuid = RequireUuid(arguments, "consumableUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                if (builder.Mode == "discard")
                    builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
                if (builder.Mode == "set_randomization")
                    builder.SerializedValue = OptionalBool(arguments, "enabled", false)
                        ? "true"
                        : "false";
                if (builder.Mode == "move")
                {
                    builder.Key = RequireOneOf(arguments, "list", "inventory", "hotbar");
                    builder.SlotIndex = RequiredInt(arguments, "destination", 0, int.MaxValue);
                }
                break;
            case "game_craft":
                builder.Mode = "craft";
                builder.Uuid = RequireUuid(arguments, "recipeUuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                break;
            case "game_discover":
                builder.Mode = "discover";
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                break;
            case "game_equipment":
                builder.Mode = RequireOneOf(arguments, "mode", "equip", "unequip");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.ExpectedNativeType = OptionalString(arguments, "expectedNativeType");
                break;
            case "suite_config_set":
                builder.ConfigurationGeneration = RequiredUlong(
                    arguments, "configurationGeneration");
                builder.Section = RequireString(arguments, "section");
                builder.Key = RequireString(arguments, "key");
                builder.SerializedValue = RequireRawString(arguments, "serializedValue");
                break;
            case "suite_emergency_stop":
                builder.ConfigurationGeneration = RequiredUlong(
                    arguments, "configurationGeneration");
                builder.Mode = RequireOneOf(arguments, "mode", "engage", "resume");
                break;
            case "game_screenshot":
                builder.SaveCapture = OptionalBool(arguments, "save", false);
                break;
            case "game_navigate":
                builder.Tab = ParseNavigationSelector(RequireSelector(arguments, "tab"));
                if (arguments.TryGetValue("subtab", out _))
                    builder.Subtab = ParseNavigationSelector(RequireSelector(arguments, "subtab"));
                builder.Uuid = OptionalUuid(arguments, "plotNodeUuid");
                builder.Capture = OptionalBool(arguments, "capture", false);
                break;
            case "game_tooltips":
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                break;
            case "game_tooltip":
                builder.Path = RequireString(arguments, "path");
                builder.Capture = OptionalBool(arguments, "capture", false);
                break;
            case "game_probe":
                builder.Probe = RequireOneOf(
                    arguments, "probe", "runtime", "action_queue_room", "navigation");
                break;
            default:
                throw new GameMcpInvalidParamsException(
                    "unknown tool '" + name + "'; call tools/list");
        }
        builder.Classification = Classification(name, builder);
        builder.RequiredData = RequiredData(name, builder);
        return builder.Freeze();
    }

    private static GameMcpNavigationSelector ParseNavigationSelector(string label) => new(label);

    private static bool IsKnownResourceUri(string uri) =>
        uri is "orb://world/overview" or
            "orb://world/categories" or
            "orb://suite/health" or
            "orb://suite/configuration" or
            "orb://trace/health" ||
        uri.StartsWith("orb://world/category/", StringComparison.Ordinal);

    private static GameMcpFrameData ResourceData(string uri) => uri switch
    {
        "orb://suite/health" => RequiredData("suite_health"),
        "orb://suite/configuration" => RequiredData("suite_configuration"),
        "orb://trace/health" => RequiredData("trace_health"),
        _ => GameMcpFrameData.World,
    };

    private static GameMcpOperationClass Classification(
        string name,
        GameMcpOperationRequestBuilder request) => name switch
    {
        "game_purchase" or "game_cast" or "game_concept" or "game_harvest" or
            "game_spell_level" or "game_discovery_offer" or "game_spell_workbench" or
            "game_spell_composition" or "game_spell_loadout" or "game_targeting" or
            "game_consumable" or "game_craft" or "game_discover" or "game_equipment" =>
                GameMcpOperationClass.Gameplay,
        "game_navigate" or "game_continue" => GameMcpOperationClass.UiState,
        "game_tooltip" when request.Capture => GameMcpOperationClass.UiState,
        "game_screenshot" when request.SaveCapture => GameMcpOperationClass.SuiteAdministration,
        "suite_config_set" or "suite_emergency_stop" =>
            GameMcpOperationClass.SuiteAdministration,
        _ => GameMcpOperationClass.ReadOnly,
    };

    private static GameMcpFrameData RequiredData(
        string name,
        GameMcpOperationRequestBuilder? request = null) => name switch
    {
        "entity_catalog" => GameMcpFrameData.None,
        "world_overview" or "world_categories" or "world_list" or "world_get" or
            "world_search" or "explain_entity" => GameMcpFrameData.World,
        "suite_health" => GameMcpFrameData.World | GameMcpFrameData.Configuration |
            GameMcpFrameData.FeatureHealth | GameMcpFrameData.ServiceHealth |
            GameMcpFrameData.Scene | GameMcpFrameData.NativeContractHealth,
        "suite_configuration" or "suite_config_set" =>
            GameMcpFrameData.Configuration | GameMcpFrameData.WritableConfiguration,
        "trace_health" => GameMcpFrameData.TraceWriterHealth,
        "suite_emergency_stop" => GameMcpFrameData.Configuration,
        "game_purchase" or "game_cast" or "game_concept" or "game_harvest" or
            "game_spell_level" or "game_discovery_offer" or "game_spell_workbench" or
            "game_spell_composition" or "game_spell_loadout" or "game_targeting" or
            "game_consumable" or "game_craft" or "game_discover" or "game_equipment" =>
            GameMcpFrameData.World | GameMcpFrameData.Configuration,
        "game_screenshot" when request?.SaveCapture == true => GameMcpFrameData.Configuration,
        "game_screenshot" or "game_navigate" or "game_probe" or "game_continue" or
            "game_screen_catalog" or "game_tooltips" or "game_tooltip" =>
            GameMcpFrameData.None,
        _ => throw new InvalidOperationException("no frame-data policy exists for tool " + name),
    };


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
                "Get exact world rows",
                "Read one or more stable UUIDs as one ordered result list from exactly one immutable world generation. expectedNativeType is an optional fail-closed assertion.",
                WorldGetSchema()),
            Tool(
                "entity_catalog",
                "Search live entity catalog",
                "Search every UUID loaded in the game's runtime registry by native type, internal asset name, and available player-facing display name, including loaded entities hidden by progression.",
                ObjectSchema(
                    new JObject
                    {
                        ["query"] = StringSchema("Case-insensitive UUID, native type, internal name, or player-facing display name fragment."),
                        ["limit"] = IntegerSchema(1, 200),
                    },
                    "query")),
            Tool(
                "explain_entity",
                "Explain one entity",
                "Evaluate visibility, availability, player verbs, recursive prerequisites, thresholds, exact costs, affordability, and typed blockers for one UUID from exactly one immutable world generation.",
                ObjectSchema(
                    new JObject
                    {
                        ["uuid"] = StringSchema("Canonical stable entity UUID."),
                    },
                    "uuid")),
            Tool(
                "world_search",
                "Search published entities",
                "Search stable-UUID entity categories only. Composite diagnostic categories are intentionally excluded; use world_list for those rows and their localized partiality evidence.",
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
                "Read one compact scene/runtime/STOP/native-contract line plus feature and service names grouped by state.",
                ObjectSchema()),
            Tool("suite_configuration", "Read committed configuration", "Read the committed writable setting catalog, current serialized values, and configuration generation.", ObjectSchema()),
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
                "game_discovery_offer",
                "Drive a Discovery Tree offer",
                "Initiate a paid discovery, select or confirm one exact live offer, or reroll. Success returns the newer named tree state; failures retain the decomposed receipt.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("initiate", "select", "confirm", "reroll"),
                        ["treeUuid"] = StringSchema("Published DiscoveryTreeSO UUID."),
                        ["offerUuid"] = StringSchema("Required for select and confirm; must be in the current live native offer set."),
                    },
                    "mode", "treeUuid"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_spell_workbench",
                "Select, discover, or create a spell",
                "Drive the native base-recipe workbench. Success returns the newer named recipe state with costs, holdings, selection, and next action inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("select", "discover", "create"),
                        ["spellRecipeUuid"] = StringSchema("Published SpellRecipeSO UUID."),
                    },
                    "mode", "spellRecipeUuid"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_spell_composition",
                "Set spell output level or augments",
                "Set the global spell output level or replace one equipped spell instance's exact augment stacks. Success returns the newer named composition state inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("set_output_level", "set_augments"),
                        ["outputLevel"] = IntegerSchema(1, int.MaxValue),
                        ["spellInstanceUuid"] = StringSchema("Runtime spell-instance UUID published inline under an equipped spell recipe."),
                        ["augmentGlyphs"] = ArraySchema(
                            ObjectSchema(
                                new JObject
                                {
                                    ["uuid"] = StringSchema("Published GlyphSO UUID."),
                                    ["count"] = IntegerSchema(1, int.MaxValue),
                                },
                                "uuid", "count"),
                            0,
                            64),
                    },
                    "mode"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_spell_loadout",
                "Remove or move an equipped spell",
                "Remove one exact runtime spell or move it to another native loadout slot. Success returns the complete newer named loadout and every next move/remove option inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("remove", "move"),
                        ["spellInstanceUuid"] = StringSchema("Runtime spell-instance UUID published by spell-slots or an equipped spell row."),
                        ["destinationSlot"] = IntegerSchema(0, 255),
                    },
                    "mode", "spellInstanceUuid"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_targeting",
                "Submit, randomize, or cancel the pending target",
                "Resolve the game's one current target request. Success returns the exact submitted structure and the complete next pending request inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("submit", "randomize", "cancel"),
                        ["targetUuid"] = StringSchema("Required only for submit; use an eligible named targeting candidate UUID."),
                    },
                    "mode"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_consumable",
                "Use or organize consumables",
                "Use, cancel, discard, randomize, or reorder one published consumable. Success returns the newer named holding, ordered inventory and hotbar, and next decisions inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "use", "cancel", "discard", "set_randomization", "move"),
                        ["consumableUuid"] = StringSchema("Published ConsumableSO UUID."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                        ["enabled"] = BooleanSchema("Requested randomization state."),
                        ["list"] = EnumSchema("inventory", "hotbar"),
                        ["destination"] = IntegerSchema(0, int.MaxValue),
                    },
                    "mode", "consumableUuid"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_craft",
                "Craft one recipe",
                "Execute the exact published direct or queued one-shot recipe. Success returns the newer named recipe, exact next cost and holdings, queue state, and next decision inline.",
                ActionSchema(
                    new JObject
                    {
                        ["recipeUuid"] = StringSchema("Published CraftingRecipeSO UUID."),
                    },
                    "recipeUuid"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_discover",
                "Discover one entity",
                "Pay and discover one exact published generic discoverable. Success returns the newer named entity row with its resulting state and next decisions inline.",
                ActionSchema(
                    new JObject
                    {
                        ["uuid"] = StringSchema("Published AlchemyRecipeSO, EquipmentSO, GlyphSO, RitualSO, or TimeRuneSO UUID."),
                    },
                    "uuid"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_equipment",
                "Equip or unequip an artifact",
                "Apply one native equipment click using the live multi-buy, slot, type-slot, stack, and usage-cost decision. Success returns the newer fully named artifact row and every next loadout decision inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("equip", "unequip"),
                        ["uuid"] = StringSchema("Published EquipmentSO UUID."),
                    },
                    "mode", "uuid"),
                readOnly: false,
                idempotent: false),
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
                "List live top tabs with the active tab marked and its current subtabs grouped beneath it.",
                ObjectSchema()),
            Tool(
                "game_navigate",
                "Navigate the live screen catalog",
                "UI-only, no gameplay/save mutation. Select one catalog tab, optional subtab, and optional published plot node; capture returns an inline PNG after arrival.",
                ObjectSchema(
                    new JObject
                    {
                        ["tab"] = StringSchema("Exact player-facing tab name."),
                        ["subtab"] = StringSchema("Optional exact player-facing subtab name."),
                        ["plotNodeUuid"] = StringSchema("Optional published plot UUID to select after navigation."),
                        ["capture"] = BooleanSchema("Return an inline PNG after arrival."),
                    },
                    "tab"),
                readOnly: false,
                idempotent: false,
                classification: "UI-only, no gameplay/save mutation"),
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
                "Read typed authored/computed TooltipNode trees, nested links, and open inspected panels for one exact path; optionally capture the opened tooltip.",
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

    private static JObject WorldGetSchema()
    {
        return ObjectSchema(
            new JObject
            {
                ["category"] = StringSchema("Exact name returned by world_categories."),
                ["uuids"] = ArraySchema(
                    StringSchema("Canonical D-format stable UUID."),
                    1,
                    GameMcpWorldQuery.MaximumBatchSize),
                ["expectedNativeType"] =
                    StringSchema("Optional exact assertion returned by world_categories."),
            },
            "category", "uuids");
    }

    private static void ValidateToolArguments(string name, JObject arguments)
    {
        var tool = (ListTools()["tools"] as JArray ?? new JArray())
            .OfType<JObject>()
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate["name"],
                name,
                StringComparison.Ordinal));
        if (tool?["inputSchema"] is not JObject schema) return;

        var properties = schema["properties"] as JObject ?? new JObject();
        var errors = new JArray();
        if (schema["required"] is JArray required)
        {
            foreach (var field in required.Values<string>())
            {
                if (field is not null && !arguments.ContainsKey(field))
                    errors.Add(ValidationError(
                        "missing_required",
                        field,
                        "required field '" + field + "' is missing"));
            }
        }

        foreach (var supplied in arguments.Properties())
        {
            if (properties.ContainsKey(supplied.Name)) continue;
            errors.Add(ValidationError(
                "unexpected_field",
                supplied.Name,
                "field '" + supplied.Name + "' is not accepted by " + name));
        }

        if (string.Equals(name, "game_discovery_offer", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var hasOffer = arguments.ContainsKey("offerUuid");
            if (mode is "select" or "confirm" && !hasOffer)
            {
                errors.Add(ValidationError(
                    "missing_required",
                    "offerUuid",
                    "required field 'offerUuid' is missing for mode '" + mode + "'"));
            }
            else if (mode is "initiate" or "reroll" && hasOffer)
            {
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "offerUuid",
                    "field 'offerUuid' is not accepted for mode '" + mode + "'"));
            }
        }

        if (string.Equals(name, "game_spell_composition", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var output = arguments.ContainsKey("outputLevel");
            var spell = arguments.ContainsKey("spellInstanceUuid");
            var augments = arguments.ContainsKey("augmentGlyphs");
            if (mode == "set_output_level")
            {
                if (!output) errors.Add(ValidationError(
                    "missing_required", "outputLevel",
                    "required field 'outputLevel' is missing for mode 'set_output_level'"));
                if (spell) errors.Add(ValidationError(
                    "unexpected_for_mode", "spellInstanceUuid",
                    "field 'spellInstanceUuid' is not accepted for mode 'set_output_level'"));
                if (augments) errors.Add(ValidationError(
                    "unexpected_for_mode", "augmentGlyphs",
                    "field 'augmentGlyphs' is not accepted for mode 'set_output_level'"));
            }
            else if (mode == "set_augments")
            {
                if (!spell) errors.Add(ValidationError(
                    "missing_required", "spellInstanceUuid",
                    "required field 'spellInstanceUuid' is missing for mode 'set_augments'"));
                if (!augments) errors.Add(ValidationError(
                    "missing_required", "augmentGlyphs",
                    "required field 'augmentGlyphs' is missing for mode 'set_augments'"));
                if (output) errors.Add(ValidationError(
                    "unexpected_for_mode", "outputLevel",
                    "field 'outputLevel' is not accepted for mode 'set_augments'"));
            }
        }

        if (string.Equals(name, "game_spell_loadout", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var destination = arguments.ContainsKey("destinationSlot");
            if (mode == "move" && !destination)
                errors.Add(ValidationError(
                    "missing_required",
                    "destinationSlot",
                    "required field 'destinationSlot' is missing for mode 'move'"));
            else if (mode == "remove" && destination)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "destinationSlot",
                    "field 'destinationSlot' is not accepted for mode 'remove'"));
        }

        if (string.Equals(name, "game_targeting", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var target = arguments.ContainsKey("targetUuid");
            if (mode == "submit" && !target)
                errors.Add(ValidationError("missing_required", "targetUuid",
                    "required field 'targetUuid' is missing for mode 'submit'"));
            else if (mode is "randomize" or "cancel" && target)
                errors.Add(ValidationError("unexpected_for_mode", "targetUuid",
                    "field 'targetUuid' is not accepted for mode '" + mode + "'"));
        }

        if (string.Equals(name, "game_consumable", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var amount = arguments.ContainsKey("amount");
            var enabled = arguments.ContainsKey("enabled");
            var list = arguments.ContainsKey("list");
            var destination = arguments.ContainsKey("destination");
            if (mode == "discard" && !amount)
                errors.Add(ValidationError(
                    "missing_required",
                    "amount",
                    "required field 'amount' is missing for mode 'discard'"));
            if (mode == "set_randomization" && !enabled)
                errors.Add(ValidationError(
                    "missing_required",
                    "enabled",
                    "required field 'enabled' is missing for mode 'set_randomization'"));
            if (mode == "move" && !list)
                errors.Add(ValidationError(
                    "missing_required",
                    "list",
                    "required field 'list' is missing for mode 'move'"));
            if (mode == "move" && !destination)
                errors.Add(ValidationError(
                    "missing_required",
                    "destination",
                    "required field 'destination' is missing for mode 'move'"));
            if (mode != "discard" && amount)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "amount",
                    "field 'amount' is accepted only for mode 'discard'"));
            if (mode != "set_randomization" && enabled)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "enabled",
                    "field 'enabled' is accepted only for mode 'set_randomization'"));
            if (mode != "move" && list)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "list",
                    "field 'list' is accepted only for mode 'move'"));
            if (mode != "move" && destination)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "destination",
                    "field 'destination' is accepted only for mode 'move'"));
        }

        if (errors.Count > 0) throw GameMcpInvalidParamsException.Validation(errors);
    }

    private static JObject ValidationError(string code, string field, string message) => new()
    {
        ["code"] = code,
        ["field"] = field,
        ["message"] = message,
    };

    private static JObject ActionSchema(JObject properties, params string[] required)
    {
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
        bool idempotent = true,
        string classification = "")
    {
        var result = new JObject
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
        if (classification.Length > 0) result["classification"] = classification;
        return result;
    }

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

    private static JObject ArraySchema(JObject items, int minimum, int maximum) => new()
    {
        ["type"] = "array",
        ["items"] = items,
        ["minItems"] = minimum,
        ["maxItems"] = maximum,
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

    private static string[] RequireStringArray(JObject source, string name, int maximum)
    {
        if (source[name] is not JArray array)
            throw new GameMcpInvalidParamsException(name + " must be an array");
        if (array.Count == 0 || array.Count > maximum)
            throw new GameMcpInvalidParamsException(
                name + " must contain between 1 and " + maximum + " entries");
        var result = new string[array.Count];
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index]?.Type != JTokenType.String)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must be a string");
            result[index] = ((string?)array[index] ?? string.Empty).Trim();
            if (result[index].Length == 0)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must not be empty");
        }
        return result;
    }

    private static GameMcpUuidCount[] RequireUuidCountArray(
        JObject source,
        string name,
        int maximum)
    {
        if (source[name] is not JArray values)
            throw new GameMcpInvalidParamsException(name + " must be an array");
        if (values.Count > maximum)
            throw new GameMcpInvalidParamsException(
                name + " accepts at most " + maximum + " rows");
        var result = new GameMcpUuidCount[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not JObject row)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must be an object");
            foreach (var property in row.Properties())
                if (property.Name is not "uuid" and not "count")
                    throw new GameMcpInvalidParamsException(
                        name + "[" + index + "]." + property.Name + " is unexpected");
            result[index] = new GameMcpUuidCount(
                RequireUuid(row, "uuid"),
                RequiredInt(row, "count", 1, int.MaxValue));
        }
        return result;
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

    private static string RequireSelector(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token))
            throw new GameMcpInvalidParamsException(name + " is required");
        if (token.Type == JTokenType.String)
        {
            var label = ((string?)token ?? string.Empty).Trim();
            if (label.Length == 0)
                throw new GameMcpInvalidParamsException(name + " must not be empty");
            return label;
        }
        throw new GameMcpInvalidParamsException(name + " must be an exact player-facing name");
    }

    private static JObject Success(JToken? id, JObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
        ["result"] = result,
    };

    internal static JObject Error(
        JToken? id,
        int code,
        string message,
        JObject? data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null) error["data"] = data;
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
            ["error"] = error,
        };
    }
}

internal sealed class GameMcpToolExecution
{
    internal GameMcpToolExecution(
        GameMcpValue payload,
        byte[]? inlinePng,
        bool isError,
        EntityIdentityCatalogSnapshot? entityIdentities = null)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        InlinePng = inlinePng;
        IsError = isError;
        EntityIdentities = entityIdentities ?? EntityIdentityCatalogPublication.Current;
    }

    private GameMcpToolExecution(string text)
    {
        TextContent = string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Text tool content must not be empty.", nameof(text))
            : text;
    }

    internal GameMcpValue? Payload { get; }
    internal byte[]? InlinePng { get; }
    internal bool IsError { get; }
    internal string? TextContent { get; }
    internal EntityIdentityCatalogSnapshot EntityIdentities { get; } =
        EntityIdentityCatalogSnapshot.Unbound(0);

    internal static GameMcpToolExecution Read(GameMcpValue payload) =>
        new(payload, null, false);
    internal static GameMcpToolExecution Read(GameMcpObjectBuilder payload) =>
        new(payload.Freeze(), null, false);
    internal static GameMcpToolExecution Error(GameMcpValue payload) =>
        new(payload, null, true);
    internal static GameMcpToolExecution Error(GameMcpObjectBuilder payload) =>
        new(payload.Freeze(), null, true);
    internal static GameMcpToolExecution Text(string text) => new(text);

    internal GameMcpToolExecution WithEntityIdentities(
        EntityIdentityCatalogSnapshot entityIdentities) =>
        TextContent is not null
            ? this
            : new GameMcpToolExecution(
                Payload!, InlinePng, IsError,
                entityIdentities ?? throw new ArgumentNullException(nameof(entityIdentities)));

    internal JObject ToProtocolResult()
    {
        if (TextContent is not null)
        {
            return new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = TextContent,
                    },
                },
            };
        }
        // The structured JSON is authoritative and emitted once. Content is reserved for media;
        // repeating the payload as text made clients decode and truncate the same result twice.
        var content = new JArray();
        if (InlinePng is not null)
        {
            content.Add(new JObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(InlinePng),
                ["mimeType"] = "image/png",
            });
        }
        var result = new JObject
        {
            ["structuredContent"] = GameMcpDocumentJsonEncoder.Encode(
                Payload!, EntityIdentities),
        };
        if (content.Count > 0) result["content"] = content;
        if (IsError) result["isError"] = true;
        return result;
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

    private GameMcpInvalidParamsException(string message, JObject dataObject)
        : base(message)
    {
        DataObject = dataObject;
    }

    internal JObject? DataObject { get; }

    internal static GameMcpInvalidParamsException Validation(JArray errors) =>
        new(
            "tool arguments failed schema validation",
            new JObject
            {
                ["kind"] = "argument_validation_failed",
                ["validationErrors"] = errors,
            });
}
#endif
