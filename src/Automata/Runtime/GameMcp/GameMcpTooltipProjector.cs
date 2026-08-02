#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OrbModding.Common;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Projects a tooltip graph once. Linked tooltips are references into one flat definition map;
/// cycles therefore cost one reference rather than another recursive subtree. Limit evidence is
/// aggregated once for the response instead of repeated at every cut edge.
/// </summary>
internal static class GameMcpTooltipProjector
{
    private const int MaximumDepth = 8;
    private const int MaximumNodes = 1_000;

    internal static JObject Project(
        ITooltipable primary,
        IEnumerable<ITooltipable>? authoredNested,
        IEnumerable<ITooltipable>? inspectedPanels)
    {
        if (primary is null) throw new ArgumentNullException(nameof(primary));

        var context = new ProjectionContext();
        var primaryKey = context.Key(primary);
        context.Projected.Add(primaryKey);
        var result = new JObject
        {
            ["source"] = "unity_main_thread",
            ["tooltip"] = ProjectTooltip(primary, 0, context),
            ["nestedTooltips"] = ProjectReferences(authoredNested, 1, context),
            ["inspectedPanels"] = ProjectReferences(inspectedPanels, 1, context),
        };
        if (context.Referenced.Count > 0)
            result["referencedTooltips"] = context.Referenced;
        if (context.Truncations.Count > 0)
        {
            var summary = new JArray();
            foreach (var pair in context.Truncations)
                summary.Add(new JObject
                {
                    ["reasonCode"] = pair.Key,
                    ["occurrences"] = pair.Value.Occurrences,
                    ["maximumDepth"] = pair.Value.MaximumDepth,
                });
            result["truncation"] = summary;
        }
        return result;
    }

    internal static int CountReachable(
        ITooltipable primary,
        IEnumerable<ITooltipable>? authoredNested)
    {
        if (primary is null) throw new ArgumentNullException(nameof(primary));
        var visited = new HashSet<ITooltipable>(ReferenceComparer.Instance);
        var pending = new Queue<ITooltipable>();
        pending.Enqueue(primary);
        if (authoredNested is not null)
            foreach (var item in authoredNested)
                if (item is not null) pending.Enqueue(item);
        while (pending.Count > 0 && visited.Count <= MaximumNodes)
        {
            var item = pending.Dequeue();
            if (!visited.Add(item)) continue;
            TryQueueNodes(item.GetTooltipNodes(), pending);
            if (item.HasAltTooltips()) TryQueueNodes(item.GetAltTooltipNodes(), pending);
        }
        return Math.Max(visited.Count - 1, 0);
    }

    private static void TryQueueNodes(
        IReadOnlyList<TooltipNode>? nodes,
        Queue<ITooltipable> pending)
    {
        if (nodes is null) return;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (node is null) continue;
            if (node.tooltipable is not null) pending.Enqueue(node.tooltipable);
            if (node.subTooltips is not null)
                for (var nested = 0; nested < node.subTooltips.Count; nested++)
                    if (node.subTooltips[nested] is not null)
                        pending.Enqueue(node.subTooltips[nested]);
            TryQueueNodes(node.children, pending);
        }
    }

    private static JArray ProjectReferences(
        IEnumerable<ITooltipable>? items,
        int depth,
        ProjectionContext context)
    {
        var result = new JArray();
        if (items is null) return result;
        foreach (var item in items)
            if (item is not null) result.Add(ProjectReference(item, depth, context));
        return result;
    }

    private static JObject ProjectReference(
        ITooltipable item,
        int depth,
        ProjectionContext context)
    {
        var key = context.Key(item);
        if (!context.Projected.Contains(key))
        {
            if (depth > MaximumDepth)
                context.Truncate("tooltip_depth_exceeded", depth);
            else
            {
                context.Projected.Add(key);
                context.Referenced[key] = ProjectTooltip(item, depth, context);
            }
        }
        return new JObject { ["ref"] = key };
    }

    private static JObject ProjectTooltip(
        ITooltipable item,
        int depth,
        ProjectionContext context)
    {
        var result = new JObject
        {
            ["name"] = item.GetName(),
            ["displayType"] = item.GetDisplayType(),
            ["description"] = item.GetDescription(),
        };
        TryAttachIdentity(result, item);
        result["nodes"] = ProjectNodes(item.GetTooltipNodes(), depth, context);
        if (item.HasAltTooltips())
        {
            var alternate = ProjectNodes(item.GetAltTooltipNodes(), depth, context);
            if (!Equivalent((GameMcpValue)result["nodes"]!, alternate))
                result["altNodes"] = alternate;
        }
        return result;
    }

    private static GameMcpArray ProjectNodes(
        IReadOnlyList<TooltipNode>? nodes,
        int depth,
        ProjectionContext context)
    {
        var result = new JArray();
        if (nodes is null) return result.Freeze();
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (node is null) continue;
            var projected = ProjectNode(node, depth, context);
            if (projected is not null) result.Add(projected);
        }
        return result.Freeze();
    }

    private static JObject? ProjectNode(
        TooltipNode node,
        int depth,
        ProjectionContext context)
    {
        if (depth > MaximumDepth)
        {
            context.Truncate("tooltip_depth_exceeded", depth);
            return null;
        }
        if (++context.NodeCount > MaximumNodes)
        {
            context.Truncate("tooltip_node_limit_exceeded", depth);
            return null;
        }

        string? text = null;
        string failure;
        try
        {
            text = node.textFn is null ? node.text : node.textFn();
            failure = string.Empty;
        }
        catch (Exception exception)
        {
            failure = exception.GetBaseException().Message;
        }

        var result = new JObject
        {
            ["kind"] = node.nodeType.ToString(),
            ["text"] = text,
            ["children"] = ProjectNodes(node.children, depth + 1, context),
        };
        if (failure.Length > 0)
        {
            result["status"] = "not_available";
            result["code"] = "tooltip_text_evaluation_failed";
            result["reason"] = failure;
        }
        if (node.tooltipable is not null)
            result["linkedTooltip"] = ProjectReference(node.tooltipable, depth + 1, context);
        result["subTooltips"] = ProjectReferences(node.subTooltips, depth + 1, context);
        return result;
    }

    private static void TryAttachIdentity(JObject result, ITooltipable item)
    {
        if (item is not IdScriptableObject) return;
        try
        {
            var uuid = RuntimeIdentityRegistryBinding.Shared.ReadStableUuid(item);
            if (uuid.HasValue && uuid.Value != Guid.Empty)
                result["uuid"] = uuid.Value.ToString("D");
        }
        catch (Exception)
        {
            // Optional identity enrichment never makes readable tooltip content unavailable.
        }
    }

    private static bool Equivalent(GameMcpValue left, GameMcpValue right)
    {
        if (left.GetType() != right.GetType()) return false;
        if (left is GameMcpNull) return true;
        if (left is GameMcpScalar leftScalar && right is GameMcpScalar rightScalar)
            return Equals(leftScalar.Value, rightScalar.Value);
        if (left is GameMcpArray leftArray && right is GameMcpArray rightArray)
        {
            if (leftArray.Items.Count != rightArray.Items.Count) return false;
            for (var index = 0; index < leftArray.Items.Count; index++)
                if (!Equivalent(leftArray.Items[index], rightArray.Items[index])) return false;
            return true;
        }
        if (left is GameMcpObject leftObject && right is GameMcpObject rightObject)
        {
            if (leftObject.Properties.Count != rightObject.Properties.Count) return false;
            for (var index = 0; index < leftObject.Properties.Count; index++)
            {
                var leftProperty = leftObject.Properties[index];
                var rightProperty = rightObject.Properties[index];
                if (!string.Equals(leftProperty.Name, rightProperty.Name, StringComparison.Ordinal) ||
                    !Equivalent(leftProperty.Value, rightProperty.Value)) return false;
            }
            return true;
        }
        return false;
    }

    private sealed class ProjectionContext
    {
        private readonly Dictionary<ITooltipable, string> _keys =
            new(ReferenceComparer.Instance);
        private int _nextReference;

        internal HashSet<string> Projected { get; } = new(StringComparer.Ordinal);
        internal JObject Referenced { get; } = new();
        internal SortedDictionary<string, TruncationCounter> Truncations { get; } =
            new(StringComparer.Ordinal);
        internal int NodeCount { get; set; }

        internal string Key(ITooltipable item)
        {
            if (_keys.TryGetValue(item, out var existing)) return existing;
            var identity = StableIdentity(item);
            var key = identity != Guid.Empty
                ? identity.ToString("D")
                : "tooltip_" + (++_nextReference).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            _keys.Add(item, key);
            return key;
        }

        internal void Truncate(string reason, int depth)
        {
            if (!Truncations.TryGetValue(reason, out var value)) value = default;
            Truncations[reason] = new TruncationCounter(
                value.Occurrences + 1,
                Math.Max(value.MaximumDepth, depth));
        }

        private static Guid StableIdentity(ITooltipable item)
        {
            if (item is not IdScriptableObject) return Guid.Empty;
            try
            {
                return RuntimeIdentityRegistryBinding.Shared.ReadStableUuid(item) ?? Guid.Empty;
            }
            catch (Exception) { return Guid.Empty; }
        }
    }

    private readonly struct TruncationCounter
    {
        internal TruncationCounter(int occurrences, int maximumDepth)
        { Occurrences = occurrences; MaximumDepth = maximumDepth; }
        internal int Occurrences { get; }
        internal int MaximumDepth { get; }
    }

    private sealed class ReferenceComparer : IEqualityComparer<ITooltipable>
    {
        internal static readonly ReferenceComparer Instance = new();
        public bool Equals(ITooltipable? left, ITooltipable? right) => ReferenceEquals(left, right);
        public int GetHashCode(ITooltipable value) => RuntimeHelpers.GetHashCode(value);
    }
}
#endif
