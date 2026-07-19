using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class ModConfigPanel : IDisposable
{
    private const float MinimumSettingRowHeight = 86f;
    private const float SettingRowGap = 7f;
    private const float SettingRowTopInset = 4f;
    private const float SettingTitleTopInset = 5f;
    private const float SettingTitleHeight = 28f;
    private const float SettingDescriptionTopInset = 34f;
    private const float SettingDescriptionBottomInset = 6f;
    private const float SettingDescriptionHeightPadding = 2f;
    private const float MinimumMeasurableContentWidth = 320f;
    private const float FallbackDescriptionWidth = 600f;
    private const float DescriptionWidthChangeTolerance = 0.5f;
    internal const string SavedRuntimeMessage = "Saved setting; runtime effect is reported separately.";
    internal const string ConfigurationSavedMessage = "Configuration saved.";
    private static readonly Color BackgroundColor = new Color(0.055f, 0.065f, 0.085f, 0.985f);
    private static readonly Color BarColor = new Color(0.09f, 0.105f, 0.135f, 1f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.18f, 0.23f, 1f);
    private static readonly Color ActiveButtonColor = new Color(0.38f, 0.22f, 0.12f, 1f);
    private static readonly Color RowColor = new Color(0.075f, 0.087f, 0.11f, 0.96f);
    private static readonly Color InvalidColor = new Color(0.95f, 0.42f, 0.35f, 1f);

    private readonly ConfigCatalogSnapshot _catalog;
    private readonly ConfigEditSession _session;
    private readonly TextMeshProUGUI _labelTemplate;
    private readonly ManualLogSource _log;
    private readonly RectTransform _modTabs;
    private readonly RectTransform _sectionTabs;
    private readonly RectTransform _settingsContent;
    private readonly ScrollRect _settingsScroll;
    private readonly IFeatureStatusSource _featureStatuses;
    private readonly TextMeshProUGUI _runtimeStatusText;
    private readonly TextMeshProUGUI _statusText;
    private readonly Button _applyButton;
    private readonly Button _revertButton;
    private readonly List<GameObject> _modTabObjects = new List<GameObject>();
    private readonly List<GameObject> _sectionTabObjects = new List<GameObject>();
    private readonly List<GameObject> _settingObjects = new List<GameObject>();
    private int _selectedModIndex;
    private int _selectedSectionIndex;
    private float _measuredDescriptionWidth;
    private bool _disposed;
    private bool _runtimeStatusDirty = true;

    private ModConfigPanel(
        GameObject root,
        ConfigCatalogSnapshot catalog,
        TextMeshProUGUI labelTemplate,
        ManualLogSource log,
        RectTransform modTabs,
        RectTransform sectionTabs,
        RectTransform settingsContent,
        ScrollRect settingsScroll,
        IFeatureStatusSource featureStatuses,
        TextMeshProUGUI runtimeStatusText,
        TextMeshProUGUI statusText,
        Button applyButton,
        Button revertButton)
    {
        Root = root;
        _catalog = catalog;
        _session = new ConfigEditSession(catalog);
        _labelTemplate = labelTemplate;
        _log = log;
        _modTabs = modTabs;
        _sectionTabs = sectionTabs;
        _settingsContent = settingsContent;
        _settingsScroll = settingsScroll;
        _featureStatuses = featureStatuses;
        _runtimeStatusText = runtimeStatusText;
        _statusText = statusText;
        _applyButton = applyButton;
        _revertButton = revertButton;
        _featureStatuses.Transitioned += OnRuntimeStatusTransitioned;
        RebuildAll(resetSettingsScroll: true);
        SetActive(false);
    }

    public GameObject Root { get; }

    public static ModConfigPanel Create(
        Transform parent,
        TextMeshProUGUI labelTemplate,
        ConfigCatalogSnapshot catalog,
        ManualLogSource log,
        IFeatureStatusSource featureStatuses)
    {
        var root = CreateRectObject(ModConfigUiShell.PanelObjectName, parent, Vector2.zero, Vector2.one, BackgroundColor);

        var header = CreateRectObject("ModTabs", root.transform, new Vector2(0.02f, 0.875f), new Vector2(0.98f, 0.975f), BarColor);
        var sections = CreateRectObject("SectionTabs", root.transform, new Vector2(0.02f, 0.77f), new Vector2(0.98f, 0.86f), BarColor);

        var runtimeBar = CreateRectObject("RuntimeStatus", root.transform, new Vector2(0.02f, 0.665f), new Vector2(0.98f, 0.75f), BarColor);
        var runtimeStatus = CreateText("RuntimeStatusText", runtimeBar.transform, new Vector2(0.015f, 0.08f), new Vector2(0.985f, 0.92f), labelTemplate, "Runtime status: Not reported by this plugin.", TextAlignmentOptions.MidlineLeft, 0.58f);

        var viewport = CreateRectObject("SettingsViewport", root.transform, new Vector2(0.02f, 0.15f), new Vector2(0.98f, 0.65f), new Color(0.035f, 0.043f, 0.06f, 0.98f));
        viewport.AddComponent<RectMask2D>();
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32f;

        var content = new GameObject("SettingsContent", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = contentRect;

        var footer = CreateRectObject("Footer", root.transform, new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.13f), BarColor);
        var status = CreateText("Status", footer.transform, new Vector2(0.02f, 0.12f), new Vector2(0.62f, 0.88f), labelTemplate, "Ready", TextAlignmentOptions.MidlineLeft, 0.72f);

        Button? apply = null;
        Button? revert = null;
        apply = CreateButton("Apply", footer.transform, new Vector2(0.65f, 0.12f), new Vector2(0.81f, 0.88f), labelTemplate, "Apply", () => { });
        revert = CreateButton("Revert", footer.transform, new Vector2(0.82f, 0.12f), new Vector2(0.98f, 0.88f), labelTemplate, "Revert", () => { });

        var panel = new ModConfigPanel(
            root,
            catalog,
            labelTemplate,
            log,
            (RectTransform)header.transform,
            (RectTransform)sections.transform,
            contentRect,
            scroll,
            featureStatuses,
            runtimeStatus,
            status,
            apply,
            revert);
        apply.onClick.RemoveAllListeners();
        apply.onClick.AddListener(panel.Apply);
        revert.onClick.RemoveAllListeners();
        revert.onClick.AddListener(panel.Revert);
        panel.RefreshStatus();
        panel.RefreshRuntimeStatusIfNeeded();
        return panel;
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;

        Root.SetActive(active);
        if (!active) return;

        if (_session.RefreshExternalValues()) RebuildSettings();
        RefreshResponsiveLayout();
        RefreshRuntimeStatusIfNeeded();
    }

    public void RefreshExternalValues()
    {
        if (!_disposed && _session.RefreshExternalValues())
        {
            RebuildSettings();
        }
    }

    public void RefreshResponsiveLayout()
    {
        if (_disposed || !Root.activeInHierarchy || _catalog.Mods.Count == 0) return;
        var descriptionWidth = CalculateDescriptionWidth(_settingsContent.rect.width);
        if (!DescriptionWidthChanged(_measuredDescriptionWidth, descriptionWidth)) return;
        RebuildSettings();
    }

    public void RefreshRuntimeStatusIfNeeded()
    {
        if (_disposed || !_runtimeStatusDirty) return;
        _runtimeStatusDirty = false;
        if (_catalog.Mods.Count == 0)
        {
            _runtimeStatusText.text = "Runtime status: No configurable plugin selected.";
            return;
        }

        var mod = _catalog.Mods[Math.Max(0, Math.Min(_selectedModIndex, _catalog.Mods.Count - 1))];
        _runtimeStatusText.text = ModRuntimeStatusProjection
            .Build(mod.Guid, _featureStatuses.GetSnapshot())
            .FormatCompact();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _featureStatuses.Transitioned -= OnRuntimeStatusTransitioned;
        UnityEngine.Object.Destroy(Root);
    }

    private void RebuildAll(bool resetSettingsScroll)
    {
        ClearObjects(_modTabObjects);
        if (_catalog.Mods.Count == 0)
        {
            _statusText.text = "No loaded plugins expose BepInEx configuration entries.";
            ClearObjects(_sectionTabObjects);
            ClearObjects(_settingObjects);
            return;
        }

        _selectedModIndex = Math.Max(0, Math.Min(_selectedModIndex, _catalog.Mods.Count - 1));
        BuildTabs(
            _modTabs,
            _catalog.Mods.Select(mod => mod.Name.Replace("Orb ", string.Empty)).ToArray(),
            _selectedModIndex,
            _modTabObjects,
            SelectMod);
        RebuildSections(resetSettingsScroll);
    }

    private void RebuildSections(bool resetSettingsScroll)
    {
        ClearObjects(_sectionTabObjects);
        var mod = _catalog.Mods[_selectedModIndex];
        if (mod.Sections.Count == 0)
        {
            ClearObjects(_settingObjects);
            return;
        }

        _selectedSectionIndex = Math.Max(0, Math.Min(_selectedSectionIndex, mod.Sections.Count - 1));
        BuildTabs(
            _sectionTabs,
            mod.Sections.Select(section => section.Name).ToArray(),
            _selectedSectionIndex,
            _sectionTabObjects,
            SelectSection);
        RebuildSettings(resetSettingsScroll);
    }

    private void RebuildSettings(bool resetScroll = false)
    {
        var requestedScrollOffset = resetScroll ? 0f : Math.Max(0f, _settingsContent.anchoredPosition.y);
        ClearObjects(_settingObjects);
        var settings = _catalog.Mods[_selectedModIndex].Sections[_selectedSectionIndex].Settings;
        var descriptionWidth = CalculateDescriptionWidth(_settingsContent.rect.width);
        var contentHeight = 0f;

        foreach (var setting in settings)
        {
            contentHeight += CreateSettingRow(setting, contentHeight, descriptionWidth);
        }

        contentHeight = Math.Max(1f, contentHeight);
        _settingsContent.sizeDelta = new Vector2(0f, contentHeight);
        _measuredDescriptionWidth = descriptionWidth;
        RestoreScrollOffset(requestedScrollOffset, contentHeight);
        RefreshStatus();
    }

    private float CreateSettingRow(
        ConfigSettingDescriptor setting,
        float topOffset,
        float descriptionWidth)
    {
        var edit = _session.Get(setting);
        var row = new GameObject("Setting." + setting.Key, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        row.transform.SetParent(_settingsContent, false);
        var rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = new Vector2(0.01f, 1f);
        rowRect.anchorMax = new Vector2(0.99f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -topOffset - SettingRowTopInset);
        rowRect.sizeDelta = new Vector2(0f, MinimumSettingRowHeight - SettingRowGap);
        row.GetComponent<Image>()!.color = RowColor;
        _settingObjects.Add(row);

        var keyText = CreateText(
            "Key",
            row.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.53f, 1f),
            _labelTemplate,
            setting.DisplayName,
            TextAlignmentOptions.MidlineLeft,
            0.78f);
        SetTopAnchoredHeight((RectTransform)keyText.transform, SettingTitleTopInset, SettingTitleHeight);

        var description = setting.Description;
        if (!string.IsNullOrWhiteSpace(setting.AcceptableValuesDescription))
        {
            description += "  " + setting.AcceptableValuesDescription;
        }
        description += setting.RestartRequired
            ? "  Restart required."
            : "  " + SavedRuntimeMessage;

        var descriptionText = CreateText(
            "Description",
            row.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.55f, 1f),
            _labelTemplate,
            description,
            TextAlignmentOptions.TopLeft,
            0.55f,
            TextOverflowModes.Overflow);
        var preferredDescriptionHeight = descriptionText
            .GetPreferredValues(description, descriptionWidth, 0f)
            .y;
        var rowHeight = CalculateSettingRowHeight(preferredDescriptionHeight);
        var visibleRowHeight = rowHeight - SettingRowGap;
        rowRect.sizeDelta = new Vector2(0f, visibleRowHeight);
        SetTopAnchoredHeight(
            (RectTransform)descriptionText.transform,
            SettingDescriptionTopInset,
            Math.Max(1f, visibleRowHeight - SettingDescriptionTopInset - SettingDescriptionBottomInset));

        if (!_session.DependencySatisfied(setting))
        {
            var dependencyMessage = _session.DescribeUnsatisfiedDependencies(setting);
            CreateText("Dependency", row.transform, new Vector2(0.58f, 0.2f), new Vector2(0.98f, 0.8f), _labelTemplate, dependencyMessage, TextAlignmentOptions.Midline, 0.6f);
            return rowHeight;
        }

        switch (setting.Kind)
        {
            case ConfigEditorKind.Boolean:
                CreateBooleanEditor(row.transform, edit);
                break;
            case ConfigEditorKind.Enum:
                CreateEnumEditor(row.transform, edit);
                break;
            case ConfigEditorKind.BoundedNumeric:
            case ConfigEditorKind.Numeric:
            case ConfigEditorKind.String:
            case ConfigEditorKind.KeyboardShortcut:
                CreateTextEditor(row.transform, edit);
                break;
            default:
                CreateText("ReadOnly", row.transform, new Vector2(0.58f, 0.2f), new Vector2(0.84f, 0.8f), _labelTemplate, edit.StagedSerialized, TextAlignmentOptions.Midline, 0.65f);
                break;
        }

        if (edit.IsEditable)
        {
            CreateButton(
                "Default",
                row.transform,
                new Vector2(0.86f, 0.18f),
                new Vector2(0.98f, 0.82f),
                _labelTemplate,
                "Default",
                () =>
                {
                    edit.StageDefault();
                    RebuildSettings();
                });
        }

        return rowHeight;
    }

    private void CreateBooleanEditor(Transform parent, ConfigEditValue edit)
    {
        CreateButton(
            "Boolean",
            parent,
            new Vector2(0.58f, 0.18f),
            new Vector2(0.84f, 0.82f),
            _labelTemplate,
            edit.StagedSerialized,
            () =>
            {
                var current = bool.TryParse(edit.StagedSerialized, out var parsed) && parsed;
                edit.Stage((!current).ToString());
                RebuildSettings();
            });
    }

    private void CreateEnumEditor(Transform parent, ConfigEditValue edit)
    {
        var names = Enum.GetNames(edit.Setting.SettingType);
        CreateButton(
            "Enum",
            parent,
            new Vector2(0.58f, 0.18f),
            new Vector2(0.84f, 0.82f),
            _labelTemplate,
            edit.StagedSerialized,
            () =>
            {
                var current = Array.FindIndex(names, name => string.Equals(name, edit.StagedSerialized, StringComparison.OrdinalIgnoreCase));
                edit.Stage(names[(current + 1 + names.Length) % names.Length]);
                RebuildSettings();
            });
    }

    private void CreateTextEditor(Transform parent, ConfigEditValue edit)
    {
        var inputObject = CreateRectObject("Input", parent, new Vector2(0.58f, 0.18f), new Vector2(0.84f, 0.82f), ButtonColor);
        var text = CreateText("Text", inputObject.transform, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f), _labelTemplate, edit.StagedSerialized, TextAlignmentOptions.MidlineLeft, 0.62f);
        var input = inputObject.AddComponent<TMP_InputField>();
        input.targetGraphic = inputObject.GetComponent<Image>();
        input.textViewport = (RectTransform)text.transform;
        input.textComponent = text;
        input.text = edit.StagedSerialized;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onSelect.AddListener(_ => SteamKeyboardBridge.TryShow(input, edit.Setting.DisplayName, edit.Setting.Kind));
        input.onValueChanged.AddListener(value =>
        {
            edit.Stage(value);
            RefreshStatus(edit);
        });
    }

    private void SelectMod(int index)
    {
        _selectedModIndex = index;
        _selectedSectionIndex = 0;
        _runtimeStatusDirty = true;
        RebuildAll(resetSettingsScroll: true);
        RefreshRuntimeStatusIfNeeded();
    }

    private void SelectSection(int index)
    {
        _selectedSectionIndex = index;
        RebuildSections(resetSettingsScroll: true);
    }

    private void Apply()
    {
        if (_session.Apply(out var error, out var appliedSettings))
        {
            ModConfigInvalidationPublisher.PublishAppliedSettings(
                GameplayInvalidationBus.Shared,
                Time.frameCount,
                appliedSettings);
            RebuildSettings();
            _statusText.text = ConfigurationSavedMessage;
        }
        else
        {
            _statusText.color = InvalidColor;
            _statusText.text = "Apply failed: " + error;
            _log.LogWarning("Mod Config could not apply staged changes: " + error);
        }
    }

    private void OnRuntimeStatusTransitioned(FeatureStatusTransition transition)
    {
        if (_disposed || _catalog.Mods.Count == 0) return;
        var selectedGuid = _catalog.Mods[Math.Max(0, Math.Min(_selectedModIndex, _catalog.Mods.Count - 1))].Guid;
        var key = transition.Current?.Key ?? transition.Previous?.Key;
        if (key.HasValue && string.Equals(key.Value.PluginId, selectedGuid, StringComparison.Ordinal))
            _runtimeStatusDirty = true;
    }

    private void Revert()
    {
        _session.RevertAll();
        _statusText.text = "Reverted staged changes.";
        RebuildSettings();
    }

    private void RefreshStatus(ConfigEditValue? changed = null)
    {
        if (changed is not null && !changed.IsValid)
        {
            _statusText.color = InvalidColor;
            _statusText.text = $"{changed.Setting.Key}: {changed.Error}";
        }
        else
        {
            _statusText.color = _labelTemplate.color;
            _statusText.text = _session.IsDirty ? "Unsaved changes" : "Ready";
        }

        _applyButton.interactable = _session.IsDirty && _session.IsValid;
        _revertButton.interactable = _session.IsDirty;
    }

    private void BuildTabs(
        RectTransform parent,
        IReadOnlyList<string> labels,
        int selected,
        ICollection<GameObject> owned,
        Action<int> onSelected)
    {
        var count = Math.Max(1, labels.Count);
        for (var index = 0; index < labels.Count; index++)
        {
            var captured = index;
            var left = (float)index / count;
            var right = (float)(index + 1) / count;
            var button = CreateButton(
                "Tab." + labels[index],
                parent,
                new Vector2(left + 0.003f, 0.08f),
                new Vector2(right - 0.003f, 0.92f),
                _labelTemplate,
                labels[index],
                () => onSelected(captured),
                index == selected);
            owned.Add(button.gameObject);
        }
    }

    private static GameObject CreateRectObject(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color? color = null)
    {
        var types = color.HasValue
            ? new[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) }
            : new[] { typeof(RectTransform) };
        var gameObject = new GameObject(name, types);
        gameObject.transform.SetParent(parent, false);
        var rect = (RectTransform)gameObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        if (color.HasValue)
        {
            gameObject.GetComponent<Image>()!.color = color.Value;
        }

        return gameObject;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextMeshProUGUI template,
        string value,
        TextAlignmentOptions alignment,
        float sizeScale,
        TextOverflowModes overflowMode = TextOverflowModes.Ellipsis)
    {
        var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        gameObject.transform.SetParent(parent, false);
        var rect = (RectTransform)gameObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = gameObject.GetComponent<TextMeshProUGUI>()!;
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = Math.Max(12f, template.fontSize * sizeScale);
        text.color = template.color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = overflowMode;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private void RestoreScrollOffset(float requestedOffset, float contentHeight)
    {
        var viewportHeight = _settingsScroll.viewport?.rect.height ?? 0f;
        var clampedOffset = ClampScrollOffset(requestedOffset, contentHeight, viewportHeight);
        _settingsScroll.verticalNormalizedPosition = CalculateVerticalNormalizedPosition(
            clampedOffset,
            contentHeight,
            viewportHeight);
    }

    internal static float CalculateSettingRowHeight(float preferredDescriptionHeight)
    {
        if (!IsFiniteNonNegative(preferredDescriptionHeight)) preferredDescriptionHeight = 0f;

        return Math.Max(
            MinimumSettingRowHeight,
            SettingDescriptionTopInset +
            preferredDescriptionHeight +
            SettingDescriptionHeightPadding +
            SettingDescriptionBottomInset +
            SettingRowGap);
    }

    internal static float ClampScrollOffset(float requestedOffset, float contentHeight, float viewportHeight)
    {
        if (!IsFiniteNonNegative(requestedOffset)) requestedOffset = 0f;
        if (!IsFiniteNonNegative(contentHeight)) contentHeight = 0f;
        if (!IsFiniteNonNegative(viewportHeight)) viewportHeight = 0f;
        var maximumOffset = Math.Max(0f, contentHeight - viewportHeight);
        return Math.Max(0f, Math.Min(requestedOffset, maximumOffset));
    }

    internal static float CalculateVerticalNormalizedPosition(
        float scrollOffset,
        float contentHeight,
        float viewportHeight)
    {
        if (!IsFiniteNonNegative(contentHeight)) contentHeight = 0f;
        if (!IsFiniteNonNegative(viewportHeight)) viewportHeight = 0f;
        var maximumOffset = Math.Max(0f, contentHeight - viewportHeight);
        if (maximumOffset <= 0f) return 1f;

        return 1f - ClampScrollOffset(scrollOffset, contentHeight, viewportHeight) / maximumOffset;
    }

    internal static float CalculateDescriptionWidth(float contentWidth)
    {
        if (!IsFiniteNonNegative(contentWidth) || contentWidth < MinimumMeasurableContentWidth)
            return FallbackDescriptionWidth;

        const float rowWidthFraction = 0.98f;
        const float descriptionWidthFraction = 0.55f - 0.018f;
        return contentWidth * rowWidthFraction * descriptionWidthFraction;
    }

    internal static bool DescriptionWidthChanged(float previousWidth, float currentWidth)
    {
        if (!IsFiniteNonNegative(currentWidth) || currentWidth <= 0f) return false;
        return !IsFiniteNonNegative(previousWidth) ||
            previousWidth <= 0f ||
            Math.Abs(previousWidth - currentWidth) > DescriptionWidthChangeTolerance;
    }

    private static bool IsFiniteNonNegative(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

    private static void SetTopAnchoredHeight(RectTransform rect, float topInset, float height)
    {
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -topInset);
        rect.sizeDelta = new Vector2(0f, height);
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextMeshProUGUI template,
        string label,
        UnityAction action,
        bool active = false)
    {
        var gameObject = CreateRectObject(name, parent, anchorMin, anchorMax, active ? ActiveButtonColor : ButtonColor);
        var button = gameObject.AddComponent<Button>();
        button.targetGraphic = gameObject.GetComponent<Image>();
        CreateText("Label", gameObject.transform, new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), template, label, TextAlignmentOptions.Midline, 0.68f);
        button.onClick.AddListener(action);
        return button;
    }

    private static void ClearObjects(ICollection<GameObject> objects)
    {
        foreach (var gameObject in objects)
        {
            UnityEngine.Object.Destroy(gameObject);
        }

        objects.Clear();
    }
}
