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

internal sealed class ModConfigNavigationObserver : MonoBehaviour
{
    public Action? Changed { get; set; }
    private void OnTransformChildrenChanged() => Changed?.Invoke();
}

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
    private readonly Type _nativeButtonType;
    private readonly Transform _buttonParent;
    private readonly Transform _panelParent;
    private readonly List<object> _nativeViews;
    private readonly List<(Button Button, UnityAction Listener)> _nativeCloseListeners;
    private ModConfigNavigationObserver? _navigationObserver;
    private bool _disposed;
    private bool _open;
    private bool _repairRequired;
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
        Type nativeButtonType,
        Transform buttonParent,
        Transform panelParent,
        List<object> nativeViews,
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
        _nativeButtonType = nativeButtonType;
        _buttonParent = buttonParent;
        _panelParent = panelParent;
        _nativeViews = nativeViews;
        _nativeCloseListeners = nativeCloseListeners;
    }

    public bool IsAlive => HostsAlive(
        !_disposed && !_repairRequired,
        IsUnityObjectAlive(_buttonObject) && IsUnityObjectAlive(_buttonObject.transform.parent) &&
            ReferenceEquals(_buttonObject.transform.parent, _buttonParent),
        IsUnityObjectAlive(_panel.Root) && IsUnityObjectAlive(_panel.Root.transform.parent) &&
            ReferenceEquals(_panel.Root.transform.parent, _panelParent),
        IsUnityObjectAlive(_buttonParent) && IsUnityObjectAlive(_panelParent));

    public static bool TryCreate(
        ManualLogSource log,
        ConfigCatalogSnapshot catalog,
        out ModConfigUiShell? shell,
        out string reason,
        Action? navigationChanged = null)
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
                .ToList();
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
                buttonType,
                buttonParent,
                screenContent.transform,
                nativeViews,
                nativeCloseListeners);
            button.onClick.AddListener(shell.Toggle);

            foreach (var nativeButton in nativeButtons) shell.BindNativeButton(nativeButton);
            var observer = buttonParent.gameObject.GetComponent<ModConfigNavigationObserver>() ??
                           buttonParent.gameObject.AddComponent<ModConfigNavigationObserver>();
            observer.Changed = navigationChanged ?? shell.RefreshNavigation;
            shell._navigationObserver = observer;

            log.LogInfo(
                $"Mod Config UI shell installed. ButtonPath={NavigationProbe.BuildObjectPath(buttonObject)}; " +
                $"PanelPath={NavigationProbe.BuildObjectPath(panel.Root)}; NativeCloseBindings={nativeCloseListeners.Count}; " +
                $"Mods={catalog.Mods.Count}; Settings={catalog.SettingCount}.");
            return true;
        }
        catch (Exception ex)
        {
            if (shell?._navigationObserver != null)
            {
                shell._navigationObserver.Changed = null;
                UnityEngine.Object.Destroy(shell._navigationObserver);
                shell._navigationObserver = null;
            }

            foreach (var binding in nativeCloseListeners)
            {
                TryRemoveListener(binding.Button, binding.Listener);
            }

            if (buttonObject is not null)
            {
                UnityEngine.Object.Destroy(buttonObject);
            }

            panel?.Dispose();
            shell = null;

            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    public void Toggle()
    {
        TrySetOpen(!_open, restorePreviousNativeView: _open);
    }

    public void RefreshNavigation()
    {
        if (_disposed) return;
        PruneNativeReferences();
        if (!IsAlive || !IsUnityObjectAlive(_buttonParent)) return;
        EnsureButtonIsLast();
        for (var index = 0; index < _buttonParent.childCount; index++)
        {
            var child = _buttonParent.GetChild(index);
            if (ReferenceEquals(child.gameObject, _buttonObject)) continue;
            var nativeButton = child.gameObject.GetComponent(_nativeButtonType);
            if (nativeButton is Component component) BindNativeButton(component);
        }
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed || !IsAlive || !_open)
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
        TrySetOpen(false, restorePreviousNativeView: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        PruneNativeReferences();
        if (_open) TrySetOpen(false, restorePreviousNativeView: true);
        _disposed = true;
        TryRemoveListener(_button, Toggle);
        DetachAll(_nativeCloseListeners, binding => TryRemoveListener(binding.Button, binding.Listener));
        _nativeViews.Clear();

        if (_navigationObserver != null)
        {
            _navigationObserver.Changed = null;
            UnityEngine.Object.Destroy(_navigationObserver);
            _navigationObserver = null;
        }

        try { _panel.Dispose(); } catch { }
        if (_buttonObject != null) UnityEngine.Object.Destroy(_buttonObject);
    }

    private void CloseFromNativeTab()
    {
        PruneNativeReferences();
        EnsureButtonIsLast();
        TrySetOpen(false, restorePreviousNativeView: false);
    }

    private void EnsureButtonIsLast()
    {
        if (!IsAlive || !IsUnityObjectAlive(_buttonParent)) return;
        if (_buttonObject.transform.GetSiblingIndex() != _buttonParent.childCount - 1)
            _buttonObject.transform.SetSiblingIndex(_buttonParent.childCount - 1);
    }

    private void BindNativeButton(Component component)
    {
        if (!IsUnityObjectAlive(component)) return;
        var view = ReadNativeView(component);
        if (IsUnityObjectAlive(view) && !_nativeViews.Any(existing => ReferenceEquals(existing, view)))
            _nativeViews.Add(view!);

        var button = component.GetComponent<Button>();
        if (button is null || _nativeCloseListeners.Any(binding => ReferenceEquals(binding.Button, button))) return;
        UnityAction listener = CloseFromNativeTab;
        button.onClick.AddListener(listener);
        _nativeCloseListeners.Add((button, listener));
    }

    private void SetOpen(bool open, bool restorePreviousNativeView)
    {
        if (_disposed || _open == open)
        {
            return;
        }

        PruneNativeReferences();
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
            if (restorePreviousNativeView && IsUnityObjectAlive(_previousNativeView) &&
                !_nativeViews.Any(IsNativeViewActive) &&
                _previousNativeView is not null)
            {
                SetNativeViewActive(_previousNativeView, true);
            }

            _previousNativeView = null;
        }

    }

    private void TrySetOpen(bool open, bool restorePreviousNativeView)
    {
        try { SetOpen(open, restorePreviousNativeView); }
        catch (Exception ex)
        {
            try { _panel.SetActive(false); } catch { }
            var fallbackNativeView = _nativeViews.FirstOrDefault(IsUnityObjectAlive);
            var recovery = OpenFailureRecovery(
                restoreRequested: open || restorePreviousNativeView,
                previousAlive: IsUnityObjectAlive(_previousNativeView),
                fallbackAlive: IsUnityObjectAlive(fallbackNativeView),
                anyNativeActive: _nativeViews.Any(IsNativeViewActive));
            try
            {
                var restoreTarget = recovery.RestorePrevious ? _previousNativeView :
                    recovery.RestoreFallback ? fallbackNativeView : null;
                if (recovery.RestorePrevious || recovery.RestoreFallback)
                    RestoreUsableNativeView(restoreTarget);
            }
            catch { }
            _open = false;
            _previousNativeView = null;
            _repairRequired = recovery.RepairRequired;
            _log.LogWarning($"Mod Config UI open/close failed; scheduling shell repair: {ex.GetBaseException().Message}");
        }
    }

    internal static bool HostsAlive(bool shellHealthy, bool buttonAlive, bool panelAlive, bool parentsAlive) =>
        shellHealthy && buttonAlive && panelAlive && parentsAlive;

    internal static bool ShouldRestoreNativeView(bool previousAlive, bool anyNativeActive) =>
        previousAlive && !anyNativeActive;

    internal static (bool RestorePrevious, bool RestoreFallback, bool RepairRequired) OpenFailureRecovery(
        bool restoreRequested,
        bool previousAlive,
        bool fallbackAlive,
        bool anyNativeActive) =>
        (restoreRequested && ShouldRestoreNativeView(previousAlive, anyNativeActive),
            restoreRequested && !previousAlive && fallbackAlive && !anyNativeActive,
            true);

    private void PruneNativeReferences()
    {
        PruneDead(_nativeViews, IsUnityObjectAlive);
        PruneDead(
            _nativeCloseListeners,
            binding => IsUnityObjectAlive(binding.Button) && IsUnityObjectAlive(binding.Button.gameObject),
            binding => TryRemoveListener(binding.Button, binding.Listener));
        if (!IsUnityObjectAlive(_previousNativeView)) _previousNativeView = null;
    }

    internal static int PruneDead<T>(List<T> items, Func<T, bool> isAlive, Action<T>? onRemoved = null)
    {
        var removed = 0;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (isAlive(items[index])) continue;
            var item = items[index];
            items.RemoveAt(index);
            removed++;
            try { onRemoved?.Invoke(item); } catch { }
        }
        return removed;
    }

    internal static int DetachAll<T>(List<T> items, Action<T> detach)
    {
        var detached = 0;
        foreach (var item in items)
        {
            try { detach(item); } catch { }
            detached++;
        }
        items.Clear();
        return detached;
    }

    private void RestoreUsableNativeView(object? preferred)
    {
        if (IsUnityObjectAlive(preferred))
        {
            SetNativeViewActive(preferred!, true);
            if (IsNativeViewActive(preferred!)) return;
        }
        foreach (var nativeView in _nativeViews)
        {
            if (!IsUnityObjectAlive(nativeView) || ReferenceEquals(nativeView, preferred)) continue;
            SetNativeViewActive(nativeView, true);
            if (IsNativeViewActive(nativeView)) return;
        }
    }

    private static bool IsUnityObjectAlive(object? value)
    {
        if (value is null) return false;
        return value is not UnityEngine.Object unityObject || unityObject != null;
    }

    private static void TryRemoveListener(Button? button, UnityAction listener)
    {
        try { if (button != null) button.onClick.RemoveListener(listener); } catch { }
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
        try
        {
            if (!IsUnityObjectAlive(view)) return;
            view.GetType()
                .GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .Invoke(view, new object[] { active });
        }
        catch { }
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
