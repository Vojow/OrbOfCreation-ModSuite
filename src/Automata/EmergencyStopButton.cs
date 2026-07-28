using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal sealed class EmergencyStopButton : IDisposable
{
    private const string ObjectName = "OrbAutomata.EmergencyStop";
    private readonly GameObject _root;
    private readonly Button _button;
    private readonly TextMeshProUGUI? _text;
    private readonly EmergencyStopControl _control;
    private string _renderedLabel = string.Empty;

    private EmergencyStopButton(GameObject root, Button button, TextMeshProUGUI? text, EmergencyStopControl control)
    {
        _root = root;
        _button = button;
        _text = text;
        _control = control;
    }

    public bool IsAlive => _root != null;

    public static bool TryCreate(EmergencyStopControl control, out EmergencyStopButton? result)
    {
        result = null;
        var toggleType = Type.GetType("UIToggleButton, Assembly-CSharp", false);
        var native = toggleType is null ? null : StatusControlGroup.FindNativeToggle(toggleType);
        if (native?.transform.parent is null) return false;
        var group = StatusControlGroup.GetOrCreate(native);
        RemoveOwnedChild(group);
        var root = UnityEngine.Object.Instantiate(native.gameObject, group, false);
        root.name = ObjectName;
        root.SetActive(false);
        StatusControlGroup.RegisterControl(root, StatusControlOrder.EmergencyStop);
        var cloned = root.GetComponent(toggleType!);
        var text = Read(cloned, "textElement") as TextMeshProUGUI;
        var icon = Read(cloned, "iconImage") as Image;
        if (icon is not null) { icon.sprite = null; icon.enabled = false; }
        if (text is not null)
        {
            text.gameObject.SetActive(true);
            text.enabled = true;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.transform.SetAsLastSibling();
        }
        if (cloned is Behaviour behaviour) behaviour.enabled = false;
        if (cloned is not null) UnityEngine.Object.Destroy(cloned);
        foreach (var component in root.GetComponents<Component>().Where(component =>
                     component.GetType().Name is "ManagedView" or "HoverTooltip"))
        {
            if (component is Behaviour oldBehaviour) oldBehaviour.enabled = false;
            UnityEngine.Object.Destroy(component);
        }
        root.AddComponent<HoverTooltip>().Setup(new EmergencyStopTooltip(control));
        var button = root.GetComponent<Button>();
        if (button is null) { UnityEngine.Object.Destroy(root); return false; }
        button.onClick.RemoveAllListeners();
        ConfiguredIntentButtonVisualOwnership.Claim(button);
        result = new EmergencyStopButton(root, button, text, control);
        button.onClick.AddListener(result.Activate);
        result.Render(force: true);
        root.SetActive(true);
        if (native.transform is RectTransform nativeRect) StatusControlGroup.Reflow(group, nativeRect);
        return true;
    }

    public void Render(bool force = false)
    {
        _control.Synchronize();
        var label = _control.Label;
        if (!force && label == _renderedLabel) return;
        _renderedLabel = label;
        if (_text is null) return;
        _text.text = label;
        _text.color = _control.IsStopped ? new Color(1, .45f, .25f) : new Color(1, .75f, .35f);
    }

    private void Activate() { _control.Activate(); Render(force: true); }
    public void Dispose() { _button.onClick.RemoveListener(Activate); if (_root != null) UnityEngine.Object.Destroy(_root); }
    private static object? Read(object? instance, string name) => instance?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
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
