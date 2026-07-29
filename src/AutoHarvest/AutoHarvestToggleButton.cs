using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal sealed class AutoHarvestToggleButton : IDisposable
{
    internal const string ObjectName = "OrbAutomata.AutoHarvestToggle";
    private readonly GameObject _root;
    private readonly Button _button;
    private readonly ConfiguredIntentIconButtonVisual _visual;
    private readonly AutoHarvestToggleControl _control;
    private ConfiguredIntentIconState? _rendered;

    private AutoHarvestToggleButton(
        GameObject root,
        Button button,
        ConfiguredIntentIconButtonVisual visual,
        AutoHarvestToggleControl control)
    {
        _root = root;
        _button = button;
        _visual = visual;
        _control = control;
    }

    internal bool IsAlive => _root != null;

    internal static bool TryCreate(
        AutoHarvestToggleControl control,
        out AutoHarvestToggleButton? result,
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
        var native = toggleType is null ? null : StatusControlGroup.FindNativeToggle(toggleType);
        if (native?.transform.parent is null)
        {
            reason = "native RightSidebar/AttributeBar/AutoBuyToggle is unavailable";
            return false;
        }
        var group = StatusControlGroup.GetOrCreate(native);
        RemoveOwnedChild(group);
        var root = UnityEngine.Object.Instantiate(native.gameObject, group, false);
        root.name = ObjectName;
        root.SetActive(false);
        StatusControlGroup.RegisterControl(root, StatusControlOrder.AutoHarvest);
        var cloned = root.GetComponent(toggleType!);
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
        root.AddComponent<HoverTooltip>().Setup(new AutoHarvestTooltip(control));
        var button = root.GetComponent<Button>();
        if (button is null)
        {
            UnityEngine.Object.Destroy(root);
            reason = "cloned Auto Buy switch has no Unity Button";
            return false;
        }
        button.onClick.RemoveAllListeners();
        ConfiguredIntentButtonVisualOwnership.Claim(button);
        if (!NativeFeatureIconResolver.TryGetHarvestIcon(out var iconSprite, out var iconReason))
        {
            UnityEngine.Object.Destroy(root);
            reason = "Auto Harvest icon capture failed: " + iconReason;
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
            reason = "Auto Harvest native visual capture failed: " + visualReason;
            return false;
        }
        result = new AutoHarvestToggleButton(root, button, visual!, control);
        button.onClick.AddListener(result.Toggle);
        result.Render(force: true);
        root.SetActive(true);
        if (native.transform is RectTransform nativeRect) StatusControlGroup.Reflow(group, nativeRect);
        reason = string.Empty;
        return true;
    }

    internal void Render(bool force = false)
    {
        var state = ConfiguredIntentIconButtonVisual.FromFeatureStatus(_control.Status);
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
        _control.Toggle();
        Render(force: true);
    }

    private static object? Read(object? instance, string name) =>
        instance?.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);

    private static void RemoveOwnedChild(Transform group)
    {
        for (var index = group.childCount - 1; index >= 0; index--)
        {
            var child = group.GetChild(index);
            if (child.name != ObjectName) continue;
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }
}
