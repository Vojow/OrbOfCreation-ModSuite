using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>Clones the audited native subview-radio vocabulary for suite navigation.</summary>
internal static class ModConfigNativeRailFactory
{
    internal const float RailWidth = 0.095f;
    internal const float DetailLeft = 0.125f;

    public static bool TryCapture(
        out NativeFeatureRailVisualPrimitives? primitives,
        out string reason) =>
        NativeViewAdapter.TryCaptureFeatureRailVisuals(out primitives, out reason);

    public static void SkinPanel(Image image, Sprite sprite, Color color)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
    }

    public static bool TryBuild(
        RectTransform parent,
        IReadOnlyList<string> labels,
        int selected,
        ICollection<GameObject> owned,
        TextMeshProUGUI template,
        NativeFeatureRailVisualPrimitives primitives,
        Action<int> onSelected,
        out string reason)
    {
        reason = string.Empty;
        if (labels.Count == 0) return true;
        try
        {
            var resolvedIcons = new Sprite[labels.Count];
            for (var index = 0; index < labels.Count; index++)
            {
                if (!NativeFeatureIconResolver.TryResolve(
                        labels[index], primitives, out var resolved, out var iconReason))
                {
                    reason = $"rail entry '{labels[index]}' has no audited native icon: {iconReason}";
                    ModConfigUiFactory.ClearObjects(owned);
                    return false;
                }
                resolvedIcons[index] = resolved!;
            }
            for (var left = 0; left < resolvedIcons.Length; left++)
            {
                for (var right = left + 1; right < resolvedIcons.Length; right++)
                {
                    if (resolvedIcons[left] != resolvedIcons[right]) continue;
                    reason =
                        $"rail entries '{labels[left]}' and '{labels[right]}' resolve the same sprite";
                    ModConfigUiFactory.ClearObjects(owned);
                    return false;
                }
            }

            for (var index = 0; index < labels.Count; index++)
            {
                var iconSprite = resolvedIcons[index];
                var captured = index;
                var buttonObject = UnityEngine.Object.Instantiate(
                    primitives.FeatureRailButtonPrototype.gameObject,
                    parent,
                    false);
                buttonObject.name = "Rail." + labels[index];
                buttonObject.SetActive(true);
                RemoveNativeWriters(buttonObject, primitives.FeatureRailButtonPrototype.GetType());

                var rect = (RectTransform)buttonObject.transform;
                var top = 1f - index * 0.115f;
                rect.anchorMin = new Vector2(0.08f, top - 0.105f);
                rect.anchorMax = new Vector2(0.92f, top - 0.01f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var button = buttonObject.GetComponent<Button>() ??
                             buttonObject.AddComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onSelected(captured));
                ConfiguredIntentButtonVisualOwnership.Claim(button);

                var frame = buttonObject.GetComponent<Image>() ??
                            buttonObject.AddComponent<Image>();
                frame.sprite = index == selected
                    ? primitives.FeatureRailActiveFrame
                    : primitives.FeatureRailBaseFrame;
                frame.color = Color.white;
                frame.raycastTarget = true;

                var nativeText = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
                if (nativeText is not null) nativeText.gameObject.SetActive(false);

                var iconObject = ModConfigUiFactory.CreateRectObject(
                    "Icon",
                    buttonObject.transform,
                    new Vector2(0.16f, 0.14f),
                    new Vector2(0.84f, 0.86f));
                var icon = iconObject.AddComponent<Image>();
                icon.sprite = iconSprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                buttonObject.AddComponent<HoverTooltip>()
                    .Setup(new ModConfigRailTooltip(labels[index], iconSprite!));
                owned.Add(buttonObject);
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            ModConfigUiFactory.ClearObjects(owned);
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    private static void RemoveNativeWriters(GameObject buttonObject, Type nativeButtonType)
    {
        foreach (var component in buttonObject.GetComponents<Component>()
                     .Where(component => nativeButtonType.IsInstanceOfType(component) ||
                                         component.GetType().Name == "HoverTooltip"))
        {
            if (component is Behaviour behaviour) behaviour.enabled = false;
            UnityEngine.Object.Destroy(component);
        }
    }
}
