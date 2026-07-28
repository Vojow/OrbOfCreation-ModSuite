using System;
using System.Linq;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal sealed class AutoConceptToggleButton : IDisposable
{
    internal const string ObjectName = "OrbAutomata.AutoConceptToggle";
    private readonly GameObject _root;
    private readonly Button _button;
    private readonly TextMeshProUGUI? _text;
    private readonly AutoConceptToggleControl _control;
    private AutoCastToggleVisualState? _renderedState;
    private bool _renderedStopped;
    private bool _disposed;

    private AutoConceptToggleButton(
        GameObject root,
        Button button,
        TextMeshProUGUI? text,
        AutoConceptToggleControl control)
    {
        _root = root;
        _button = button;
        _text = text;
        _control = control;
    }

    public bool IsAlive => !_disposed && _root != null;

    public static bool TryCreate(
        AutoConceptToggleControl control,
        out AutoConceptToggleButton? result,
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

        GameObject? root = null;
        try
        {
            var group = StatusControlGroup.GetOrCreate(native);
            RemoveOwnedChild(group);
            root = UnityEngine.Object.Instantiate(native.gameObject, group, false);
            root.name = ObjectName;
            root.SetActive(false);
            StatusControlGroup.RegisterControl(root, StatusControlOrder.AutoConcept);

            var cloned = root.GetComponent(toggleType);
            var text = ReadField(cloned, "textElement") as TextMeshProUGUI;
            var icon = ReadField(cloned, "iconImage") as Image;
            if (icon is not null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }
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

            RemoveNativeViewBindings(root);
            foreach (var tooltip in root.GetComponents<Component>()
                         .Where(component => component.GetType().Name == "HoverTooltip"))
            {
                if (tooltip is Behaviour tooltipBehaviour) tooltipBehaviour.enabled = false;
                UnityEngine.Object.Destroy(tooltip);
            }
            root.AddComponent<HoverTooltip>().Setup(new AutoConceptTooltip(control));

            var button = root.GetComponent<Button>();
            if (button is null)
            {
                UnityEngine.Object.Destroy(root);
                reason = "cloned Auto Buy switch has no Unity Button";
                return false;
            }
            button.onClick.RemoveAllListeners();
            result = new AutoConceptToggleButton(root, button, text, control);
            button.onClick.AddListener(result.Toggle);
            result.Render(force: true);
            root.SetActive(true);
            if (native.transform is RectTransform nativeRect) StatusControlGroup.Reflow(group, nativeRect);
            var rect = root.transform as RectTransform;
            Plugin.Log.LogAutomataInfo(
                $"Auto Concept toggle installed: AnchoredPosition=({rect?.anchoredPosition.x:0.##},{rect?.anchoredPosition.y:0.##}); Native={native.gameObject.name}.");
            return true;
        }
        catch (Exception ex)
        {
            if (root is not null) UnityEngine.Object.Destroy(root);
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    public void Render(bool force = false)
    {
        if (!IsAlive) return;
        var state = _control.State;
        var stopped = AutomataFeatureStatusVisuals.IsEmergencyStopped(_control.Status);
        if (!force && _renderedState == state && _renderedStopped == stopped) return;
        _renderedState = state;
        _renderedStopped = stopped;
        if (_text is null) return;
        _text.text = FormatLabel(state, stopped);
        _text.color = stopped
            ? new Color(1.0f, 0.45f, 0.25f)
            : state == AutoCastToggleVisualState.On
            ? new Color(0.4f, 1.0f, 0.55f)
            : new Color(0.7f, 0.7f, 0.7f);
    }

    internal static string FormatLabel(AutoCastToggleVisualState state, bool stopped = false) => stopped
        ? "CN ON / STOPPED"
        : state switch
    {
        AutoCastToggleVisualState.On => "CN ON",
        _ => "CN OFF",
    };

    private void Toggle()
    {
        _control.Toggle();
        Render(force: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _button.onClick.RemoveListener(Toggle);
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    private static object? ReadField(object? instance, string fieldName)
    {
        for (var type = instance?.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return field.GetValue(instance);
        }
        return null;
    }

    private static void RemoveNativeViewBindings(GameObject root)
    {
        foreach (var component in root.GetComponents<Component>()
                     .Where(component => component.GetType().Name == "ManagedView"))
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
