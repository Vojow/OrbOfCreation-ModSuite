using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbMentor;

internal sealed class MentorToggleButton : IDisposable
{
    private readonly GameObject _root;
    private readonly Button _button;
    private readonly TextMeshProUGUI? _text;
    private readonly Image? _icon;
    private readonly MentorConfig _config;
    private readonly MentorRuntime _runtime;

    private MentorToggleButton(GameObject root, Button button, TextMeshProUGUI? text, Image? icon, MentorConfig config, MentorRuntime runtime)
    { _root = root; _button = button; _text = text; _icon = icon; _config = config; _runtime = runtime; }
    public bool IsAlive => _root != null;

    public static bool TryCreate(MentorConfig config, MentorRuntime runtime, out MentorToggleButton? result)
    {
        result = null;
        var toggleType = Type.GetType("UIToggleButton, Assembly-CSharp", false);
        var managerType = Type.GetType("AutoBuyManager, Assembly-CSharp", false);
        if (toggleType is null || managerType is null) return false;
        var autoBuyEnabled = Resources.FindObjectsOfTypeAll(managerType)
            .Select(manager => Read(manager, "autoBuyEnabled"))
            .FirstOrDefault(value => value is not null);
        if (autoBuyEnabled is null) return false;
        var native = Resources.FindObjectsOfTypeAll(toggleType)
            .OfType<Component>()
            .FirstOrDefault(component => ReferenceEquals(Read(component, "isOnVariable"), autoBuyEnabled));
        if (native?.transform.parent is null) return false;
        var root = UnityEngine.Object.Instantiate(native.gameObject, native.transform.parent, false);
        root.name = "OrbMentor.Toggle";
        var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        root.transform.SetSiblingIndex(native.transform.GetSiblingIndex());
        if (root.transform is RectTransform rect && native.transform is RectTransform nativeRect)
        {
            var width = Math.Max(44, Math.Abs(nativeRect.rect.width));
            rect.anchoredPosition = new Vector2(nativeRect.anchoredPosition.x - width - 12, nativeRect.anchoredPosition.y);
        }
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
        if (clonedHoverTooltip is not null)
        {
            clonedHoverTooltip.enabled = false;
            UnityEngine.Object.Destroy(clonedHoverTooltip);
        }
        var hoverTooltip = root.AddComponent<HoverTooltip>();
        hoverTooltip.Setup(new MentorTooltip(config, runtime));
        var button = root.GetComponent<Button>();
        if (button is null) { UnityEngine.Object.Destroy(root); return false; }
        button.onClick.RemoveAllListeners();
        result = new MentorToggleButton(root, button, text, icon, config, runtime);
        button.onClick.AddListener(result.Toggle);
        result.Render();
        return true;
    }

    public void Render()
    {
        var state = _runtime.IsBlocked ? "BLOCKED" : _config.Active ? "ON" : "OFF";
        var color = _runtime.IsBlocked ? new Color(1, .3f, .25f) : _config.Active ? new Color(.4f, 1, .55f) : new Color(.7f, .7f, .7f);
        if (_text is not null)
        {
            _text.text = $"M {state}";
            _text.color = color;
        }
    }
    private void Toggle()
    {
        if (!_runtime.IsBlocked) _config.Mode.Value = _config.Mode.Value == MentorOperationMode.Active ? MentorOperationMode.Disabled : MentorOperationMode.Active;
        _runtime.Cancel(); Render(); Plugin.ShowNotice(_runtime.IsBlocked ? $"Orb Mentor BLOCKED: {_runtime.BlockedReason}" : $"Orb Mentor {_config.Mode.Value}. {_runtime.StatusText()}", _root.transform as RectTransform);
    }
    public void Dispose() { _button.onClick.RemoveListener(Toggle); if (_root != null) UnityEngine.Object.Destroy(_root); }
    private static object? Read(object? instance, string name) => instance?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
}
