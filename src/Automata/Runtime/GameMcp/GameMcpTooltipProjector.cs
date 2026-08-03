#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Text;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>Renders the screen's tooltip words without serializing Unity's node graph.</summary>
internal static class GameMcpTooltipProjector
{
    private const int MaximumLines = 200;

    internal static JObject Project(
        ITooltipable primary,
        IEnumerable<ITooltipable>? authoredNested,
        IEnumerable<ITooltipable>? inspectedPanels)
    {
        if (primary is null) throw new ArgumentNullException(nameof(primary));
        var lines = new List<string>();
        var visited = new HashSet<ITooltipable>(ReferenceComparer.Instance);
        AppendTooltip(primary, lines, visited);
        AppendTooltips(authoredNested, lines, visited);
        AppendTooltips(inspectedPanels, lines, visited);
        return new JObject { ["text"] = string.Join("\n", lines) };
    }

    private static void AppendTooltips(
        IEnumerable<ITooltipable>? tooltips,
        List<string> lines,
        HashSet<ITooltipable> visited)
    {
        if (tooltips is null) return;
        foreach (var tooltip in tooltips)
            if (tooltip is not null) AppendTooltip(tooltip, lines, visited);
    }

    private static void AppendTooltip(
        ITooltipable tooltip,
        List<string> lines,
        HashSet<ITooltipable> visited)
    {
        if (lines.Count >= MaximumLines || !visited.Add(tooltip)) return;
        AppendLine(lines, tooltip.GetName());
        AppendLine(lines, tooltip.GetDisplayType());
        AppendLine(lines, tooltip.GetDescription());
        var primaryStart = lines.Count;
        AppendNodes(tooltip.GetTooltipNodes(), lines, visited);
        if (tooltip.HasAltTooltips())
        {
            var alternate = new List<string>();
            var alternateVisited = new HashSet<ITooltipable>(visited, ReferenceComparer.Instance);
            AppendNodes(tooltip.GetAltTooltipNodes(), alternate, alternateVisited);
            if (!SameLines(lines, primaryStart, alternate))
            {
                for (var index = 0; index < alternate.Count && lines.Count < MaximumLines; index++)
                    AppendLine(lines, alternate[index]);
                visited.UnionWith(alternateVisited);
            }
        }
    }

    private static void AppendNodes(
        IReadOnlyList<TooltipNode>? nodes,
        List<string> lines,
        HashSet<ITooltipable> visited)
    {
        if (nodes is null) return;
        for (var index = 0; index < nodes.Count && lines.Count < MaximumLines; index++)
        {
            var node = nodes[index];
            if (node is null) continue;
            try
            {
                AppendLine(lines, node.textFn is null ? node.text : node.textFn());
            }
            catch (Exception exception)
            {
                AppendLine(lines, "Tooltip text unavailable: " +
                    exception.GetBaseException().Message);
            }
            if (node.tooltipable is not null)
                AppendTooltip(node.tooltipable, lines, visited);
            AppendTooltips(node.subTooltips, lines, visited);
            AppendNodes(node.children, lines, visited);
        }
    }

    private static void AppendLine(List<string> lines, string? text)
    {
        var plain = GameMcpTextFormatter.Plain(text ?? string.Empty).Trim();
        if (plain.Length == 0) return;
        if (lines.Count == 0 || !string.Equals(lines[^1], plain, StringComparison.Ordinal))
            lines.Add(plain);
    }

    private static bool SameLines(
        IReadOnlyList<string> primary,
        int primaryStart,
        IReadOnlyList<string> alternate)
    {
        if (primary.Count - primaryStart != alternate.Count) return false;
        for (var index = 0; index < alternate.Count; index++)
            if (!string.Equals(
                    primary[primaryStart + index],
                    alternate[index],
                    StringComparison.Ordinal))
                return false;
        return true;
    }

    private sealed class ReferenceComparer : IEqualityComparer<ITooltipable>
    {
        internal static readonly ReferenceComparer Instance = new();
        public bool Equals(ITooltipable? x, ITooltipable? y) => ReferenceEquals(x, y);
        public int GetHashCode(ITooltipable obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
#endif
