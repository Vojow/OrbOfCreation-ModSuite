using System;
using UnityEngine;
using UnityEngine.UI;

namespace OrbMentor;

internal static class MentorStatusControlGroup
{
    private const string ObjectName = "OrbModSuite.StatusControls";
    private const float Gap = 12.0f;

    public static Transform GetOrCreate(Component nativeToggle)
    {
        var parent = nativeToggle.transform.parent ?? throw new InvalidOperationException("native toggle parent unavailable");
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (child.name == ObjectName) return child;
        }
        if (nativeToggle.transform is not RectTransform nativeRect) throw new InvalidOperationException("native toggle rect unavailable");
        var width = Width(nativeRect);
        var root = new GameObject(ObjectName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)root.transform;
        rect.anchorMin = nativeRect.anchorMin;
        rect.anchorMax = nativeRect.anchorMax;
        rect.pivot = new Vector2(0.0f, 0.5f);
        rect.sizeDelta = new Vector2(3 * width + 2 * Gap, Math.Max(1.0f, Math.Abs(nativeRect.rect.height)));
        rect.anchoredPosition = new Vector2(nativeRect.anchoredPosition.x + width * 0.5f + Gap, nativeRect.anchoredPosition.y);
        return rect;
    }

    public static void Place(GameObject control, RectTransform nativeRect)
    {
        var width = Width(nativeRect);
        if (control.transform is not RectTransform rect) return;
        rect.anchorMin = new Vector2(0.0f, 0.5f);
        rect.anchorMax = new Vector2(0.0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(width * 0.5f + 2 * (width + Gap), 0.0f);
    }

    private static float Width(RectTransform rect)
    {
        var width = Math.Abs(rect.rect.width);
        if (width < 1.0f) width = Math.Abs(rect.sizeDelta.x);
        return width < 1.0f ? 44.0f : width;
    }
}
