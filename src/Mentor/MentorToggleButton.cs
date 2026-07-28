using System;
using System.Linq;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbMentor;

internal sealed class MentorToggleButton : IDisposable
{
    private const string ObjectName = "OrbMentor.Toggle";
    private readonly GameObject _root;
    private readonly Button _button;
    private readonly TextMeshProUGUI? _text;
    private readonly Image? _icon;
    private readonly MentorConfig _config;
    private readonly Func<FeatureStatusSnapshot> _readStatus;
    private int _lastVisualState = -1;

    private MentorToggleButton(
        GameObject root,
        Button button,
        TextMeshProUGUI? text,
        Image? icon,
        MentorConfig config,
        Func<FeatureStatusSnapshot> readStatus)
    { _root = root; _button = button; _text = text; _icon = icon; _config = config; _readStatus = readStatus; }
    public bool IsAlive => _root != null;

    public static bool TryCreate(
        MentorConfig config,
        Func<FeatureStatusSnapshot> readStatus,
        out MentorToggleButton? result)
    {
        result = null;
        var toggleType = Type.GetType("UIToggleButton, Assembly-CSharp", false);
        if (toggleType is null) return false;
        var native = StatusControlGroup.FindNativeToggle(toggleType);
        if (native?.transform.parent is null) return false;
        var group = StatusControlGroup.GetOrCreate(native);
        RemoveOwnedChild(group);
        var root = UnityEngine.Object.Instantiate(native.gameObject, group, false);
        root.name = ObjectName;
        root.SetActive(false);
        StatusControlGroup.RegisterControl(root, StatusControlOrder.Mentor);
        var cloned = root.GetComponent(toggleType);
        var text = Read(cloned, "textElement") as TextMeshProUGUI;
        var icon = Read(cloned, "iconImage") as Image;
        var clonedHoverTooltip = root.GetComponent<HoverTooltip>();
        if (icon is not null)
        {
            icon.sprite = null;
            icon.enabled = false;
        }
        if (text is not null)
        {
            text.gameObject.SetActive(true);
            text.alignment = TextAlignmentOptions.Center;
        }
        if (cloned is Behaviour behaviour) behaviour.enabled = false;
        if (cloned is not null) UnityEngine.Object.Destroy(cloned);
        RemoveNativeViewBindings(root);
        if (clonedHoverTooltip is not null)
        {
            clonedHoverTooltip.enabled = false;
            UnityEngine.Object.Destroy(clonedHoverTooltip);
        }
        var hoverTooltip = root.AddComponent<HoverTooltip>();
        hoverTooltip.Setup(new MentorTooltip(config, readStatus));
        var button = root.GetComponent<Button>();
        if (button is null) { UnityEngine.Object.Destroy(root); return false; }
        button.onClick.RemoveAllListeners();
        result = new MentorToggleButton(root, button, text, icon, config, readStatus);
        button.onClick.AddListener(result.Toggle);
        result.Render();
        root.SetActive(true);
        if (native.transform is RectTransform nativeRect) StatusControlGroup.Reflow(group, nativeRect);
        var installedRect = root.transform as RectTransform;
        Plugin.Log.LogInfo($"Mentor toggle installed: AnchoredPosition=({installedRect?.anchoredPosition.x:0.##},{installedRect?.anchoredPosition.y:0.##}); Native={native.gameObject.name}; NativeActive={native.gameObject.activeInHierarchy}.");
        return true;
    }

    public void Render()
    {
        var status = _readStatus();
        var presentation = FeatureStatusPresenter.Present(status);
        var visualState = (int)presentation.ConfiguredState;
        if (_lastVisualState == visualState) return;
        _lastVisualState = visualState;
        var state = StatusLabel(presentation.ConfiguredState);
        var color = StatusColor(presentation.ConfiguredState);
        if (_text is not null)
        {
            _text.text = $"M {state}";
            _text.color = color;
        }
    }
    private void Toggle()
    {
        _config.Mode.Value = _config.Mode.Value == MentorOperationMode.Active
            ? MentorOperationMode.Disabled
            : MentorOperationMode.Active;
        Render();
        Plugin.ShowNotice(
            $"Orb Mentor. {FeatureStatusPresenter.Format(_readStatus())}.",
            _root.transform as RectTransform);
    }
    public void Dispose() { _button.onClick.RemoveListener(Toggle); if (_root != null) UnityEngine.Object.Destroy(_root); }
    private static object? Read(object? instance, string name) => instance?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    internal static string StatusLabel(FeatureConfiguredPresentationState state) => state switch
    {
        FeatureConfiguredPresentationState.On => "ON",
        _ => "OFF",
    };
    internal static Color StatusColor(FeatureConfiguredPresentationState state) => state switch
    {
        FeatureConfiguredPresentationState.On => new Color(.4f, 1, .55f),
        _ => new Color(.7f, .7f, .7f),
    };
    private static void RemoveNativeViewBindings(GameObject root)
    {
        foreach (var component in root.GetComponents<Component>().Where(component => component.GetType().Name == "ManagedView"))
        {
            if (component is Behaviour behaviour) behaviour.enabled = false;
            UnityEngine.Object.Destroy(component);
        }
    }
    private static void RemoveOwnedChild(Transform group)
    {
        for (var index = group.childCount - 1; index >= 0; index--)
        {
            var child = group.GetChild(index);
            if (child.name != ObjectName) continue;
            child.name = ObjectName + ".Removing";
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }
}
