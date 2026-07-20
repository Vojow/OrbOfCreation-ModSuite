using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal sealed class AutoBuyToggleButton : IDisposable
{
    private const string ObjectName = "OrbAutomata.AutoBuyToggle";
    private readonly GameObject _root;
    private readonly Button _button;
    private readonly TextMeshProUGUI? _text;
    private readonly AutoBuyToggleControl _control;
    private AutoBuyToggleButton(GameObject root, Button button, TextMeshProUGUI? text, AutoBuyToggleControl control)
    { _root = root; _button = button; _text = text; _control = control; }
    public bool IsAlive => _root != null;

    public static bool TryCreate(AutoBuyToggleControl control, out AutoBuyToggleButton? result)
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
        StatusControlGroup.RegisterControl(root, StatusControlOrder.AutoBuy);
        var cloned = root.GetComponent(toggleType);
        var text = Read(cloned, "textElement") as TextMeshProUGUI;
        var icon = Read(cloned, "iconImage") as Image;
        if (icon is not null) { icon.sprite = null; icon.enabled = false; }
        if (text is not null) { text.gameObject.SetActive(true); text.alignment = TextAlignmentOptions.Center; }
        if (cloned is Behaviour behaviour) behaviour.enabled = false;
        if (cloned is not null) UnityEngine.Object.Destroy(cloned);
        RemoveNativeViewBindings(root);
        foreach (var old in root.GetComponents<Component>().Where(c => c.GetType().Name == "HoverTooltip")) { if (old is Behaviour b) b.enabled = false; UnityEngine.Object.Destroy(old); }
        root.AddComponent<HoverTooltip>().Setup(new AutoBuyTooltip(control));
        var button = root.GetComponent<Button>();
        if (button is null) { UnityEngine.Object.Destroy(root); return false; }
        button.onClick.RemoveAllListeners();
        result = new AutoBuyToggleButton(root, button, text, control);
        button.onClick.AddListener(result.Toggle);
        result.Render();
        root.SetActive(true);
        if (native.transform is RectTransform nativeRect) StatusControlGroup.Reflow(group, nativeRect);
        var rect = root.transform as RectTransform;
        Plugin.Log.LogAutomataInfo($"Auto Buy toggle installed: AnchoredPosition=({rect?.anchoredPosition.x:0.##},{rect?.anchoredPosition.y:0.##}); Native={native.gameObject.name}; NativeActive={native.gameObject.activeInHierarchy}.");
        return true;
    }
    public void Render()
    {
        var state = _control.State;
        var label = state == AutoCastToggleVisualState.On ? "ON" : "OFF";
        var color = state == AutoCastToggleVisualState.On ? new Color(.4f, 1, .55f) : new Color(.7f, .7f, .7f);
        if (_text is not null) { _text.text = $"AB {label}"; _text.color = color; }
    }
    private void Toggle() { _control.Toggle(); Render(); }
    public void Dispose() { _button.onClick.RemoveListener(Toggle); if (_root != null) UnityEngine.Object.Destroy(_root); }
    private static object? Read(object? instance, string name) => instance?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
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
