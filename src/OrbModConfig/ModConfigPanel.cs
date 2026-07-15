using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class ModConfigPanel : IDisposable
{
    private const float SettingRowHeight = 86f;
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
    private readonly TextMeshProUGUI _statusText;
    private readonly Button _applyButton;
    private readonly Button _revertButton;
    private readonly List<GameObject> _modTabObjects = new List<GameObject>();
    private readonly List<GameObject> _sectionTabObjects = new List<GameObject>();
    private readonly List<GameObject> _settingObjects = new List<GameObject>();
    private int _selectedModIndex;
    private int _selectedSectionIndex;
    private bool _disposed;

    private ModConfigPanel(
        GameObject root,
        ConfigCatalogSnapshot catalog,
        TextMeshProUGUI labelTemplate,
        ManualLogSource log,
        RectTransform modTabs,
        RectTransform sectionTabs,
        RectTransform settingsContent,
        ScrollRect settingsScroll,
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
        _statusText = statusText;
        _applyButton = applyButton;
        _revertButton = revertButton;
        RebuildAll();
        SetActive(false);
    }

    public GameObject Root { get; }

    public static ModConfigPanel Create(
        Transform parent,
        TextMeshProUGUI labelTemplate,
        ConfigCatalogSnapshot catalog,
        ManualLogSource log)
    {
        var root = CreateRectObject(ModConfigUiShell.PanelObjectName, parent, Vector2.zero, Vector2.one, BackgroundColor);

        var header = CreateRectObject("ModTabs", root.transform, new Vector2(0.02f, 0.875f), new Vector2(0.98f, 0.975f), BarColor);
        var sections = CreateRectObject("SectionTabs", root.transform, new Vector2(0.02f, 0.77f), new Vector2(0.98f, 0.86f), BarColor);

        var viewport = CreateRectObject("SettingsViewport", root.transform, new Vector2(0.02f, 0.15f), new Vector2(0.98f, 0.75f), new Color(0.035f, 0.043f, 0.06f, 0.98f));
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
            status,
            apply,
            revert);
        apply.onClick.RemoveAllListeners();
        apply.onClick.AddListener(panel.Apply);
        revert.onClick.RemoveAllListeners();
        revert.onClick.AddListener(panel.Revert);
        panel.RefreshStatus();
        return panel;
    }

    public void SetActive(bool active)
    {
        if (!_disposed)
        {
            Root.SetActive(active);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnityEngine.Object.Destroy(Root);
    }

    private void RebuildAll()
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
        RebuildSections();
    }

    private void RebuildSections()
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
        RebuildSettings();
    }

    private void RebuildSettings()
    {
        ClearObjects(_settingObjects);
        var settings = _catalog.Mods[_selectedModIndex].Sections[_selectedSectionIndex].Settings;
        _settingsContent.sizeDelta = new Vector2(0f, Math.Max(1, settings.Count) * SettingRowHeight);
        _settingsScroll.verticalNormalizedPosition = 1f;

        for (var index = 0; index < settings.Count; index++)
        {
            CreateSettingRow(settings[index], index);
        }

        RefreshStatus();
    }

    private void CreateSettingRow(ConfigSettingDescriptor setting, int index)
    {
        var edit = _session.Get(setting);
        var row = new GameObject("Setting." + setting.Key, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        row.transform.SetParent(_settingsContent, false);
        var rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = new Vector2(0.01f, 1f);
        rowRect.anchorMax = new Vector2(0.99f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -index * SettingRowHeight - 4f);
        rowRect.sizeDelta = new Vector2(0f, SettingRowHeight - 7f);
        row.GetComponent<Image>()!.color = RowColor;
        _settingObjects.Add(row);

        CreateText("Key", row.transform, new Vector2(0.018f, 0.51f), new Vector2(0.53f, 0.94f), _labelTemplate, setting.Key, TextAlignmentOptions.MidlineLeft, 0.78f);
        var description = setting.Description;
        if (!string.IsNullOrWhiteSpace(setting.AcceptableValuesDescription))
        {
            description += "  " + setting.AcceptableValuesDescription;
        }

        CreateText("Description", row.transform, new Vector2(0.018f, 0.08f), new Vector2(0.55f, 0.52f), _labelTemplate, description, TextAlignmentOptions.TopLeft, 0.55f);

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
    }

    private void CreateBooleanEditor(Transform parent, ConfigEditValue edit)
    {
        Button? button = null;
        TextMeshProUGUI? label = null;
        button = CreateButton(
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
                if (label is not null)
                {
                    label.text = edit.StagedSerialized;
                }

                RefreshStatus(edit);
            });
        label = button.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void CreateEnumEditor(Transform parent, ConfigEditValue edit)
    {
        var names = Enum.GetNames(edit.Setting.SettingType);
        Button? button = null;
        TextMeshProUGUI? label = null;
        button = CreateButton(
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
                if (label is not null)
                {
                    label.text = edit.StagedSerialized;
                }

                RefreshStatus(edit);
            });
        label = button.GetComponentInChildren<TextMeshProUGUI>(true);
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
        RebuildAll();
    }

    private void SelectSection(int index)
    {
        _selectedSectionIndex = index;
        RebuildSections();
    }

    private void Apply()
    {
        if (_session.Apply(out var error))
        {
            _statusText.text = "Applied and saved.";
            RebuildSettings();
        }
        else
        {
            _statusText.color = InvalidColor;
            _statusText.text = "Apply failed: " + error;
            _log.LogWarning("Mod Config could not apply staged changes: " + error);
        }
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
        float sizeScale)
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
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.text = value;
        return text;
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
