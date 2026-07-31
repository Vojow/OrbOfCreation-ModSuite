using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModConfig;
using UnityEngine;

namespace OrbAutomata;

internal sealed record QuickControlNativePrimitives(
    RectTransform Anchor,
    NativeButtonStateVisualPrimitives StateVisuals);

/// <summary>
/// Resolves the top-left column from the scene-bound audited
/// <c>UIContentArea.canvas</c> reference and exact native HelpButtons structure.
/// </summary>
internal static class QuickControlNativeAdapter
{
    internal const string AnchorPath = "Canvas/HelpButtons";

    internal static bool TryCapture(
        out QuickControlNativePrimitives? primitives,
        out string reason)
    {
        primitives = null;
        var contentAreaType = Type.GetType("UIContentArea, Assembly-CSharp", false);
        if (contentAreaType is null)
        {
            reason = "audited UIContentArea type is unavailable";
            return false;
        }
        try
        {
            var candidates = Resources.FindObjectsOfTypeAll(contentAreaType)
                .OfType<Component>()
                .OrderBy(NativeObjectPath.Build, StringComparer.Ordinal)
                .ToArray();
            if (!TryResolveAnchor(candidates, out var anchor, out var anchorReason))
            {
                reason = "audited top-left HelpButtons anchor unavailable: " + anchorReason;
                return false;
            }
            if (!NativeViewAdapter.TryCaptureButtonStateVisuals(
                    out var stateVisuals,
                    out var stateReason))
            {
                reason = "quick-control state visual unavailable: " + stateReason;
                return false;
            }
            primitives = new QuickControlNativePrimitives(anchor!, stateVisuals!);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            var root = ex.GetBaseException();
            reason =
                "quick-control native capture failed while checking UIContentArea.canvas and " +
                "UIViewRadioButton state frames: " +
                $"{root.GetType().FullName}: {root.Message}";
            return false;
        }
    }

    internal static bool TryResolveAnchor(
        IReadOnlyList<Component> contentAreas,
        out RectTransform? anchor,
        out string reason)
    {
        anchor = null;
        if (contentAreas.Count == 0)
        {
            reason = "no UIContentArea object exists in the loaded scene";
            return false;
        }
        var matches = new List<RectTransform>();
        var failures = new List<string>();
        foreach (var contentArea in contentAreas)
        {
            var canvasField = FindField(contentArea.GetType(), "canvas");
            if (canvasField is null)
            {
                failures.Add(
                    $"{contentArea.GetType().FullName}.canvas field check failed at " +
                    $"'{NativeObjectPath.Build(contentArea)}': expected UnityEngine.RectTransform, " +
                    "actual <missing>");
                continue;
            }
            if (canvasField.FieldType != typeof(RectTransform))
            {
                failures.Add(
                    $"{canvasField.DeclaringType?.FullName}.canvas declared type check failed at " +
                    $"'{NativeObjectPath.Build(contentArea)}': expected UnityEngine.RectTransform, " +
                    $"actual {canvasField.FieldType.FullName}");
                continue;
            }
            var canvasValue = canvasField.GetValue(contentArea);
            if (canvasValue is not RectTransform canvas)
            {
                failures.Add(
                    $"{canvasField.DeclaringType?.FullName}.canvas value type check failed at " +
                    $"'{NativeObjectPath.Build(contentArea)}': expected UnityEngine.RectTransform, " +
                    $"actual {canvasValue?.GetType().FullName ?? "<null>"}");
                continue;
            }
            var helpButtons = FindDirectChild(canvas, "HelpButtons");
            if (helpButtons is not RectTransform helpRect ||
                !string.Equals(NativeObjectPath.Build(helpRect), AnchorPath, StringComparison.Ordinal))
            {
                failures.Add(
                    $"'{NativeObjectPath.Build(contentArea)}' canvas has no exact {AnchorPath} child");
                continue;
            }
            if (FindDirectChild(helpRect, "SettingsButton") is null ||
                FindDirectChild(helpRect, "PlayerStatsButton") is null)
            {
                failures.Add(
                    $"{AnchorPath} lacks direct SettingsButton/PlayerStatsButton children");
                continue;
            }
            matches.Add(helpRect);
        }
        if (matches.Count != 1)
        {
            reason = matches.Count == 0
                ? string.Join("; ", failures)
                : $"expected one exact {AnchorPath} anchor, found {matches.Count}";
            return false;
        }
        anchor = matches[0];
        reason = string.Empty;
        return true;
    }

    private static Transform? FindDirectChild(Transform parent, string name)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        }
        return null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, flags);
            if (field is not null) return field;
        }
        return null;
    }
}
