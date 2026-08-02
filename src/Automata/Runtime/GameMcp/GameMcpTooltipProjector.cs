#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Projects the native tooltip document into bounded, typed rows. It evaluates only the text
/// delegate a <c>TooltipNode</c> explicitly authors; it does not render UI or follow click handlers.
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
        var result = new JObject
        {
            ["source"] = "unity_main_thread",
            ["tooltip"] = ProjectTooltip(primary, "primary", "primary", 0, context),
        };
        var nested = ProjectMany(authoredNested, "authored_nested", "nested", 1, context);
        if (nested.Count > 0) result["nestedTooltips"] = nested;
        var inspected = ProjectMany(inspectedPanels, "inspected_panel", "inspected", 1, context);
        if (inspected.Count > 0) result["inspectedPanels"] = inspected;
        return result;
    }

    private static JArray ProjectMany(
        IEnumerable<ITooltipable>? items,
        string role,
        string path,
        int depth,
        ProjectionContext context)
    {
        var result = new JArray();
        if (items is null) return result;
        var index = 0;
        foreach (var item in items)
        {
            if (item is not null)
                result.Add(ProjectTooltip(item, role, path + "/" + index, depth, context));
            index++;
        }
        return result;
    }

    private static JObject ProjectTooltip(
        ITooltipable item,
        string role,
        string path,
        int depth,
        ProjectionContext context)
    {
        if (depth > MaximumDepth)
            return Limited("tooltip_depth_exceeded", depth);
        if (!context.Tooltips.Add(item))
            return Limited("tooltip_cycle", depth);

        try
        {
            var result = new JObject
            {
                ["name"] = item.GetName(),
                ["displayType"] = item.GetDisplayType(),
                ["description"] = item.GetDescription(),
            };
            TryAttachIdentity(result, item);
            var nodes = ProjectNodes(item.GetTooltipNodes(), path + "/nodes", depth, context);
            if (nodes.Items.Count > 0) result["nodes"] = nodes;
            if (item.HasAltTooltips())
            {
                var altNodes = ProjectNodes(
                    item.GetAltTooltipNodes(), path + "/altNodes", depth, context);
                if (altNodes.Items.Count > 0 && !Equivalent(nodes, altNodes))
                    result["altNodes"] = altNodes;
            }
            return result;
        }
        finally
        {
            context.Tooltips.Remove(item);
        }
    }

    private static GameMcpArray ProjectNodes(
        IReadOnlyList<TooltipNode>? nodes,
        string path,
        int depth,
        ProjectionContext context)
    {
        var result = new JArray();
        if (nodes is null) return result.Freeze();
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (node is null) continue;
            result.Add(ProjectNode(node, index, path + "/" + index, depth, context));
        }
        return result.Freeze();
    }

    private static JObject ProjectNode(
        TooltipNode node,
        int ordinal,
        string path,
        int depth,
        ProjectionContext context)
    {
        if (depth > MaximumDepth)
            return Limited("tooltip_depth_exceeded", depth);
        if (++context.NodeCount > MaximumNodes)
            return Limited("tooltip_node_limit_exceeded", depth);

        string? computedText = null;
        string computationReason;
        try
        {
            computedText = node.textFn is null ? node.text : node.textFn();
            computationReason = string.Empty;
        }
        catch (Exception exception)
        {
            computationReason = exception.GetBaseException().Message;
        }

        var result = new JObject
        {
            ["kind"] = node.nodeType.ToString(),
            ["text"] = computedText,
        };
        if (computationReason.Length > 0)
        {
            result["status"] = "not_available";
            result["code"] = "tooltip_text_evaluation_failed";
            result["reason"] = computationReason;
        }
        var children = ProjectNodes(node.children, path + "/children", depth + 1, context);
        if (children.Items.Count > 0) result["children"] = children;
        if (node.tooltipable is not null)
            result["linkedTooltip"] = ProjectTooltip(
                node.tooltipable, "node_link", path + "/linkedTooltip", depth + 1, context);
        var subTooltips = ProjectMany(
            node.subTooltips, "node_subtooltip", path + "/subTooltips", depth + 1, context);
        if (subTooltips.Count > 0) result["subTooltips"] = subTooltips;
        return result;
    }

    private static JObject Limited(string code, int depth) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["truncatedAtDepth"] = depth,
    };

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
            // Tooltip content remains useful when optional identity enrichment is unavailable.
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
                    !Equivalent(leftProperty.Value, rightProperty.Value))
                    return false;
            }
            return true;
        }
        return false;
    }

    private sealed class ProjectionContext
    {
        internal HashSet<ITooltipable> Tooltips { get; } = new();
        internal int NodeCount { get; set; }
    }
}
#endif
