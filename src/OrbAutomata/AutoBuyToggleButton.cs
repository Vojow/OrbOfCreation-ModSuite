using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal sealed class AutoBuyToggleButton : IDisposable
{
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
        var managerType = Type.GetType("AutoBuyManager, Assembly-CSharp", false);
        if (toggleType is null || managerType is null) return false;
        var autoBuyEnabled = Resources.FindObjectsOfTypeAll(managerType)
            .OfType<Component>()
            .Where(manager => manager.gameObject.activeInHierarchy)
            .Select(manager => Read(manager, "autoBuyEnabled"))
            .FirstOrDefault(value => value is not null);
        if (autoBuyEnabled is null) return false;
        var native = Resources.FindObjectsOfTypeAll(toggleType).OfType<Component>().FirstOrDefault(c => IsNativeQueueToggle(c) && ReferenceEquals(Read(c, "isOnVariable"), autoBuyEnabled));
        if (native?.transform.parent is null) return false;
        var group = StatusControlGroup.GetOrCreate(native);
        var root = UnityEngine.Object.Instantiate(native.gameObject, group, false);
        root.name = "OrbAutomata.AutoBuyToggle";
        if (native.transform is RectTransform nativeRect) StatusControlGroup.Reflow(group, nativeRect);
        var cloned = root.GetComponent(toggleType);
        var text = Read(cloned, "textElement") as TextMeshProUGUI;
        var icon = Read(cloned, "iconImage") as Image;
        if (icon is not null) { icon.sprite = null; icon.enabled = false; }
        if (text is not null) { text.gameObject.SetActive(true); text.alignment = TextAlignmentOptions.Center; }
        if (cloned is Behaviour behaviour) behaviour.enabled = false;
        if (cloned is not null) UnityEngine.Object.Destroy(cloned);
        foreach (var old in root.GetComponents<Component>().Where(c => c.GetType().Name == "HoverTooltip")) { if (old is Behaviour b) b.enabled = false; UnityEngine.Object.Destroy(old); }
        root.AddComponent<HoverTooltip>().Setup(new AutoBuyTooltip(control));
        var button = root.GetComponent<Button>();
        if (button is null) { UnityEngine.Object.Destroy(root); return false; }
        button.onClick.RemoveAllListeners();
        result = new AutoBuyToggleButton(root, button, text, control);
        button.onClick.AddListener(result.Toggle); result.Render();
        var rect = root.transform as RectTransform;
        Plugin.Log.LogInfo($"Auto Buy toggle installed: AnchoredPosition=({rect?.anchoredPosition.x:0.##},{rect?.anchoredPosition.y:0.##}); Native={native.gameObject.name}; NativeActive={native.gameObject.activeInHierarchy}.");
        return true;
    }
    public void Render()
    {
        var state = _control.State;
        var label = state == AutoCastToggleVisualState.On ? "ON" : state == AutoCastToggleVisualState.Blocked ? "!" : "OFF";
        var color = state == AutoCastToggleVisualState.On ? new Color(.4f, 1, .55f) : state == AutoCastToggleVisualState.Blocked ? new Color(1, .35f, .3f) : new Color(.7f, .7f, .7f);
        if (_text is not null) { _text.text = $"AB {label}"; _text.color = color; }
    }
    private void Toggle() { _control.Toggle(); Render(); }
    public void Dispose() { _button.onClick.RemoveListener(Toggle); if (_root != null) UnityEngine.Object.Destroy(_root); }
    private static object? Read(object? instance, string name) => instance?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    private static bool IsNativeQueueToggle(Component component) =>
        component.gameObject.activeInHierarchy &&
        !component.gameObject.name.StartsWith("OrbAutomata.", StringComparison.Ordinal) &&
        component.gameObject.name != "OrbMentor.Toggle" &&
        component.transform.parent?.name != StatusControlGroup.ObjectName;
}
