using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal sealed class AutoCastToggleButton : IDisposable
{
    internal const string ObjectName = "OrbAutomata.AutoCastToggle";
    private static readonly Color OffColor = new Color(0.55f, 0.55f, 0.55f, 1.0f);
    private static readonly Color OnColor = new Color(0.4f, 1.0f, 0.55f, 1.0f);

    private readonly GameObject _root;
    private readonly Button _button;
    private readonly Image? _rootImage;
    private readonly Image? _iconImage;
    private readonly TextMeshProUGUI? _text;
    private readonly Sprite? _offSprite;
    private readonly Sprite? _onSprite;
    private readonly Color _baseRootColor;
    private readonly AutoCastToggleControl _control;
    private readonly ManualLogSource _log;
    private AutoCastToggleVisualState? _renderedState;
    private bool _noticeFailureLogged;
    private bool _disposed;

    private AutoCastToggleButton(
        GameObject root,
        Button button,
        Image? rootImage,
        Image? iconImage,
        TextMeshProUGUI? text,
        Sprite? offSprite,
        Sprite? onSprite,
        AutoCastToggleControl control,
        ManualLogSource log)
    {
        _root = root;
        _button = button;
        _rootImage = rootImage;
        _iconImage = iconImage;
        _text = text;
        _offSprite = offSprite;
        _onSprite = onSprite;
        _baseRootColor = rootImage?.color ?? Color.white;
        _control = control;
        _log = log;
    }

    public bool IsAlive => !_disposed && _root != null;

    public static bool TryCreate(
        AutoCastToggleControl control,
        ManualLogSource log,
        out AutoCastToggleButton? toggle,
        out string reason)
    {
        toggle = null;
        reason = string.Empty;
        var nativeToggleType = Type.GetType("UIToggleButton, Assembly-CSharp", false);
        if (nativeToggleType is null)
        {
            reason = "native UIToggleButton type is unavailable";
            return false;
        }

        var nativeToggle = StatusControlGroup.FindNativeToggle(nativeToggleType);
        if (nativeToggle is null || nativeToggle.transform.parent is null)
        {
            reason = "native RightSidebar/AttributeBar/AutoBuyToggle is unavailable";
            return false;
        }

        GameObject? root = null;
        try
        {
            var group = StatusControlGroup.GetOrCreate(nativeToggle);
            RemoveOwnedChild(group);
            root = UnityEngine.Object.Instantiate(nativeToggle.gameObject, group, false);
            root.name = ObjectName;
            root.SetActive(false);
            StatusControlGroup.RegisterControl(root, StatusControlOrder.AutoCast);

            var clonedNativeToggle = root.GetComponent(nativeToggleType);
            var iconImage = ReadField(clonedNativeToggle, "iconImage") as Image;
            var text = ReadField(clonedNativeToggle, "textElement") as TextMeshProUGUI;
            var offSprite = ReadField(clonedNativeToggle, "offButtonSprite") as Sprite ?? root.GetComponent<Image>()?.sprite;
            var onSprite = ReadField(clonedNativeToggle, "onButtonSprite") as Sprite;
            var spellIcon = FindEquippedSpellIcon(out var iconSource);
            if (iconImage is not null && spellIcon is not null)
            {
                iconImage.sprite = spellIcon;
                iconImage.preserveAspect = true;
            }
            if (text is not null)
            {
                text.gameObject.SetActive(true);
                text.enabled = true;
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
                text.transform.SetAsLastSibling();
            }
            if (clonedNativeToggle is Behaviour behaviour)
            {
                behaviour.enabled = false;
            }

            if (clonedNativeToggle is not null)
            {
                UnityEngine.Object.Destroy(clonedNativeToggle);
            }

            RemoveNativeViewBindings(root);
            foreach (var tooltip in root.GetComponents<Component>()
                         .Where(component => component.GetType().Name == "HoverTooltip"))
            {
                if (tooltip is Behaviour tooltipBehaviour)
                {
                    tooltipBehaviour.enabled = false;
                }

                UnityEngine.Object.Destroy(tooltip);
            }

            var hoverTooltip = root.AddComponent<HoverTooltip>();
            hoverTooltip.Setup(new AutoCastTooltip(control));

            var button = root.GetComponent<Button>();
            if (button is null)
            {
                UnityEngine.Object.Destroy(root);
                reason = "cloned Auto Buy switch has no Unity Button";
                return false;
            }

            button.onClick.RemoveAllListeners();
            toggle = new AutoCastToggleButton(
                root,
                button,
                root.GetComponent<Image>(),
                iconImage,
                text,
                offSprite,
                onSprite,
                control,
                log);
            button.onClick.AddListener(toggle.Toggle);
            toggle.Render(force: true);
            root.SetActive(true);
            if (nativeToggle.transform is RectTransform nativeRect) StatusControlGroup.Reflow(group, nativeRect);
            var rect = root.transform as RectTransform;
            log.LogAutomataInfo(
                $"Auto Cast toggle installed left of the native Auto Buy switch: {BuildPath(root)}; " +
                $"AnchoredPosition={FormatVector(rect?.anchoredPosition)}; Size={FormatVector(rect?.sizeDelta)}; " +
                $"Icon={iconSource}.");
            return true;
        }
        catch (Exception ex)
        {
            if (root is not null)
            {
                UnityEngine.Object.Destroy(root);
            }

            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    public void Render(bool force = false)
    {
        if (!IsAlive)
        {
            return;
        }

        var state = _control.State;
        if (!force && _renderedState == state)
        {
            return;
        }

        var announce = _renderedState.HasValue && _renderedState.Value != state;
        _renderedState = state;
        var active = state == AutoCastToggleVisualState.On;
        var color = state == AutoCastToggleVisualState.On ? OnColor : OffColor;
        if (_rootImage is not null)
        {
            _rootImage.sprite = active && _onSprite is not null ? _onSprite : _offSprite;
            _rootImage.color = _baseRootColor;
        }

        if (_iconImage is not null)
        {
            _iconImage.color = color;
        }

        if (_text is not null)
        {
            _text.text = FormatLabel(state);
            _text.color = color;
        }

        if (announce)
        {
            ShowStateNotice(state);
        }
    }

    internal static string FormatLabel(AutoCastToggleVisualState state) => state switch
    {
        AutoCastToggleVisualState.On => "AC ON",
        _ => "AC OFF",
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _button.onClick.RemoveListener(Toggle);
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }
        catch
        {
        }
    }

    private void Toggle()
    {
        _control.Toggle();
        Render(force: true);
    }

    private void ShowStateNotice(AutoCastToggleVisualState state)
    {
        try
        {
            var tooltipNodeType = Type.GetType("TooltipNode, Assembly-CSharp", false);
            var popupType = Type.GetType("UIPopupText, Assembly-CSharp", false);
            if (tooltipNodeType is null || popupType is null)
            {
                throw new InvalidOperationException("native popup types unavailable");
            }

            var colorMethodName = state switch
            {
                AutoCastToggleVisualState.On => "GetPositiveColor",
                _ => "GetNegativeColor",
            };
            var globalVariablesType = Type.GetType("GlobalVariables, Assembly-CSharp", false);
            var color = globalVariablesType?
                .GetMethod(colorMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .Invoke(null, Array.Empty<object>());
            var constructor = tooltipNodeType.GetConstructor(new[] { typeof(string), typeof(Color) });
            var message = state switch
            {
                AutoCastToggleVisualState.On => "Auto Cast: ON",
                _ => "Auto Cast: OFF",
            };
            var node = color is null ? null : constructor?.Invoke(new[] { (object)message, color });
            if (node is null)
            {
                throw new InvalidOperationException("native TooltipNode constructor unavailable");
            }

            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(tooltipNodeType);
            var nodes = (IList?)Activator.CreateInstance(listType);
            nodes?.Add(node);
            var anchor = FindStatusAnchor() ?? _root.transform as RectTransform;
            var createOn = popupType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return method.Name == "CreateOn" &&
                           parameters.Length == 3 &&
                           parameters[0].ParameterType == listType &&
                           parameters[1].ParameterType == typeof(RectTransform) &&
                           parameters[2].ParameterType == typeof(Vector2);
                });
            if (nodes is null || anchor is null || createOn is null)
            {
                throw new InvalidOperationException("native status popup anchor unavailable");
            }

            createOn.Invoke(null, new object[] { nodes, anchor, new Vector2(0.0f, 32.0f) });
        }
        catch (Exception ex)
        {
            if (!_noticeFailureLogged)
            {
                _noticeFailureLogged = true;
                _log.LogAutomataWarning($"Auto Cast state notice could not use the native status area: {ex.GetBaseException().Message}");
            }
        }
    }

    private static RectTransform? FindStatusAnchor()
    {
        var statusListType = Type.GetType("UICondensedStatusList, Assembly-CSharp", false);
        if (statusListType is null)
        {
            return null;
        }

        return Resources.FindObjectsOfTypeAll(statusListType)
            .OfType<Component>()
            .Where(component => component.gameObject.activeInHierarchy)
            .OrderByDescending(component => BuildPath(component.gameObject)
                .IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(component => component.transform as RectTransform)
            .FirstOrDefault(rect => rect is not null);
    }

    private static object? ReadField(object? instance, string fieldName)
    {
        if (instance is null)
        {
            return null;
        }

        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                return field.GetValue(instance);
            }
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

    private static Sprite? FindEquippedSpellIcon(out string source)
    {
        source = "native Auto Buy fallback";
        var managerType = Type.GetType("SpellManager, Assembly-CSharp", false);
        var manager = managerType?
            .GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetValue(null);
        var activeSpells = ReadField(manager, "activeSpells") as IEnumerable;
        if (activeSpells is null)
        {
            return null;
        }

        foreach (var spell in activeSpells)
        {
            if (spell is null || InvokeNoArgs(spell, "IsEmpty") is true)
            {
                continue;
            }

            if (InvokeNoArgs(spell, "GetIcon") is Sprite icon)
            {
                source = ReflectionUtil.ReadDisplayName(spell) ?? spell.GetType().Name;
                return icon;
            }
        }

        return null;
    }

    private static object? InvokeNoArgs(object instance, string methodName)
    {
        try
        {
            return instance.GetType()
                .GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null)?
                .Invoke(instance, Array.Empty<object>());
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static void RemoveOwnedChild(Transform group)
    {
        for (var index = group.childCount - 1; index >= 0; index--)
        {
            var child = group.GetChild(index);
            if (string.Equals(child.name, ObjectName, StringComparison.Ordinal))
            {
                child.name = ObjectName + ".Removing";
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static string BuildPath(GameObject gameObject)
    {
        var names = new System.Collections.Generic.List<string>();
        for (var current = gameObject.transform; current is not null; current = current.parent)
        {
            names.Add(current.name);
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static string FormatVector(Vector2? vector)
    {
        return vector.HasValue ? $"({vector.Value.x:0.##},{vector.Value.y:0.##})" : "n/a";
    }
}
