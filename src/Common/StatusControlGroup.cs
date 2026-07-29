using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModding.Common;

public static class StatusControlOrder
{
    public const int EmergencyStop = 50;
    public const int AutoBuy = 100;
    public const int AutoCast = 200;
    public const int AutoConcept = 300;
    public const int AutoHarvest = 400;
    public const int Mentor = 500;
}

/// <summary>
/// Marks a gameplay status control for ordered placement in the compact suite tray.
/// Higher order values appear first; STOP is deliberately last and separated.
/// </summary>
public sealed class StatusControlSlot : MonoBehaviour
{
    public int Order { get; private set; }
    public float ExtraGapBefore { get; private set; }

    public void Configure(int order, float extraGapBefore)
    {
        Order = order;
        ExtraGapBefore = Math.Max(0.0f, extraGapBefore);
    }
}

/// <summary>
/// Owns the compact two-column status-control tray in the audited empty lane at
/// the left edge of the native right-sidebar AttributeBar.
/// </summary>
public static class StatusControlGroup
{
    public const string ObjectName = "OrbModSuite.StatusControls";
    public const int Columns = 2;
    public const float ControlSize = 34.0f;
    public const float ColumnGap = 4.0f;
    public const float RowGap = 4.0f;
    public const float TrayInset = 4.0f;
    public const float StopSeparation = 6.0f;

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
        var root = new GameObject(ObjectName, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = Vector2.zero;
        ConfigureGroupRect(rect, nativeRect);
        return rect;
    }

    public static void RegisterControl(GameObject control, int order, float extraGapBefore = 0.0f)
    {
        var slot = control.GetComponent<StatusControlSlot>() ?? control.AddComponent<StatusControlSlot>();
        slot.Configure(order, extraGapBefore);
    }

    public static void Reflow(Transform group, RectTransform nativeRect)
    {
        var controls = new List<StatusControlSlot>(group.childCount);
        for (var index = 0; index < group.childCount; index++)
        {
            var child = group.GetChild(index);
            var slot = child.GetComponent<StatusControlSlot>();
            if (slot is not null && child is RectTransform && child.gameObject.activeSelf) controls.Add(slot);
        }

        controls.Sort(static (left, right) =>
        {
            var order = right.Order.CompareTo(left.Order);
            return order != 0 ? order : string.CompareOrdinal(left.gameObject.name, right.gameObject.name);
        });

        var extraWidth = controls
            .Select((control, index) => index % Columns == 0 ? 0.0f : control.ExtraGapBefore)
            .DefaultIfEmpty(0.0f)
            .Max();
        var groupSize = CalculateGroupSize(controls.Count, extraWidth);
        for (var visibleIndex = 0; visibleIndex < controls.Count; visibleIndex++)
        {
            var slot = controls[visibleIndex];
            var rect = (RectTransform)slot.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ControlSize, ControlSize);
            rect.anchoredPosition = CalculateSlotCenter(
                visibleIndex,
                controls.Count,
                visibleIndex % Columns == 0 ? 0.0f : slot.ExtraGapBefore);
        }

        if (group is RectTransform groupRect)
        {
            groupRect.sizeDelta = groupSize;
            ConfigureGroupRect(groupRect, nativeRect);
        }
    }

    public static Vector2 CalculateGroupPosition(Vector2 nativePosition, Vector2 nativePivot, float nativeWidth) =>
        new(nativePosition.x - nativePivot.x * nativeWidth + TrayInset, TrayInset);

    public static Vector2 CalculateGroupSize(int count, float extraWidth = 0.0f)
    {
        if (count <= 0) return Vector2.zero;
        var columns = Math.Min(Columns, count);
        var rows = (count + Columns - 1) / Columns;
        return new Vector2(
            columns * ControlSize + (columns - 1) * ColumnGap + Math.Max(0.0f, extraWidth),
            rows * ControlSize + (rows - 1) * RowGap);
    }

    public static Vector2 CalculateSlotCenter(int visibleIndex, int count, float extraGapBefore = 0.0f)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (visibleIndex < 0 || visibleIndex >= count) throw new ArgumentOutOfRangeException(nameof(visibleIndex));
        var groupSize = CalculateGroupSize(count, visibleIndex % Columns == 0 ? 0.0f : extraGapBefore);
        var column = visibleIndex % Columns;
        var row = visibleIndex / Columns;
        return new Vector2(
            ControlSize * 0.5f + column * (ControlSize + ColumnGap) +
            (column == 0 ? 0.0f : Math.Max(0.0f, extraGapBefore)),
            groupSize.y - ControlSize * 0.5f - row * (ControlSize + RowGap));
    }

    private static void ConfigureGroupRect(RectTransform groupRect, RectTransform nativeRect)
    {
        var width = GetWidth(nativeRect);
        groupRect.anchorMin = new Vector2(nativeRect.anchorMin.x, 0.0f);
        groupRect.anchorMax = groupRect.anchorMin;
        groupRect.pivot = Vector2.zero;
        groupRect.anchoredPosition = CalculateGroupPosition(
            nativeRect.anchoredPosition,
            nativeRect.pivot,
            width);
    }

    private static float GetWidth(RectTransform rect)
    {
        var width = Math.Abs(rect.rect.width);
        if (width < 1.0f) width = Math.Abs(rect.sizeDelta.x);
        return width < 1.0f ? 44.0f : width;
    }
}
