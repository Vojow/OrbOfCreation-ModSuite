using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OrbModConfig;

internal static class ModConfigPalette
{
    public static readonly Color Background = new(0.055f, 0.065f, 0.085f, 0.985f);
    public static readonly Color Bar = new(0.09f, 0.105f, 0.135f, 1f);
    public static readonly Color Button = new(0.16f, 0.18f, 0.23f, 1f);
    public static readonly Color ActiveButton = new(0.38f, 0.22f, 0.12f, 1f);
    public static readonly Color Row = new(0.075f, 0.087f, 0.11f, 0.96f);
    public static readonly Color Invalid = new(0.95f, 0.42f, 0.35f, 1f);
}

internal static class ModConfigUiFactory
{
    public static GameObject CreateRectObject(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color? color = null)
    {
        var types = color.HasValue
            ? new[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) }
            : new[] { typeof(RectTransform) };
        var gameObject = new GameObject(name, types);
        gameObject.transform.SetParent(parent, false);
        var rect = (RectTransform)gameObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        if (color.HasValue) gameObject.GetComponent<Image>()!.color = color.Value;
        return gameObject;
    }

    public static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextMeshProUGUI template,
        string value,
        TextAlignmentOptions alignment,
        float sizeScale,
        TextOverflowModes overflowMode = TextOverflowModes.Ellipsis)
    {
        var gameObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        gameObject.transform.SetParent(parent, false);
        var rect = (RectTransform)gameObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = gameObject.GetComponent<TextMeshProUGUI>()!;
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = Math.Max(12f, template.fontSize * sizeScale);
        text.color = template.color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = overflowMode;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    public static Button CreateButton(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextMeshProUGUI template,
        string label,
        UnityAction action,
        bool active = false)
    {
        var gameObject = CreateRectObject(
            name,
            parent,
            anchorMin,
            anchorMax,
            active ? ModConfigPalette.ActiveButton : ModConfigPalette.Button);
        var button = gameObject.AddComponent<Button>();
        button.targetGraphic = gameObject.GetComponent<Image>();
        CreateText(
            "Label",
            gameObject.transform,
            new Vector2(0.03f, 0.05f),
            new Vector2(0.97f, 0.95f),
            template,
            label,
            TextAlignmentOptions.Midline,
            0.68f);
        button.onClick.AddListener(action);
        return button;
    }

    public static void BuildTabs(
        RectTransform parent,
        IReadOnlyList<string> labels,
        int selected,
        ICollection<GameObject> owned,
        TextMeshProUGUI template,
        Action<int> onSelected)
    {
        var count = Math.Max(1, labels.Count);
        for (var index = 0; index < labels.Count; index++)
        {
            var captured = index;
            var left = (float)index / count;
            var right = (float)(index + 1) / count;
            var button = CreateButton(
                "Tab." + labels[index],
                parent,
                new Vector2(left + 0.003f, 0.08f),
                new Vector2(right - 0.003f, 0.92f),
                template,
                labels[index],
                () => onSelected(captured),
                index == selected);
            owned.Add(button.gameObject);
        }
    }

    public static void SetTopAnchoredHeight(RectTransform rect, float topInset, float height)
    {
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -topInset);
        rect.sizeDelta = new Vector2(0f, height);
    }

    public static void ClearObjects(ICollection<GameObject> objects)
    {
        foreach (var gameObject in objects)
        {
            if (gameObject is not null) UnityEngine.Object.Destroy(gameObject);
        }
        objects.Clear();
    }
}
