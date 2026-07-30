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
            for (var index = 0; index < labels.Count; index++)
            {
                if (!TryResolveIcon(labels[index], primitives, out var iconSprite, out var iconReason))
                {
                    reason = $"rail entry '{labels[index]}' has no audited native icon: {iconReason}";
                    ModConfigUiFactory.ClearObjects(owned);
                    return false;
                }
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

    internal static bool TryResolveIcon(
        string label,
        NativeFeatureRailVisualPrimitives primitives,
        out Sprite? icon,
        out string reason)
    {
        if (label.StartsWith("Runtime", StringComparison.Ordinal))
        {
            icon = primitives.RuntimeIcon;
            reason = string.Empty;
            return true;
        }
        switch (label)
        {
            case "General":
                icon = primitives.GeneralIcon;
                reason = string.Empty;
                return true;
            case "Auto Buy":
                return NativeFeatureIconResolver.TryGetAutoBuyIcon(out icon, out reason);
            case "Auto Cast":
                icon = AutoCastToggleButton.FindEquippedSpellIcon(out var source);
                reason = icon is null ? "no equipped spell icon is available" : source;
                return icon is not null;
            case "Auto Concept":
                icon = primitives.ConceptIcon;
                reason = string.Empty;
                return true;
            case "Auto Harvest":
                return NativeFeatureIconResolver.TryGetHarvestIcon(out icon, out reason);
            case "Auto Items":
                // The native Alchemy top-bar sprite is the audited inventory/consumable-adjacent
                // vocabulary already captured with the rail primitives.
                icon = primitives.AdvancedIcon;
                reason = string.Empty;
                return true;
            case "Auto Scribe":
                // Scribe is a native Scholar subview, so its consolidated page reuses the audited
                // Scholar top-bar sprite instead of synthesizing an icon.
                icon = primitives.ConceptIcon;
                reason = string.Empty;
                return true;
            case "Mentor":
                return NativeFeatureIconResolver.TryGetMentorIcon(out icon, out reason);
            case "Advanced":
                icon = primitives.AdvancedIcon;
                reason = string.Empty;
                return true;
            default:
                icon = null;
                reason = "unrecognized consolidated page";
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
