#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OrbAutomata.GameMcp;

/// <summary>
/// One wire vocabulary for entity identity, status, reason codes, and flat tool results. The
/// profiler's embedded authored catalog is immutable after startup, so this HTTP-side pass performs
/// no Unity read and never copies mutable world state.
/// </summary>
internal static class GameMcpEntityWireNormalizer
{
    internal static JToken Normalize(JToken source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        NormalizeToken(source);
        return source;
    }

    private static void NormalizeToken(JToken token)
    {
        if (token is JObject item)
        {
            NormalizeObject(item);
            return;
        }
        if (token is not JArray array) return;
        for (var index = 0; index < array.Count; index++)
        {
            var value = array[index];
            if (value is JValue { Type: JTokenType.String } scalar &&
                Guid.TryParseExact((string?)scalar, "D", out var uuid) &&
                uuid != Guid.Empty)
            {
                array[index] = Reference(uuid);
                continue;
            }
            if (value is not null) NormalizeToken(value);
        }
    }

    private static void NormalizeObject(JObject item)
    {
        FlattenDetails(item);
        FlattenReading(item);
        Rename(item, "playerFacingName", "name");
        Rename(item, "availableAmount", "amount");
        Rename(item, "balance", "amount");
        Rename(item, "balanceBefore", "amountBefore");
        Rename(item, "balanceAfter", "amountAfter");
        Rename(item, "trueQuantity", "amount");
        Rename(item, "trueRate", "netRate");

        if (item["status"] is JValue statusValue)
        {
            var status = (string?)statusValue ?? string.Empty;
            item["status"] = status switch
            {
                "not_available" => "unavailable",
                "rejected" or "skipped" => "refused",
                _ => status,
            };
            if (status is "available" or "committed") item.Remove("code");
            else if (item["code"] is JToken code)
            {
                item["reasonCode"] = Snake((string?)code ?? string.Empty);
                item.Remove("code");
            }
        }
        if (item["reasonCode"] is JValue reasonCode)
            item["reasonCode"] = Snake((string?)reasonCode ?? string.Empty);
        if (item["kind"] is JValue { Type: JTokenType.String } kind)
            item["kind"] = Snake((string?)kind ?? string.Empty);

        if (item["mcpCategory"] is JToken category)
        {
            item["category"] = category;
            item.Remove("mcpCategory");
        }
        if (item["expectedNativeType"] is JToken nativeType)
        {
            item["nativeType"] = nativeType;
            item.Remove("expectedNativeType");
        }

        var properties = new List<JProperty>(item.Properties());
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            if (property.Parent is null) continue;
            if (property.Value is JValue { Type: JTokenType.String } scalar &&
                Guid.TryParseExact((string?)scalar, "D", out var uuid))
            {
                NormalizeIdentity(item, property, uuid);
                continue;
            }
            NormalizeToken(property.Value);
        }

        RemovePassingPredicates(item);
        DeduplicateChildIdentity(item, "state");
        RemoveEqualPair(item);
        PromoteIdentity(item);
    }

    private static void NormalizeIdentity(JObject parent, JProperty property, Guid uuid)
    {
        if (uuid == Guid.Empty)
        {
            property.Remove();
            return;
        }
        if (property.Name == "uuid")
        {
            AddIdentityFields(parent, uuid);
            return;
        }
        if (!property.Name.EndsWith("Id", StringComparison.Ordinal) &&
            !property.Name.EndsWith("Uuid", StringComparison.Ordinal))
        {
            property.Value = Reference(uuid);
            return;
        }

        var rootUuid = (string?)parent["uuid"];
        if (rootUuid is not null &&
            Guid.TryParseExact(rootUuid, "D", out var existing) &&
            existing == uuid)
        {
            property.Remove();
            return;
        }

        if (property.Name == "entityId")
        {
            property.Remove();
            parent["uuid"] = uuid.ToString("D");
            AddIdentityFields(parent, uuid);
            return;
        }

        var suffixLength = property.Name.EndsWith("Uuid", StringComparison.Ordinal) ? 4 : 2;
        var role = property.Name.Substring(0, property.Name.Length - suffixLength);
        property.Remove();
        parent[role] = Reference(uuid);
    }

    private static JObject Reference(Guid uuid)
    {
        var result = new JObject { ["uuid"] = uuid.ToString("D") };
        AddIdentityFields(result, uuid);
        return result;
    }

    private static void AddIdentityFields(JObject target, Guid uuid)
    {
        if (!GameMcpEntityCatalog.TryGet(uuid, out var identity))
        {
            target["nameEvidence"] = new JObject
            {
                ["status"] = "unavailable",
                ["reasonCode"] = "entity_name_unavailable",
                ["reason"] = "the authored catalog has no player-facing name for this stable UUID",
            };
            return;
        }
        target["name"] = identity.Name;
        if (!string.Equals(identity.InternalName, identity.Name, StringComparison.Ordinal))
            target["internalName"] = identity.InternalName;
        if (identity.Category.Length > 0) target["category"] = identity.Category;
        if (identity.NativeType.Length > 0) target["nativeType"] = identity.NativeType;
    }

    private static void FlattenDetails(JObject item)
    {
        if (item["details"] is not JObject details) return;
        foreach (var property in new List<JProperty>(details.Properties()))
        {
            if (item[property.Name] is null)
                item[property.Name] = property.Value;
        }
        item.Remove("details");
    }

    private static void FlattenReading(JObject item)
    {
        if (item["reading"] is not JObject reading) return;
        foreach (var property in new List<JProperty>(reading.Properties()))
        {
            if (item[property.Name] is null)
                item[property.Name] = property.Value;
        }
        item.Remove("reading");
    }

    private static void Rename(JObject item, string from, string to)
    {
        if (item[from] is not JToken value) return;
        if (item[to] is null) item[to] = value;
        item.Remove(from);
    }

    private static void RemovePassingPredicates(JObject item)
    {
        if (item["predicates"] is not JObject predicates) return;
        foreach (var property in new List<JProperty>(predicates.Properties()))
        {
            if (property.Value is not JObject predicate ||
                predicate["value"]?.Type != JTokenType.Boolean ||
                (bool)predicate["value"]! != true)
            {
                continue;
            }
            property.Remove();
        }
        if (!predicates.HasValues) item.Remove("predicates");
    }

    private static void DeduplicateChildIdentity(JObject item, string field)
    {
        if (item["uuid"] is not JToken root || item[field] is not JObject child ||
            child["uuid"] is not JToken nested || !JToken.DeepEquals(root, nested))
        {
            return;
        }
        child.Remove("uuid");
        child.Remove("name");
        child.Remove("internalName");
        child.Remove("category");
        child.Remove("nativeType");
    }

    private static void RemoveEqualPair(JObject item)
    {
        if (item.Count != 2 || item["before"] is not JToken before ||
            item["after"] is not JToken after || !JToken.DeepEquals(before, after))
        {
            return;
        }
        item.RemoveAll();
    }

    private static void PromoteIdentity(JObject item)
    {
        if (item["uuid"] is null) return;
        var fields = new[] { "nativeType", "category", "internalName", "name", "uuid" };
        for (var index = 0; index < fields.Length; index++)
        {
            var property = item.Property(fields[index]);
            if (property is null) continue;
            property.Remove();
            item.AddFirst(property);
        }
        var status = item.Property("status");
        if (status is null) return;
        status.Remove();
        item.AddFirst(status);
    }

    internal static string Snake(string value)
    {
        if (value.Length == 0) return value;
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current))
            {
                if (index > 0 && result[result.Length - 1] != '_') result.Append('_');
                result.Append(char.ToLowerInvariant(current));
            }
            else if (current == ' ' || current == '-')
            {
                if (result.Length > 0 && result[result.Length - 1] != '_') result.Append('_');
            }
            else result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }
}
#endif
