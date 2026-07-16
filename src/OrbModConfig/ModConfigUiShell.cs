using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class ModConfigUiShell : IDisposable
{
    private const float ExternalRefreshIntervalSeconds = 0.1f;
    internal const string ButtonObjectName = "OrbModConfig.ModsButton";
    internal const string PanelObjectName = "OrbModConfig.Panel";

    private readonly ManualLogSource _log;
    private readonly GameObject _buttonObject;
    private readonly ModConfigPanel _panel;
    private readonly Button _button;
    private readonly Image? _buttonImage;
    private readonly Color _buttonBaseColor;
    private readonly Sprite? _buttonInactiveSprite;
    private readonly Sprite? _buttonActiveSprite;
    private readonly IReadOnlyList<object> _nativeViews;
    private readonly List<(Button Button, UnityAction Listener)> _nativeCloseListeners;
    private bool _disposed;
    private bool _open;
    private object? _previousNativeView;
    private float _externalRefreshSeconds;

    private ModConfigUiShell(
        ManualLogSource log,
        GameObject buttonObject,
        ModConfigPanel panel,
        Button button,
        Image? buttonImage,
        Sprite? buttonInactiveSprite,
        Sprite? buttonActiveSprite,
        IReadOnlyList<object> nativeViews,
        List<(Button Button, UnityAction Listener)> nativeCloseListeners)
    {
        _log = log;
        _buttonObject = buttonObject;
        _panel = panel;
        _button = button;
        _buttonImage = buttonImage;
        _buttonBaseColor = buttonImage?.color ?? Color.white;
        _buttonInactiveSprite = buttonInactiveSprite;
        _buttonActiveSprite = buttonActiveSprite;
        _nativeViews = nativeViews;
        _nativeCloseListeners = nativeCloseListeners;
    }

    public static bool TryCreate(
        ManualLogSource log,
        ConfigCatalogSnapshot catalog,
        out ModConfigUiShell? shell,
        out string reason)
    {
        shell = null;
        reason = string.Empty;

        var buttonType = Type.GetType("UIViewRadioButton, Assembly-CSharp", false);
        if (buttonType is null)
        {
            reason = "UIViewRadioButton type unavailable";
            return false;
        }

        var nativeButtons = Resources.FindObjectsOfTypeAll(buttonType)
            .OfType<Component>()
            .Where(component => NavigationProbe.BuildObjectPath(component)
                .IndexOf("MainContentContainer/TopBar/ViewRadio", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        var cloneSource = nativeButtons
            .Where(component => component.gameObject.activeInHierarchy)
            .OrderBy(component => component.transform.GetSiblingIndex())
            .LastOrDefault()
            ?? nativeButtons
                .OrderBy(component => component.transform.GetSiblingIndex())
                .LastOrDefault();
        if (cloneSource is null)
        {
            reason = "native top-bar button unavailable";
            return false;
        }

        var screenContent = GameObject.Find("Canvas/ContentArea/MainContentContainer/ScreenContent");
        if (screenContent is null)
        {
            reason = "ScreenContent container unavailable";
            return false;
        }

        var buttonParent = cloneSource.transform.parent;
        if (buttonParent is null)
        {
            reason = "native top-bar button has no parent";
            return false;
        }

        RemoveOwnedChild(buttonParent, ButtonObjectName);
        RemoveOwnedChild(screenContent.transform, PanelObjectName);

        GameObject? buttonObject = null;
        ModConfigPanel? panel = null;
        var nativeCloseListeners = new List<(Button Button, UnityAction Listener)>();
        try
        {
            // RenderContent swaps the root image between these two sprites.
            // Cloning an active native tab otherwise permanently copies its
            // highlighted sprite after UIViewRadioButton is removed.
            var inactiveSprite = ReadSpriteField(cloneSource, "baseImage");
            var activeSprite = ReadSpriteField(cloneSource, "activeImage");
            var nativeViews = nativeButtons
                .Select(ReadNativeView)
                .Where(view => view is not null)
                .Cast<object>()
                .Distinct()
                .ToArray();
            buttonObject = UnityEngine.Object.Instantiate(cloneSource.gameObject, buttonParent, false);
            buttonObject.name = ButtonObjectName;
            buttonObject.SetActive(true);
            buttonObject.transform.SetSiblingIndex(buttonParent.childCount - 1);

            var clonedGameComponent = buttonObject.GetComponent(buttonType);
            if (clonedGameComponent is Behaviour clonedBehaviour)
            {
                clonedBehaviour.enabled = false;
            }

            if (clonedGameComponent is not null)
            {
                UnityEngine.Object.Destroy(clonedGameComponent);
            }

            foreach (var tooltip in buttonObject.GetComponents<Component>()
                         .Where(component => component.GetType().Name == "HoverTooltip"))
            {
                if (tooltip is Behaviour tooltipBehaviour)
                {
                    tooltipBehaviour.enabled = false;
                }

                UnityEngine.Object.Destroy(tooltip);
            }

            var button = buttonObject.GetComponent<Button>();
            if (button is null)
            {
                reason = "cloned top-bar object has no Unity Button";
                UnityEngine.Object.Destroy(buttonObject);
                return false;
            }

            button.onClick.RemoveAllListeners();
            var label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label is null)
            {
                reason = "cloned top-bar button has no TextMeshPro label";
                UnityEngine.Object.Destroy(buttonObject);
                return false;
            }

            label.text = "Mods";
            panel = ModConfigPanel.Create(screenContent.transform, label, catalog, log);
            var image = buttonObject.GetComponent<Image>();
            if (image is not null && inactiveSprite is not null)
            {
                image.sprite = inactiveSprite;
            }

            shell = new ModConfigUiShell(
                log,
                buttonObject,
                panel,
                button,
                image,
                inactiveSprite,
                activeSprite,
                nativeViews,
                nativeCloseListeners);
            button.onClick.AddListener(shell.Toggle);

            foreach (var nativeButton in nativeButtons
                         .Select(component => component.GetComponent<Button>())
                         .Where(nativeButton => nativeButton is not null))
            {
                UnityAction listener = shell.CloseFromNativeTab;
                nativeButton!.onClick.AddListener(listener);
                nativeCloseListeners.Add((nativeButton, listener));
            }

            log.LogInfo(
                $"Mod Config UI shell installed. ButtonPath={NavigationProbe.BuildObjectPath(buttonObject)}; " +
                $"PanelPath={NavigationProbe.BuildObjectPath(panel.Root)}; NativeCloseBindings={nativeCloseListeners.Count}; " +
                $"Mods={catalog.Mods.Count}; Settings={catalog.SettingCount}.");
            return true;
        }
        catch (Exception ex)
        {
            foreach (var binding in nativeCloseListeners)
            {
                binding.Button.onClick.RemoveListener(binding.Listener);
            }

            if (buttonObject is not null)
            {
                UnityEngine.Object.Destroy(buttonObject);
            }

            panel?.Dispose();

            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    public void Toggle()
    {
        SetOpen(!_open, restorePreviousNativeView: _open);
    }

    public void EnsureButtonIsLast()
    {
        if (!_disposed && _buttonObject.transform.parent is { } parent &&
            _buttonObject.transform.GetSiblingIndex() != parent.childCount - 1)
        {
            _buttonObject.transform.SetSiblingIndex(parent.childCount - 1);
        }
    }

    public void Tick(float unscaledDeltaTime)
    {
        EnsureButtonIsLast();
        if (_disposed || !_open)
        {
            _externalRefreshSeconds = 0f;
            return;
        }

        _externalRefreshSeconds -= Math.Max(0f, unscaledDeltaTime);
        if (_externalRefreshSeconds > 0f)
        {
            return;
        }

        _externalRefreshSeconds = ExternalRefreshIntervalSeconds;
        _panel.RefreshExternalValues();
    }

    public void Close()
    {
        SetOpen(false, restorePreviousNativeView: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_open)
        {
            SetOpen(false, restorePreviousNativeView: true);
        }

        _disposed = true;
        _button.onClick.RemoveListener(Toggle);
        foreach (var binding in _nativeCloseListeners)
        {
            binding.Button.onClick.RemoveListener(binding.Listener);
        }

        _panel.Dispose();
        UnityEngine.Object.Destroy(_buttonObject);
    }

    private void CloseFromNativeTab()
    {
        SetOpen(false, restorePreviousNativeView: false);
    }

    private void SetOpen(bool open, bool restorePreviousNativeView)
    {
        if (_disposed || _open == open)
        {
            return;
        }

        if (open)
        {
            _previousNativeView = _nativeViews.FirstOrDefault(IsNativeViewActive);
            foreach (var nativeView in _nativeViews)
            {
                SetNativeViewActive(nativeView, false);
            }
        }

        _open = open;
        _panel.SetActive(open);
        if (_buttonImage is not null)
        {
            var sprite = open ? _buttonActiveSprite : _buttonInactiveSprite;
            if (sprite is not null)
            {
                _buttonImage.sprite = sprite;
                _buttonImage.color = _buttonBaseColor;
            }
            else
            {
                _buttonImage.color = open
                    ? Color.Lerp(_buttonBaseColor, Color.white, 0.25f)
                    : _buttonBaseColor;
            }
        }

        if (!open)
        {
            if (restorePreviousNativeView &&
                !_nativeViews.Any(IsNativeViewActive) &&
                _previousNativeView is not null)
            {
                SetNativeViewActive(_previousNativeView, true);
            }

            _previousNativeView = null;
        }

    }

    private static object? ReadNativeView(Component component)
    {
        return FindField(component.GetType(), "item")?.GetValue(component);
    }

    private static bool IsNativeViewActive(object view)
    {
        try
        {
            return view.GetType()
                       .GetMethod("IsActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                       .Invoke(view, null) as bool? == true;
        }
        catch
        {
            return false;
        }
    }

    private static void SetNativeViewActive(object view, bool active)
    {
        view.GetType()
            .GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .Invoke(view, new object[] { active });
    }

    private static Sprite? ReadSpriteField(Component component, string fieldName)
    {
        return component.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetValue(component) as Sprite;
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(fieldName, flags);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static void RemoveOwnedChild(Transform? parent, string objectName)
    {
        if (parent is null)
        {
            return;
        }

        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, objectName, StringComparison.Ordinal))
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }
}
