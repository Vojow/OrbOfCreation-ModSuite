using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// Owns the editable-settings page, including staged values, section navigation,
/// responsive row layout, and per-plugin navigation memory. The panel shell only
/// decides which top-level page is visible.
/// </summary>
internal sealed class ModSettingsPage : IDisposable
{
    internal const string SavedRuntimeMessage = "Saved setting; runtime effect is reported separately.";
    internal const string ConfigurationSavedMessage = "Configuration saved.";
    private readonly ConfigCatalogSnapshot _catalog;
    private readonly ConfigEditSession _session;
    private readonly TextMeshProUGUI _labelTemplate;
    private readonly ManualLogSource _log;
    private readonly RectTransform _sectionTabs;
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;
    private readonly TextMeshProUGUI _statusText;
    private readonly Button _applyButton;
    private readonly Button _revertButton;
    private readonly ModSettingsApplyCoordinator _applyCoordinator;
    private readonly List<GameObject> _sectionTabObjects = new();
    private readonly Dictionary<int, ModSettingsNavigationState> _navigation = new();
    private readonly ModSettingListView _settingList;
    private int _selectedModIndex;
    private int _selectedSectionIndex;
    private bool _visible;
    private bool _disposed;

    public ModSettingsPage(
        ConfigCatalogSnapshot catalog,
        TextMeshProUGUI labelTemplate,
        ManualLogSource log,
        RectTransform sectionTabs,
        RectTransform content,
        ScrollRect scroll,
        TextMeshProUGUI statusText,
        Button applyButton,
        Button revertButton,
        GameplayInvalidationBus invalidationBus)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _session = new ConfigEditSession(catalog);
        _labelTemplate = labelTemplate ?? throw new ArgumentNullException(nameof(labelTemplate));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sectionTabs = sectionTabs ?? throw new ArgumentNullException(nameof(sectionTabs));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _statusText = statusText ?? throw new ArgumentNullException(nameof(statusText));
        _applyButton = applyButton ?? throw new ArgumentNullException(nameof(applyButton));
        _revertButton = revertButton ?? throw new ArgumentNullException(nameof(revertButton));
        _applyCoordinator = new ModSettingsApplyCoordinator(
            invalidationBus ?? throw new ArgumentNullException(nameof(invalidationBus)),
            () => Time.frameCount);
        _settingList = new ModSettingListView(
            _session,
            _content,
            _labelTemplate,
            () => RebuildSettings(),
            RefreshStatus);
        _applyButton.onClick.RemoveAllListeners();
        _applyButton.onClick.AddListener(Apply);
        _revertButton.onClick.RemoveAllListeners();
        _revertButton.onClick.AddListener(Revert);
    }

    public bool IsVisible => _visible;

    public ModConfigNavigationBookmark CaptureBookmark()
    {
        if (!_visible || _catalog.Mods.Count == 0) return ModConfigNavigationBookmark.Runtime;
        CaptureNavigation();
        var mod = _catalog.Mods[_selectedModIndex];
        var sectionName = mod.Sections.Count == 0
            ? string.Empty
            : mod.Sections[_selectedSectionIndex].Name;
        return new ModConfigNavigationBookmark(
            mod.Guid,
            sectionName,
            Math.Max(0f, _content.anchoredPosition.y));
    }

    public void RestoreBookmark(ModConfigNavigationBookmark bookmark)
    {
        if (bookmark.IsRuntime) return;
        var pluginIndex = ModConfigNavigationBookmarkPolicy.ResolveTopPageIndex(_catalog, bookmark) - 1;
        if (pluginIndex < 0) return;
        var mod = _catalog.Mods[pluginIndex];
        _navigation[pluginIndex] = new ModSettingsNavigationState(
            ModConfigNavigationBookmarkPolicy.ResolveSectionIndex(mod, bookmark),
            Math.Max(0f, bookmark.ScrollOffset));
    }

    public void ShowPlugin(int pluginIndex)
    {
        ThrowIfDisposed();
        CaptureNavigation();
        _visible = true;
        _selectedModIndex = Math.Max(0, Math.Min(pluginIndex, _catalog.Mods.Count - 1));
        var mod = _catalog.Mods[_selectedModIndex];
        var state = _navigation.TryGetValue(_selectedModIndex, out var remembered)
            ? remembered.ClampTo(mod.Sections.Count)
            : new ModSettingsNavigationState(0, 0f);
        _selectedSectionIndex = state.SectionIndex;
        RebuildSections(state.ScrollOffset);
    }

    public void Hide()
    {
        if (_disposed || !_visible) return;
        CaptureNavigation();
        _visible = false;
        ModConfigUiFactory.ClearObjects(_sectionTabObjects);
        _settingList.Clear();
    }

    public void RefreshExternalValues()
    {
        if (!_disposed && _session.RefreshExternalValues() && _visible) RebuildSettings();
    }

    public void RefreshResponsiveLayout()
    {
        if (_disposed || !_visible || _catalog.Mods.Count == 0) return;
        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0) return;
        var descriptionWidth = ModSettingsLayout.CalculateDescriptionWidth(_content.rect.width);
        if (ModSettingsLayout.DescriptionWidthChanged(_settingList.MeasuredDescriptionWidth, descriptionWidth))
            RebuildSettings();
    }

    public void RefreshStatus(ConfigEditValue? changed = null)
    {
        if (_disposed || !_visible) return;
        if (_catalog.Mods.Count == 0)
        {
            SetStatus("No loaded plugins expose configuration entries or schema status.", invalid: false);
            SetFooterInteractable(false, false);
            return;
        }

        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0)
        {
            SetStatus("Configuration schema status only; no editable settings loaded.", invalid: false);
            SetFooterInteractable(false, false);
            return;
        }

        var conflict = _session.Values.FirstOrDefault(value =>
            string.Equals(value.Setting.PluginGuid, mod.Guid, StringComparison.Ordinal) &&
            value.HasExternalConflict);
        if (conflict is not null)
            SetStatus($"{conflict.Setting.Key}: changed outside this page; choose Keep mine or Take live.", invalid: true);
        else if (changed is not null && !changed.IsValid)
            SetStatus($"{changed.Setting.Key}: {changed.Error}", invalid: true);
        else
            SetStatus(_session.IsModDirty(mod) ? "Unsaved changes" : "Ready", invalid: false);

        SetFooterInteractable(
            CanApplySelection(mod, _session.IsModDirty(mod), _session.IsModValid(mod)),
            _session.IsModDirty(mod) || _session.ModHasExternalConflicts(mod));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _applyButton.onClick.RemoveListener(Apply);
        _revertButton.onClick.RemoveListener(Revert);
        ModConfigUiFactory.ClearObjects(_sectionTabObjects);
        _settingList.Dispose();
    }

    internal static bool CanApplySelection(
        ModConfigDescriptor selectedMod,
        bool sessionDirty,
        bool sessionValid) =>
        selectedMod.Sections.Count > 0 && sessionDirty && sessionValid;

    private void RebuildSections(float requestedScrollOffset)
    {
        ModConfigUiFactory.ClearObjects(_sectionTabObjects);
        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0)
        {
            _settingList.Clear();
            _content.sizeDelta = new Vector2(0f, 1f);
            RestoreScrollOffset(0f, 1f);
            RefreshStatus();
            return;
        }

        _selectedSectionIndex = Math.Max(0, Math.Min(_selectedSectionIndex, mod.Sections.Count - 1));
        ModConfigUiFactory.BuildTabs(
            _sectionTabs,
            mod.Sections.Select(section => section.Name).ToArray(),
            _selectedSectionIndex,
            _sectionTabObjects,
            _labelTemplate,
            SelectSection);
        RebuildSettings(requestedScrollOffset);
    }

    private void RebuildSettings(float? requestedScrollOffset = null)
    {
        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0)
        {
            _settingList.Clear();
            _content.sizeDelta = new Vector2(0f, 1f);
            RestoreScrollOffset(0f, 1f);
            RefreshStatus();
            return;
        }

        var requestedOffset = requestedScrollOffset ?? Math.Max(0f, _content.anchoredPosition.y);
        var settings = mod.Sections[_selectedSectionIndex].Settings;
        var contentHeight = _settingList.Render(settings);
        RestoreScrollOffset(requestedOffset, contentHeight);
        RefreshStatus();
    }

    private void SelectSection(int index)
    {
        if (index == _selectedSectionIndex) return;
        _selectedSectionIndex = index;
        _navigation[_selectedModIndex] = new ModSettingsNavigationState(index, 0f);
        RebuildSections(0f);
    }

    private void Apply()
    {
        if (!_visible || _catalog.Mods.Count == 0 || _catalog.Mods[_selectedModIndex].Sections.Count == 0)
        {
            RefreshStatus();
            return;
        }

        var selectedMod = _catalog.Mods[_selectedModIndex];
        if (_applyCoordinator.TryApply(_session, selectedMod, out var error, out _))
        {
            RebuildSettings();
            _statusText.text = ConfigurationSavedMessage;
        }
        else
        {
            SetStatus("Apply failed: " + error, invalid: true);
            _log.LogWarning("Mod Config could not apply staged changes: " + error);
        }
    }

    private void Revert()
    {
        if (!_visible || _catalog.Mods.Count == 0 || _catalog.Mods[_selectedModIndex].Sections.Count == 0)
        {
            RefreshStatus();
            return;
        }
        _session.Revert(_catalog.Mods[_selectedModIndex]);
        _statusText.text = "Reverted staged changes.";
        RebuildSettings();
    }

    private void CaptureNavigation()
    {
        if (!_visible || _catalog.Mods.Count == 0) return;
        _navigation[_selectedModIndex] = new ModSettingsNavigationState(
            _selectedSectionIndex,
            Math.Max(0f, _content.anchoredPosition.y));
    }

    private void RestoreScrollOffset(float requestedOffset, float contentHeight)
    {
        var viewportHeight = _scroll.viewport?.rect.height ?? 0f;
        var clampedOffset = ModSettingsLayout.ClampScrollOffset(
            requestedOffset,
            contentHeight,
            viewportHeight);
        _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, clampedOffset);
        _scroll.verticalNormalizedPosition = ModSettingsLayout.CalculateVerticalNormalizedPosition(
            clampedOffset,
            contentHeight,
            viewportHeight);
    }

    private void SetStatus(string text, bool invalid)
    {
        _statusText.color = invalid ? ModConfigPalette.Invalid : _labelTemplate.color;
        _statusText.text = text;
    }

    private void SetFooterInteractable(bool apply, bool revert)
    {
        _applyButton.interactable = apply;
        _revertButton.interactable = revert;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ModSettingsPage));
    }
}
