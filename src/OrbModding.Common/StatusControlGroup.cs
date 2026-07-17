using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModding.Common;

public static class StatusControlOrder
{
    public const int AutoBuy = 100;
    public const int AutoCast = 200;
    public const int AutoConcept = 300;
    public const int Mentor = 400;
}

/// <summary>
/// Marks a gameplay status control for ordered placement beside the native Auto Buy toggle.
/// Lower order values are placed closer to the native toggle.
/// </summary>
public sealed class StatusControlSlot : MonoBehaviour
{
    public int Order { get; private set; }

    public void Configure(int order) => Order = order;
}

/// <summary>
/// Owns the shared, variable-length status-control strip used by suite plugins.
/// </summary>
public static class StatusControlGroup
{
    public const string ObjectName = "OrbModSuite.StatusControls";
    public const float Gap = 12.0f;

    public static Component? FindNativeToggle(Type toggleType) =>
        Resources.FindObjectsOfTypeAll(toggleType)
            .OfType<Component>()
            .FirstOrDefault(IsNativeAnchor);

    public static bool IsNativeAnchor(Component component) =>
        component.gameObject.name == "AutoBuyToggle" &&
        component.transform.parent?.name == "AttributeBar" &&
        component.transform.parent.parent?.name == "RightSidebar";

    public static Transform GetOrCreate(Component nativeToggle)
    {
        var parent = nativeToggle.transform.parent ?? throw new InvalidOperationException("native toggle parent unavailable");
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (child.name != ObjectName) continue;
            if (nativeToggle.transform is RectTransform existingNativeRect && child is RectTransform existingGroupRect)
                ConfigureGroupRect(existingGroupRect, existingNativeRect);
            return child;
        }

        if (nativeToggle.transform is not RectTransform nativeRect) throw new InvalidOperationException("native toggle rect unavailable");
        var height = Math.Max(1.0f, Math.Abs(nativeRect.rect.height));
        var root = new GameObject(ObjectName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(0.0f, height);
        ConfigureGroupRect(rect, nativeRect);
        return rect;
    }

    public static void RegisterControl(GameObject control, int order)
    {
        var slot = control.GetComponent<StatusControlSlot>() ?? control.AddComponent<StatusControlSlot>();
        slot.Configure(order);
    }

    public static void Reflow(Transform group, RectTransform nativeRect)
    {
        var width = GetWidth(nativeRect);
        var controls = new List<StatusControlSlot>(group.childCount);
        for (var index = 0; index < group.childCount; index++)
        {
            var child = group.GetChild(index);
            var slot = child.GetComponent<StatusControlSlot>();
            if (slot is not null && child is RectTransform && child.gameObject.activeSelf) controls.Add(slot);
        }

        controls.Sort(static (left, right) =>
        {
            var order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : string.CompareOrdinal(left.gameObject.name, right.gameObject.name);
        });

        var groupWidth = CalculateGroupWidth(width, controls.Count);
        for (var slotFromNative = 0; slotFromNative < controls.Count; slotFromNative++)
        {
            var rect = (RectTransform)controls[slotFromNative].transform;
            rect.anchorMin = new Vector2(0.0f, 0.5f);
            rect.anchorMax = new Vector2(0.0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(
                CalculateSlotCenterX(width, controls.Count, slotFromNative),
                0.0f);
        }

        if (group is RectTransform groupRect)
        {
            groupRect.sizeDelta = new Vector2(groupWidth, groupRect.sizeDelta.y);
            ConfigureGroupRect(groupRect, nativeRect);
        }
    }

    public static Vector2 CalculateGroupPosition(Vector2 nativePosition, Vector2 nativePivot, float width, float height) =>
        new(nativePosition.x - nativePivot.x * width - Gap,
            nativePosition.y + (0.5f - nativePivot.y) * height);

    public static float CalculateGroupWidth(float width, int count) =>
        count <= 0 ? 0.0f : count * width + (count - 1) * Gap;

    public static float CalculateSlotCenterX(float width, int count, int slotFromNative)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (slotFromNative < 0 || slotFromNative >= count) throw new ArgumentOutOfRangeException(nameof(slotFromNative));
        return CalculateGroupWidth(width, count) - width * 0.5f - slotFromNative * (width + Gap);
    }

    private static void ConfigureGroupRect(RectTransform groupRect, RectTransform nativeRect)
    {
        var width = GetWidth(nativeRect);
        var height = Math.Abs(nativeRect.rect.height);
        groupRect.anchorMin = nativeRect.anchorMin;
        groupRect.anchorMax = nativeRect.anchorMax;
        groupRect.pivot = new Vector2(1.0f, 0.5f);
        groupRect.anchoredPosition = CalculateGroupPosition(
            nativeRect.anchoredPosition,
            nativeRect.pivot,
            width,
            height);
    }

    private static float GetWidth(RectTransform rect)
    {
        var width = Math.Abs(rect.rect.width);
        if (width < 1.0f) width = Math.Abs(rect.sizeDelta.x);
        return width < 1.0f ? 44.0f : width;
    }
}
