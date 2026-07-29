using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// Owns the specialized Mods-page editor for the Auto Items temporary UUID selection.
/// </summary>
internal sealed class AutoItemsTemporaryItemPickerView
{
    private const float EditorTop = 76f;
    private const float ItemHeight = 40f;
    private const float ItemStride = 44f;

    private readonly TextMeshProUGUI _labelTemplate;
    private readonly Action _rebuildRequested;
    private readonly Action<ConfigEditValue?> _statusChanged;
    private readonly AutoItemsTemporaryItemPickerState _state = new();

    internal AutoItemsTemporaryItemPickerView(
        TextMeshProUGUI labelTemplate,
        Action rebuildRequested,
        Action<ConfigEditValue?> statusChanged)
    {
        _labelTemplate = labelTemplate ?? throw new ArgumentNullException(nameof(labelTemplate));
        _rebuildRequested =
            rebuildRequested ?? throw new ArgumentNullException(nameof(rebuildRequested));
        _statusChanged = statusChanged ?? throw new ArgumentNullException(nameof(statusChanged));
    }

    internal static bool AppliesTo(ConfigSettingDescriptor setting) =>
        string.Equals(setting.PluginGuid, PluginIds.SuiteGuid, StringComparison.Ordinal) &&
        string.Equals(setting.SourceSection, "AutoItems", StringComparison.Ordinal) &&
        string.Equals(setting.Key, "TemporaryItemAllowlist", StringComparison.Ordinal) &&
        setting.SettingType == typeof(string);

    internal AutoItemsTemporaryItemCatalogSnapshot? CaptureCatalog() =>
        _state.Mode == AutoItemsTemporaryItemEditorMode.Items
            ? AutoItemsTemporaryItemCatalog.Capture()
            : null;

    internal float Measure(
        ConfigEditValue edit,
        AutoItemsTemporaryItemCatalogSnapshot? catalog,
        float minimumHeight)
    {
        if (_state.Mode == AutoItemsTemporaryItemEditorMode.Raw) return 140f;
        if (_state.Mode != AutoItemsTemporaryItemEditorMode.Items) return minimumHeight;
        if (catalog is null || !catalog.IsAvailable) return 148f;

        var selected = AutoItemsTemporaryItemSelection.Parse(edit.StagedSerialized);
        var known = new HashSet<Guid>();
        var visible = 0;
        for (var index = 0; index < catalog.Options.Count; index++)
        {
            var option = catalog.Options[index];
            known.Add(option.ItemId);
            if (AutoItemsTemporaryItemFiltering.Matches(option, _state.Filter, selected))
                visible++;
        }
        var unavailable = 0;
        if (AutoItemsTemporaryItemFiltering.ShowsUnavailable(_state.Filter))
        {
            foreach (var itemId in selected)
            {
                if (!known.Contains(itemId)) unavailable++;
            }
        }
        return 88f + Math.Max(1, visible + unavailable) * ItemStride;
    }

    internal void Render(
        Transform parent,
        ConfigEditValue edit,
        AutoItemsTemporaryItemCatalogSnapshot? catalog)
    {
        var selected = AutoItemsTemporaryItemSelection.Parse(edit.StagedSerialized);
        CreateTopButton(
            "Items",
            parent,
            0.58f,
            0.68f,
            12f,
            52f,
            $"Items ({selected.Count})",
            () =>
            {
                _state.ToggleItems();
                _rebuildRequested();
            },
            _state.Mode == AutoItemsTemporaryItemEditorMode.Items);
        CreateTopButton(
            "Filter",
            parent,
            0.69f,
            0.78f,
            12f,
            52f,
            _state.Filter.ToString(),
            () =>
            {
                _state.CycleFilter();
                _rebuildRequested();
            },
            _state.Filter != AutoItemsTemporaryItemFilter.All);
        CreateTopButton(
            "Raw",
            parent,
            0.79f,
            0.87f,
            12f,
            52f,
            "Raw",
            () =>
            {
                _state.ToggleRaw();
                _rebuildRequested();
            },
            _state.Mode == AutoItemsTemporaryItemEditorMode.Raw);
        CreateTopButton(
            "Default",
            parent,
            0.88f,
            0.98f,
            12f,
            52f,
            "Default",
            () =>
            {
                edit.StageDefault();
                _statusChanged(edit);
                _rebuildRequested();
            });

        if (_state.Mode == AutoItemsTemporaryItemEditorMode.Raw)
        {
            CreateRawEditor(parent, edit);
            return;
        }
        if (_state.Mode != AutoItemsTemporaryItemEditorMode.Items) return;
        RenderItems(parent, edit, selected, catalog);
    }

    private void RenderItems(
        Transform parent,
        ConfigEditValue edit,
        HashSet<Guid> selected,
        AutoItemsTemporaryItemCatalogSnapshot? catalog)
    {
        if (catalog is null || !catalog.IsAvailable)
        {
            CreateTopText(
                "Unavailable",
                parent,
                EditorTop,
                58f,
                catalog?.UnavailableReason ?? "The temporary-item catalog is unavailable.");
            return;
        }

        var known = new HashSet<Guid>();
        var top = EditorTop;
        for (var index = 0; index < catalog.Options.Count; index++)
        {
            var option = catalog.Options[index];
            known.Add(option.ItemId);
            if (!AutoItemsTemporaryItemFiltering.Matches(option, _state.Filter, selected))
                continue;

            var selectedNow = selected.Contains(option.ItemId);
            CreateTopButton(
                "Item." + option.ItemId.ToString("N"),
                parent,
                0.58f,
                0.98f,
                top,
                ItemHeight,
                ItemLabel(option, selectedNow),
                () => Toggle(edit, option.ItemId),
                selectedNow);
            top += ItemStride;
        }

        if (AutoItemsTemporaryItemFiltering.ShowsUnavailable(_state.Filter))
        {
            foreach (var itemId in SelectedUnavailable(selected, known))
            {
                var captured = itemId;
                CreateTopButton(
                    "Unavailable." + captured.ToString("N"),
                    parent,
                    0.58f,
                    0.98f,
                    top,
                    ItemHeight,
                    $"[x] Unavailable item | {captured:D} | click to remove",
                    () => Toggle(edit, captured),
                    active: true);
                top += ItemStride;
            }
        }

        if (top == EditorTop)
            CreateTopText(
                "Empty",
                parent,
                top,
                ItemHeight,
                "No temporary items match this filter.");
    }

    private void Toggle(ConfigEditValue edit, Guid itemId)
    {
        edit.Stage(AutoItemsTemporaryItemSelection.Toggle(edit.StagedSerialized, itemId));
        _statusChanged(edit);
        _rebuildRequested();
    }

    private static string ItemLabel(
        AutoItemsTemporaryItemOption option,
        bool selected)
    {
        var duration = option.DurationSeconds > 0d &&
                       !double.IsNaN(option.DurationSeconds) &&
                       !double.IsInfinity(option.DurationSeconds)
            ? $" | {option.DurationSeconds:0.#}s"
            : string.Empty;
        var toxicity = option.ToxicityCost.Length == 0
            ? string.Empty
            : $" | toxicity {option.ToxicityCost}";
        return $"{(selected ? "[x]" : "[ ]")} {option.Family} | {option.DisplayName}" +
               $" | owned {option.OwnedQuantity}{toxicity}{duration}";
    }

    private static IReadOnlyList<Guid> SelectedUnavailable(
        HashSet<Guid> selected,
        HashSet<Guid> known)
    {
        var unavailable = new List<Guid>();
        foreach (var itemId in selected)
        {
            if (!known.Contains(itemId)) unavailable.Add(itemId);
        }
        unavailable.Sort();
        return unavailable;
    }

    private Button CreateTopButton(
        string name,
        Transform parent,
        float left,
        float right,
        float top,
        float height,
        string label,
        UnityEngine.Events.UnityAction action,
        bool active = false)
    {
        var button = ModConfigUiFactory.CreateButton(
            name,
            parent,
            new Vector2(left, 1f),
            new Vector2(right, 1f),
            _labelTemplate,
            label,
            action,
            active);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)button.transform,
            top,
            height);
        return button;
    }

    private void CreateTopText(
        string name,
        Transform parent,
        float top,
        float height,
        string value)
    {
        var text = ModConfigUiFactory.CreateText(
            name,
            parent,
            new Vector2(0.58f, 1f),
            new Vector2(0.98f, 1f),
            _labelTemplate,
            value,
            TextAlignmentOptions.MidlineLeft,
            0.55f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)text.transform,
            top,
            height);
    }

    private void CreateRawEditor(Transform parent, ConfigEditValue edit)
    {
        var inputObject = ModConfigUiFactory.CreateRectObject(
            "RawInput",
            parent,
            new Vector2(0.58f, 1f),
            new Vector2(0.98f, 1f),
            ModConfigPalette.Button);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)inputObject.transform,
            EditorTop,
            50f);
        var text = ModConfigUiFactory.CreateText(
            "Text",
            inputObject.transform,
            new Vector2(0.025f, 0.05f),
            new Vector2(0.975f, 0.95f),
            _labelTemplate,
            edit.StagedSerialized,
            TextAlignmentOptions.MidlineLeft,
            0.55f);
        var input = inputObject.AddComponent<TMP_InputField>();
        input.targetGraphic = inputObject.GetComponent<Image>();
        input.textViewport = (RectTransform)text.transform;
        input.textComponent = text;
        input.text = edit.StagedSerialized;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onSelect.AddListener(_ =>
            SteamKeyboardBridge.TryShow(input, edit.Setting.DisplayName, edit.Setting.Kind));
        input.onValueChanged.AddListener(value =>
        {
            edit.Stage(value);
            _statusChanged(edit);
        });
        input.onEndEdit.AddListener(_ => _rebuildRequested());
    }
}
