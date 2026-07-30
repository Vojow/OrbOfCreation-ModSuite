using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal delegate bool TryResolveFeatureIcon(out Sprite? icon, out string reason);

/// <summary>
/// Installs one ordinary configured-intent control in the shared native quick-control tray.
/// Feature policy stays in the supplied control and tooltip; this type owns only Unity cloning,
/// visual state, click dispatch, and lifecycle cleanup.
/// </summary>
internal sealed class AutomataFeatureToggleButton : IDisposable
{
    private readonly GameObject _root;
    private readonly Button _button;
    private readonly ConfiguredIntentIconButtonVisual _visual;
    private readonly Action _toggle;
    private readonly Func<FeatureStatusSnapshot> _readStatus;
    private ConfiguredIntentIconState? _rendered;

    private AutomataFeatureToggleButton(
        GameObject root,
        Button button,
        ConfiguredIntentIconButtonVisual visual,
        Action toggle,
        Func<FeatureStatusSnapshot> readStatus)
    {
        _root = root;
        _button = button;
        _visual = visual;
        _toggle = toggle;
        _readStatus = readStatus;
    }

    internal bool IsAlive => _root != null;

    internal static bool TryCreate(
        string objectName,
        int order,
        string displayName,
        ITooltipable tooltip,
        TryResolveFeatureIcon resolveIcon,
        Action toggle,
        Func<FeatureStatusSnapshot> readStatus,
        out AutomataFeatureToggleButton? result,
        out string reason)
    {
        result = null;
        reason = string.Empty;
        var toggleType = Type.GetType("UIToggleButton, Assembly-CSharp", false);
        if (toggleType is null)
        {
            reason = "native UIToggleButton type is unavailable";
            return false;
        }
        var native = StatusControlGroup.FindNativeToggle(toggleType);
        if (native?.transform.parent is null)
        {
            reason = "native RightSidebar/AttributeBar/AutoBuyToggle is unavailable";
            return false;
        }

        var group = StatusControlGroup.GetOrCreate(native);
        RemoveOwnedChild(group, objectName);
        var root = UnityEngine.Object.Instantiate(native.gameObject, group, false);
        root.name = objectName;
        root.SetActive(false);
        StatusControlGroup.RegisterControl(root, order);
        var cloned = root.GetComponent(toggleType);
        var text = Read(cloned, "textElement") as TextMeshProUGUI;
        var icon = Read(cloned, "iconImage") as Image;
        if (text is not null)
        {
            text.gameObject.SetActive(true);
            text.alignment = TextAlignmentOptions.Center;
        }
        if (cloned is Behaviour behaviour) behaviour.enabled = false;
        if (cloned is not null) UnityEngine.Object.Destroy(cloned);
        foreach (var component in root.GetComponents<Component>().Where(component =>
                     component.GetType().Name is "ManagedView" or "HoverTooltip"))
        {
            if (component is Behaviour oldBehaviour) oldBehaviour.enabled = false;
            UnityEngine.Object.Destroy(component);
        }
        root.AddComponent<HoverTooltip>().Setup(tooltip);

        var button = root.GetComponent<Button>();
        if (button is null)
        {
            UnityEngine.Object.Destroy(root);
            reason = "cloned Auto Buy switch has no Unity Button";
            return false;
        }
        button.onClick.RemoveAllListeners();
        ConfiguredIntentButtonVisualOwnership.Claim(button);
        if (!resolveIcon(out var iconSprite, out var iconReason))
        {
            UnityEngine.Object.Destroy(root);
            reason = displayName + " icon capture failed: " + iconReason;
            return false;
        }
        if (!ConfiguredIntentIconButtonVisual.TryCreateFeature(
                root,
                button,
                icon,
                text,
                iconSprite,
                out var visual,
                out var visualReason))
        {
            UnityEngine.Object.Destroy(root);
            reason = displayName + " native visual capture failed: " + visualReason;
            return false;
        }

        result = new AutomataFeatureToggleButton(
            root,
            button,
            visual!,
            toggle,
            readStatus);
        button.onClick.AddListener(result.Toggle);
        result.Render(force: true);
        root.SetActive(true);
        if (native.transform is RectTransform nativeRect)
            StatusControlGroup.Reflow(group, nativeRect);
        reason = string.Empty;
        return true;
    }

    internal void Render(bool force = false)
    {
        var state = ConfiguredIntentIconButtonVisual.FromFeatureStatus(_readStatus());
        if (!force && _rendered == state) return;
        _rendered = state;
        _visual.Render(state, force);
    }

    public void Dispose()
    {
        _button.onClick.RemoveListener(Toggle);
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    private void Toggle()
    {
        _toggle();
        Render(force: true);
    }

    private static object? Read(object? instance, string name) =>
        instance?.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);

    private static void RemoveOwnedChild(Transform group, string objectName)
    {
        for (var index = group.childCount - 1; index >= 0; index--)
        {
            var child = group.GetChild(index);
            if (child.name != objectName) continue;
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }
}
