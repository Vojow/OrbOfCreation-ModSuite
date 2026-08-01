#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
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
            ["dataSource"] = "direct_unity_main_thread_read",
            ["collectorGap"] =
                "Tooltip documents are UI-local and are not published by the world collector.",
            ["tooltip"] = ProjectTooltip(primary, "primary", "primary", 0, context),
            ["nodeLimit"] = MaximumNodes,
            ["depthLimit"] = MaximumDepth,
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
            return Limited(path, role, "tooltip_depth_exceeded");
        if (!context.Tooltips.Add(item))
            return Limited(path, role, "tooltip_cycle");

        try
        {
            var result = new JObject
            {
                ["role"] = role,
                ["path"] = path,
                ["depth"] = depth,
                ["name"] = item.GetName(),
                ["displayType"] = item.GetDisplayType(),
                ["description"] = item.GetDescription(),
                ["hasAltTooltips"] = item.HasAltTooltips(),
            };
            var nodes = ProjectNodes(item.GetTooltipNodes(), path + "/nodes", depth, context);
            if (nodes.Count > 0) result["nodes"] = nodes;
            if (item.HasAltTooltips())
            {
                var altNodes = ProjectNodes(
                    item.GetAltTooltipNodes(), path + "/altNodes", depth, context);
                if (altNodes.Count > 0) result["altNodes"] = altNodes;
            }
            return result;
        }
        finally
        {
            context.Tooltips.Remove(item);
        }
    }

    private static JArray ProjectNodes(
        IReadOnlyList<TooltipNode>? nodes,
        string path,
        int depth,
        ProjectionContext context)
    {
        var result = new JArray();
        if (nodes is null) return result;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (node is null) continue;
            result.Add(ProjectNode(node, index, path + "/" + index, depth, context));
        }
        return result;
    }

    private static JObject ProjectNode(
        TooltipNode node,
        int ordinal,
        string path,
        int depth,
        ProjectionContext context)
    {
        if (depth > MaximumDepth)
            return Limited(path, "node", "tooltip_depth_exceeded");
        if (++context.NodeCount > MaximumNodes)
            return Limited(path, "node", "tooltip_node_limit_exceeded");

        var textKind = node.textFn is null ? "authored" : "computed";
        string? computedText = null;
        string computationStatus;
        string computationReason;
        try
        {
            computedText = node.textFn is null ? node.text : node.textFn();
            computationStatus = "available";
            computationReason = string.Empty;
        }
        catch (Exception exception)
        {
            computationStatus = "not_available";
            computationReason = exception.GetBaseException().Message;
        }

        var result = new JObject
        {
            ["path"] = path,
            ["depth"] = depth,
            ["ordinal"] = ordinal,
            ["nodeKind"] = node.nodeType.ToString(),
            ["parentKind"] = node.parentType.ToString(),
            ["textKind"] = textKind,
            ["text"] = computedText,
            ["computationStatus"] = computationStatus,
            ["hasIcon"] = node.icon is not null,
            ["iconBacked"] = node.isIconBacked,
            ["size"] = node.size,
            ["color"] = Color(node.color),
            ["textColor"] = Color(node.textColor),
        };
        if (!string.IsNullOrEmpty(node.text)) result["authoredText"] = node.text;
        if (computationReason.Length > 0) result["computationReason"] = computationReason;
        var children = ProjectNodes(node.children, path + "/children", depth + 1, context);
        if (children.Count > 0) result["children"] = children;
        if (node.tooltipable is not null)
            result["linkedTooltip"] = ProjectTooltip(
                node.tooltipable, "node_link", path + "/linkedTooltip", depth + 1, context);
        var subTooltips = ProjectMany(
            node.subTooltips, "node_subtooltip", path + "/subTooltips", depth + 1, context);
        if (subTooltips.Count > 0) result["subTooltips"] = subTooltips;
        return result;
    }

    private static JObject Limited(string path, string role, string code) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["role"] = role,
        ["path"] = path,
    };

    private static JObject Color(UnityEngine.Color color) => new()
    {
        ["r"] = color.r,
        ["g"] = color.g,
        ["b"] = color.b,
        ["a"] = color.a,
    };

    private sealed class ProjectionContext
    {
        internal HashSet<ITooltipable> Tooltips { get; } = new();
        internal int NodeCount { get; set; }
    }
}
#endif
