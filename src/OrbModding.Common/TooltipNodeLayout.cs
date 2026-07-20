using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbModding.Common;

internal static class TooltipNodeLayout
{
    public static void AddFeatureStatus(
        List<TooltipNode> nodes,
        in FeatureStatusSnapshot status,
        Color color,
        int lineWidth)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        AddLines(nodes, FeatureStatusPresenter.FormatLines(status, lineWidth), color);
    }

    public static void AddCompactFeatureStatus(
        List<TooltipNode> nodes,
        string label,
        in FeatureStatusSnapshot status,
        int lineWidth)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        AddLines(nodes, FeatureStatusPresenter.FormatCompactLines(label, status, lineWidth));
    }

    public static void AddLines(
        List<TooltipNode> nodes,
        IReadOnlyList<string> lines,
        Color? color = null,
        string firstLinePrefix = "")
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (lines is null) throw new ArgumentNullException(nameof(lines));
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
                throw new ArgumentException("Tooltip lines must contain one physical line per native node.", nameof(lines));
            var text = index == 0 ? firstLinePrefix + line : line;
            nodes.Add(color.HasValue ? new TooltipNode(text, color.Value) : new TooltipNode(text));
        }
    }
}
