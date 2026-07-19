using System.Text;
using System.Text.Json;

namespace OrbModding.RuntimeReplay;

public static class ReplayJsonCodec
{
    private static readonly string[] LifecycleTransitions =
    {
        "SceneEntered", "SceneExited", "SaveLoadStarted", "SaveLoaded",
        "RegistryRebuilt", "RuntimeReady", "ResetStarted", "ResetCompleted",
        "NewGamePlusStarted"
    };

    public static RuntimeReplay Parse(string json)
    {
        using var document = ParseDocument(json, "replay");
        var root = RequireObject(document.RootElement, "replay");
        RejectUnknown(root, "replay", "schema", "schemaVersion", "replayId", "setup", "events");
        var schema = RequireString(root, "schema", "replay", 64);
        if (schema != RuntimeReplay.SchemaIdentifier)
            throw Error($"Unsupported schema '{schema}'; expected '{RuntimeReplay.SchemaIdentifier}'.");
        var version = RequireInt32(root, "schemaVersion", "replay");
        if (version != RuntimeReplay.CurrentSchemaVersion)
            throw Error($"Unsupported schemaVersion {version}; only version 1 is accepted.");
        var replayId = RequireIdentifier(root, "replayId", "replay", 80);
        var setup = ParseSetupElement(Require(root, "setup", "replay"));
        var eventArray = Require(root, "events", "replay");
        if (eventArray.ValueKind != JsonValueKind.Array)
            throw Error("replay.events must be an array.");
        var events = new List<ReplayEvent>();
        foreach (var item in eventArray.EnumerateArray())
            events.Add(ParseEventElement(item));
        ValidateEvents(events);
        return new RuntimeReplay(schema, version, replayId, setup, events.AsReadOnly());
    }

    public static ReplaySetup ParseSetup(string json)
    {
        using var document = ParseDocument(json, "setup");
        return ParseSetupElement(document.RootElement);
    }

    public static ReplayEvent ParseEvent(string json)
    {
        using var document = ParseDocument(json, "event");
        return ParseEventElement(document.RootElement);
    }

    public static string Write(RuntimeReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ValidateReplay(replay);
        using var stream = new MemoryStream();
        using (var writer = CreateWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", replay.Schema);
            writer.WriteNumber("schemaVersion", replay.SchemaVersion);
            writer.WriteString("replayId", replay.ReplayId);
            writer.WritePropertyName("setup");
            WriteSetup(writer, replay.Setup);
            writer.WritePropertyName("events");
            writer.WriteStartArray();
            foreach (var replayEvent in replay.Events)
                WriteEvent(writer, replayEvent);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return CanonicalText(stream);
    }

    public static string WriteSetup(ReplaySetup setup)
    {
        ValidateSetup(setup);
        using var stream = new MemoryStream();
        using (var writer = CreateWriter(stream)) WriteSetup(writer, setup);
        return CanonicalText(stream);
    }

    public static string WriteEvent(ReplayEvent replayEvent)
    {
        ValidateEvent(replayEvent);
        using var stream = new MemoryStream();
        using (var writer = CreateWriter(stream, indented: false)) WriteEvent(writer, replayEvent);
        return CanonicalText(stream);
    }

    private static ReplaySetup ParseSetupElement(JsonElement value)
    {
        var setup = RequireObject(value, "setup");
        RejectUnknown(setup, "setup", "queueCapacity", "primaryResource", "candidates");
        var capacity = RequireInt32(setup, "queueCapacity", "setup");
        var resourceValue = RequireObject(Require(setup, "primaryResource", "setup"), "setup.primaryResource");
        RejectUnknown(resourceValue, "setup.primaryResource", "uuid", "expectedNativeType", "initialQuantity");
        var primaryResource = new ReplayResource(
            ParseIdentity(resourceValue, "setup.primaryResource"),
            RequireDecimal(resourceValue, "initialQuantity", "setup.primaryResource"));
        var candidateArray = Require(setup, "candidates", "setup");
        if (candidateArray.ValueKind != JsonValueKind.Array)
            throw Error("setup.candidates must be an array.");
        var candidates = new List<ReplayCandidate>();
        foreach (var valueCandidate in candidateArray.EnumerateArray())
        {
            var candidate = RequireObject(valueCandidate, "setup.candidates[]");
            RejectUnknown(candidate, "setup.candidates[]", "uuid", "expectedNativeType", "baseCost", "costScaling", "available", "maximumLevel");
            candidates.Add(new ReplayCandidate(
                ParseIdentity(candidate, "setup.candidates[]"),
                RequireDecimal(candidate, "baseCost", "setup.candidates[]"),
                RequireDecimal(candidate, "costScaling", "setup.candidates[]"),
                RequireBoolean(candidate, "available", "setup.candidates[]"),
                OptionalInt32(candidate, "maximumLevel", "setup.candidates[]")));
        }
        var result = new ReplaySetup(capacity, primaryResource, candidates.AsReadOnly());
        ValidateSetup(result);
        return result;
    }

    private static ReplayEvent ParseEventElement(JsonElement value)
    {
        var item = RequireObject(value, "event");
        var kind = RequireString(item, "kind", "event", 32);
        var sequence = RequireInt32(item, "sequence", "event");
        var frame = RequireInt64(item, "atFrame", "event");
        var microseconds = RequireInt64(item, "atMicroseconds", "event");
        ReplayEvent result = kind switch
        {
            "lifecycle" => ParseLifecycle(item, sequence, frame, microseconds),
            "resource" => ParseResource(item, sequence, frame, microseconds),
            "queue" => ParseQueue(item, sequence, frame, microseconds),
            "progression" => ParseProgression(item, sequence, frame, microseconds),
            "inventory" => ParseInventory(item, sequence, frame, microseconds),
            "configuration" => ParseConfiguration(item, sequence, frame, microseconds),
            "completion" => ParseCompletion(item, sequence, frame, microseconds),
            _ => throw Error($"event.kind '{kind}' is not supported.")
        };
        ValidateEvent(result);
        return result;
    }

    private static ReplayEvent ParseLifecycle(JsonElement item, int sequence, long frame, long time)
    {
        RejectUnknown(item, "lifecycle event", "sequence", "atFrame", "atMicroseconds", "kind", "transition", "sceneName", "nativeIdentityToken");
        return new LifecycleReplayEvent(sequence, frame, time,
            RequireString(item, "transition", "lifecycle event", 64),
            RequireString(item, "sceneName", "lifecycle event", 64),
            RequireIdentifier(item, "nativeIdentityToken", "lifecycle event", 80));
    }

    private static ReplayEvent ParseResource(JsonElement item, int sequence, long frame, long time)
    {
        RejectUnknown(item, "resource event", "sequence", "atFrame", "atMicroseconds", "kind", "uuid", "expectedNativeType", "quantity");
        return new ResourceReplayEvent(sequence, frame, time, ParseIdentity(item, "resource event"), RequireDecimal(item, "quantity", "resource event"));
    }

    private static ReplayEvent ParseQueue(JsonElement item, int sequence, long frame, long time)
    {
        RejectUnknown(item, "queue event", "sequence", "atFrame", "atMicroseconds", "kind", "manualActions");
        return new QueueReplayEvent(sequence, frame, time, RequireInt32(item, "manualActions", "queue event"));
    }

    private static ReplayEvent ParseProgression(JsonElement item, int sequence, long frame, long time)
    {
        RejectUnknown(item, "progression event", "sequence", "atFrame", "atMicroseconds", "kind", "uuid", "expectedNativeType", "available");
        return new ProgressionReplayEvent(sequence, frame, time, ParseIdentity(item, "progression event"), RequireBoolean(item, "available", "progression event"));
    }

    private static ReplayEvent ParseInventory(JsonElement item, int sequence, long frame, long time)
    {
        RejectUnknown(item, "inventory event", "sequence", "atFrame", "atMicroseconds", "kind", "uuid", "expectedNativeType", "quantity");
        return new InventoryReplayEvent(sequence, frame, time, ParseIdentity(item, "inventory event"), RequireInt32(item, "quantity", "inventory event"));
    }

    private static ReplayEvent ParseConfiguration(JsonElement item, int sequence, long frame, long time)
    {
        RejectUnknown(item, "configuration event", "sequence", "atFrame", "atMicroseconds", "kind", "setting", "enabled");
        return new ConfigurationReplayEvent(sequence, frame, time, RequireString(item, "setting", "configuration event", 64), RequireBoolean(item, "enabled", "configuration event"));
    }

    private static ReplayEvent ParseCompletion(JsonElement item, int sequence, long frame, long time)
    {
        RejectUnknown(item, "completion event", "sequence", "atFrame", "atMicroseconds", "kind", "uuid", "expectedNativeType", "count");
        return new CompletionReplayEvent(sequence, frame, time, ParseIdentity(item, "completion event"), RequireInt32(item, "count", "completion event"));
    }

    private static void WriteSetup(Utf8JsonWriter writer, ReplaySetup setup)
    {
        writer.WriteStartObject();
        writer.WriteNumber("queueCapacity", setup.QueueCapacity);
        writer.WritePropertyName("primaryResource");
        writer.WriteStartObject();
        WriteIdentity(writer, setup.PrimaryResource.Identity);
        writer.WriteNumber("initialQuantity", setup.PrimaryResource.InitialQuantity);
        writer.WriteEndObject();
        writer.WritePropertyName("candidates");
        writer.WriteStartArray();
        foreach (var candidate in setup.Candidates)
        {
            writer.WriteStartObject();
            WriteIdentity(writer, candidate.Identity);
            writer.WriteNumber("baseCost", candidate.BaseCost);
            writer.WriteNumber("costScaling", candidate.CostScaling);
            writer.WriteBoolean("available", candidate.Available);
            if (candidate.MaximumLevel.HasValue) writer.WriteNumber("maximumLevel", candidate.MaximumLevel.Value);
            else writer.WriteNull("maximumLevel");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter writer, ReplayEvent replayEvent)
    {
        writer.WriteStartObject();
        writer.WriteNumber("sequence", replayEvent.Sequence);
        writer.WriteNumber("atFrame", replayEvent.AtFrame);
        writer.WriteNumber("atMicroseconds", replayEvent.AtMicroseconds);
        writer.WriteString("kind", replayEvent.Kind);
        switch (replayEvent)
        {
            case LifecycleReplayEvent value:
                writer.WriteString("transition", value.Transition);
                writer.WriteString("sceneName", value.SceneName);
                writer.WriteString("nativeIdentityToken", value.NativeIdentityToken);
                break;
            case ResourceReplayEvent value:
                WriteIdentity(writer, value.Identity);
                writer.WriteNumber("quantity", value.Quantity);
                break;
            case QueueReplayEvent value:
                writer.WriteNumber("manualActions", value.ManualActions);
                break;
            case ProgressionReplayEvent value:
                WriteIdentity(writer, value.Identity);
                writer.WriteBoolean("available", value.Available);
                break;
            case InventoryReplayEvent value:
                WriteIdentity(writer, value.Identity);
                writer.WriteNumber("quantity", value.Quantity);
                break;
            case ConfigurationReplayEvent value:
                writer.WriteString("setting", value.Setting);
                writer.WriteBoolean("enabled", value.Enabled);
                break;
            case CompletionReplayEvent value:
                WriteIdentity(writer, value.Identity);
                writer.WriteNumber("count", value.Count);
                break;
            default:
                throw Error($"Unsupported event type {replayEvent.GetType().Name}.");
        }
        writer.WriteEndObject();
    }

    private static void WriteIdentity(Utf8JsonWriter writer, ReplayIdentity identity)
    {
        writer.WriteString("uuid", identity.Uuid);
        writer.WriteString("expectedNativeType", identity.ExpectedNativeType);
    }

    private static void ValidateReplay(RuntimeReplay replay)
    {
        if (replay.Schema != RuntimeReplay.SchemaIdentifier) throw Error($"Only schema '{RuntimeReplay.SchemaIdentifier}' can be written.");
        if (replay.SchemaVersion != RuntimeReplay.CurrentSchemaVersion) throw Error("Only schemaVersion 1 can be written.");
        ValidateIdentifier(replay.ReplayId, "replayId", 80);
        ValidateSetup(replay.Setup);
        ValidateEvents(replay.Events);
    }

    private static void ValidateSetup(ReplaySetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.QueueCapacity < 1 || setup.QueueCapacity > 10000) throw Error("setup.queueCapacity must be between 1 and 10000.");
        if (setup.PrimaryResource is null) throw Error("setup.primaryResource is required.");
        ValidateIdentity(setup.PrimaryResource.Identity);
        if (setup.PrimaryResource.Identity.ExpectedNativeType != "ResourceSO") throw Error("setup.primaryResource requires expectedNativeType ResourceSO.");
        if (setup.PrimaryResource.InitialQuantity < 0) throw Error("setup.primaryResource.initialQuantity cannot be negative.");
        if (setup.Candidates is null || setup.Candidates.Count < 1 || setup.Candidates.Count > 10000) throw Error("setup.candidates must contain between 1 and 10000 entries.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in setup.Candidates)
        {
            ValidateIdentity(candidate.Identity);
            if (candidate.Identity.ExpectedNativeType is not ("StructureSO" or "UpgradeSO")) throw Error("Candidate expectedNativeType must be StructureSO or UpgradeSO.");
            if (!identities.Add(candidate.Identity.Uuid)) throw Error("Candidate UUIDs must be unique even across native types.");
            if (candidate.BaseCost < 0 || candidate.CostScaling <= 0) throw Error("Candidate costs must be non-negative with positive scaling.");
            if (candidate.MaximumLevel is <= 0) throw Error("Candidate maximumLevel must be null or positive.");
        }
    }

    private static void ValidateEvents(IReadOnlyList<ReplayEvent> events)
    {
        if (events is null || events.Count > 100000) throw Error("events exceeds the 100000 event limit.");
        long previousFrame = 0;
        long previousTime = 0;
        for (var index = 0; index < events.Count; index++)
        {
            var replayEvent = events[index];
            ValidateEvent(replayEvent);
            if (replayEvent.Sequence != index) throw Error("Event sequence values must start at zero and be contiguous.");
            if (replayEvent.AtFrame < previousFrame || replayEvent.AtMicroseconds < previousTime) throw Error("Events must be ordered by nondecreasing frame and microsecond values.");
            if (replayEvent.AtFrame == previousFrame && replayEvent.AtMicroseconds != previousTime)
                throw Error("Events at the same frame must use identical microseconds.");
            var frameDelta = replayEvent.AtFrame - previousFrame;
            var timeDelta = replayEvent.AtMicroseconds - previousTime;
            if (frameDelta > 0 && timeDelta % frameDelta != 0)
                throw Error("Every timestamp gap must be exactly divisible by its frame gap.");
            previousFrame = replayEvent.AtFrame;
            previousTime = replayEvent.AtMicroseconds;
        }
    }

    private static void ValidateEvent(ReplayEvent replayEvent)
    {
        ArgumentNullException.ThrowIfNull(replayEvent);
        if (replayEvent.Sequence < 0 || replayEvent.AtFrame < 0 || replayEvent.AtMicroseconds < 0) throw Error("Event sequence, frame, and microseconds must be non-negative integers.");
        if (replayEvent.AtFrame > RuntimeReplay.MaximumFrame)
            throw Error($"Event frames cannot exceed the V1 limit of {RuntimeReplay.MaximumFrame}.");
        if (replayEvent.AtMicroseconds > RuntimeReplay.MaximumMicroseconds)
            throw Error($"Event timestamps cannot exceed the V1 limit of {RuntimeReplay.MaximumMicroseconds} microseconds.");
        switch (replayEvent)
        {
            case LifecycleReplayEvent value:
                if (!LifecycleTransitions.Contains(value.Transition, StringComparer.Ordinal)) throw Error($"Unknown lifecycle transition '{value.Transition}'.");
                ValidateIdentifier(value.SceneName, "sceneName", 64);
                ValidateIdentifier(value.NativeIdentityToken, "nativeIdentityToken", 80);
                break;
            case ResourceReplayEvent value:
                ValidateIdentity(value.Identity);
                if (value.Identity.ExpectedNativeType != "ResourceSO") throw Error("Resource events require expectedNativeType ResourceSO.");
                if (value.Quantity < 0) throw Error("Resource quantity cannot be negative.");
                break;
            case QueueReplayEvent value when value.ManualActions < 0 || value.ManualActions > 10000:
                throw Error("manualActions must be between 0 and 10000.");
            case ProgressionReplayEvent value:
                ValidateIdentity(value.Identity);
                ValidateCandidateEventType(value.Identity.ExpectedNativeType, "Progression");
                break;
            case InventoryReplayEvent value:
                ValidateIdentity(value.Identity);
                if (value.Identity.ExpectedNativeType is not ("ArtifactSO" or "SpellSO" or "AlchemyRecipeSO"))
                    throw Error("Inventory events require expectedNativeType ArtifactSO, SpellSO, or AlchemyRecipeSO.");
                if (value.Quantity < 0) throw Error("Inventory quantity cannot be negative.");
                break;
            case ConfigurationReplayEvent value:
                if (value.Setting != "AutoBuyEnabled") throw Error("Only the reviewed AutoBuyEnabled configuration setting is accepted in V1.");
                break;
            case CompletionReplayEvent value:
                ValidateIdentity(value.Identity);
                ValidateCandidateEventType(value.Identity.ExpectedNativeType, "Completion");
                if (value.Count < 1 || value.Count > 10000) throw Error("Completion count must be between 1 and 10000.");
                break;
            case QueueReplayEvent:
                break;
            default:
                throw Error($"Unsupported event type {replayEvent.GetType().Name}.");
        }
    }

    private static ReplayIdentity ParseIdentity(JsonElement item, string path) =>
        new(RequireUuid(item, "uuid", path), RequireNativeType(item, "expectedNativeType", path));

    private static void ValidateIdentity(ReplayIdentity identity)
    {
        if (identity is null || !Guid.TryParseExact(identity.Uuid, "D", out var parsed) || parsed.ToString("D") != identity.Uuid)
            throw Error("Identity UUIDs must use lowercase canonical D format.");
        ValidateShortText(identity.ExpectedNativeType, "expectedNativeType", 128);
        if (identity.ExpectedNativeType.Contains('.') || identity.ExpectedNativeType.Contains('+')) throw Error("expectedNativeType must be an exact unqualified native type name.");
    }

    private static string RequireUuid(JsonElement item, string property, string path)
    {
        var value = RequireString(item, property, path, 36);
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed.ToString("D") != value) throw Error($"{path}.{property} must be a lowercase canonical UUID.");
        return value;
    }

    private static string RequireNativeType(JsonElement item, string property, string path)
    {
        var value = RequireString(item, property, path, 128);
        if (value.Contains('.') || value.Contains('+')) throw Error($"{path}.{property} must be an exact unqualified native type.");
        return value;
    }

    private static JsonDocument ParseDocument(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) throw Error($"{name} JSON is empty.");
        try { return JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 }); }
        catch (JsonException exception) { throw new ReplayFormatException($"Invalid {name} JSON: {exception.Message}"); }
    }

    private static JsonElement RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error($"{path} must be an object.");
        return value;
    }

    private static JsonElement Require(JsonElement item, string property, string path)
    {
        if (!item.TryGetProperty(property, out var value)) throw Error($"{path}.{property} is required.");
        return value;
    }

    private static string RequireString(JsonElement item, string property, string path, int maxLength)
    {
        var value = Require(item, property, path);
        if (value.ValueKind != JsonValueKind.String) throw Error($"{path}.{property} must be a string.");
        var text = value.GetString()!;
        ValidateShortText(text, path + "." + property, maxLength);
        return text;
    }

    private static string RequireIdentifier(JsonElement item, string property, string path, int maxLength)
    {
        var value = RequireString(item, property, path, maxLength);
        ValidateIdentifier(value, path + "." + property, maxLength);
        return value;
    }

    private static void ValidateIdentifier(string value, string path, int maxLength)
    {
        ValidateShortText(value, path, maxLength);
        if (value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))) throw Error($"{path} contains a disallowed character.");
    }

    private static void ValidateShortText(string value, string path, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl)) throw Error($"{path} must be nonblank, at most {maxLength} characters, and contain no control characters.");
    }

    private static int RequireInt32(JsonElement item, string property, string path)
    {
        var value = Require(item, property, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result)) throw Error($"{path}.{property} must be an integer.");
        return result;
    }

    private static int? OptionalInt32(JsonElement item, string property, string path)
    {
        var value = Require(item, property, path);
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result)) throw Error($"{path}.{property} must be an integer or null.");
        return result;
    }

    private static long RequireInt64(JsonElement item, string property, string path)
    {
        var value = Require(item, property, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result)) throw Error($"{path}.{property} must be an integer.");
        return result;
    }

    private static decimal RequireDecimal(JsonElement item, string property, string path)
    {
        var value = Require(item, property, path);
        var raw = value.GetRawText();
        if (value.ValueKind != JsonValueKind.Number ||
            raw.IndexOfAny(new[] { 'e', 'E' }) >= 0 ||
            !value.TryGetDecimal(out var result))
            throw Error($"{path}.{property} must be a finite non-exponent decimal number.");
        return result;
    }

    private static bool RequireBoolean(JsonElement item, string property, string path)
    {
        var value = Require(item, property, path);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Error($"{path}.{property} must be a boolean.");
        return value.GetBoolean();
    }

    private static void RejectUnknown(JsonElement item, string path, params string[] allowed)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var allowlist = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw Error($"{path} contains duplicate member '{property.Name}'.");
            if (!allowlist.Contains(property.Name)) throw Error($"{path} contains unknown member '{property.Name}'.");
        }
    }

    private static Utf8JsonWriter CreateWriter(Stream stream, bool indented = true) => new(stream, new JsonWriterOptions { Indented = indented });
    private static string CanonicalText(MemoryStream stream) => Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
    private static void ValidateCandidateEventType(string expectedNativeType, string eventName)
    {
        if (expectedNativeType is not ("StructureSO" or "UpgradeSO"))
            throw Error($"{eventName} events require expectedNativeType StructureSO or UpgradeSO.");
    }
    private static ReplayFormatException Error(string message) => new(message);
}
