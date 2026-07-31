using System;
using OrbAutomata;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>Suite-owned Mods-page editor for exact temporary-item approval.</summary>
internal sealed class AutoItemsTemporaryItemPickerView
{
    private const float StateTop = 5f;
    private const float StateHeight = 30f;
    private const float StateContentGap = 7f;
    private const float ItemHeight = 48f;
    private const float ItemStride = 53f;
    private const float BottomInset = 5f;
    private const float EditorAnchorSpan = 0.4f;
    private const float RowAnchorSpan = 0.98f;
    private const float StateWidthFraction = 0.7f;
    private const float FailureTextWidthFraction = 0.93f;
    private const float FailureTextHeightFraction = 0.84f;
    private const float FailureTextSizeScale = 0.55f;

    private readonly TextMeshProUGUI _labelTemplate;
    private readonly Action _rebuildRequested;
    private readonly Action<ConfigEditValue?> _statusChanged;

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
        setting is not null &&
        string.Equals(setting.PluginGuid, PluginIds.SuiteGuid, StringComparison.Ordinal) &&
        string.Equals(setting.SourceSection, "AutoItems", StringComparison.Ordinal) &&
        string.Equals(setting.Key, "TemporaryItemAllowlist", StringComparison.Ordinal) &&
        setting.SettingType == typeof(string);

    internal AutoItemsTemporaryItemCatalogSnapshot CaptureCatalog() =>
        AutoItemsTemporaryItemCatalog.Capture();

    internal static float CalculateEditorWidth(float contentWidth) =>
        Math.Max(1f, contentWidth * RowAnchorSpan * EditorAnchorSpan);

    internal float Measure(
        ConfigEditValue edit,
        AutoItemsTemporaryItemCatalogSnapshot catalog,
        float editorWidth,
        float minimumHeight)
    {
        if (edit is null) throw new ArgumentNullException(nameof(edit));
        var presentation = AutoItemsTemporaryItemPickerModel.Compose(
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            edit.StagedSerialized);
        var layout = CalculateLayout(presentation, editorWidth);
        var rows = presentation.ContentState == AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed
            ? presentation.UnresolvableEntries.Count
            : Math.Max(1, presentation.Items.Count) + presentation.UnresolvableEntries.Count;
        var contentHeight = presentation.ContentState ==
            AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed
                ? layout.FailureHeight + rows * ItemStride
                : rows * ItemStride;
        return Math.Max(
            minimumHeight,
            layout.ContentTop + contentHeight + BottomInset +
            (presentation.ContentState ==
                AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed
                    ? ItemStride - ItemHeight
                    : 0f));
    }

    internal void Render(
        Transform parent,
        ConfigEditValue edit,
        AutoItemsTemporaryItemCatalogSnapshot catalog,
        float editorWidth)
    {
        if (parent is null) throw new ArgumentNullException(nameof(parent));
        if (edit is null) throw new ArgumentNullException(nameof(edit));
        var presentation = AutoItemsTemporaryItemPickerModel.Compose(
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            edit.StagedSerialized);
        var layout = CalculateLayout(presentation, editorWidth);

        var state = CreateTopText(
            "PickerStateLine",
            parent,
            0.58f,
            0.86f,
            StateTop,
            layout.StateHeight,
            presentation.ApprovalStateLine,
            TextAlignmentOptions.MidlineLeft,
            0.62f);
        if (presentation.ContentState ==
            AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed)
        {
            state.color = ModConfigPalette.Invalid;
        }

        CreateTopButton(
            "Default",
            parent,
            0.87f,
            0.98f,
            StateTop,
            StateHeight,
            "Default",
            () =>
            {
                edit.StageDefault();
                Changed(edit);
            });

        if (presentation.ContentState ==
            AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed)
        {
            CreateFailureState(
                parent,
                layout.ContentTop,
                layout.FailureHeight,
                presentation.ContentMessage);
            var failureTop = layout.ContentTop + layout.FailureHeight +
                (ItemStride - ItemHeight);
            for (var index = 0; index < presentation.UnresolvableEntries.Count; index++)
            {
                CreateUnresolvableRow(
                    parent,
                    failureTop,
                    edit,
                    presentation.UnresolvableEntries[index],
                    index);
                failureTop += ItemStride;
            }
            return;
        }

        var top = layout.ContentTop;
        if (presentation.Items.Count == 0)
        {
            CreateTopText(
                "PickerEmptyState",
                parent,
                0.58f,
                0.98f,
                top,
                ItemHeight,
                presentation.ContentMessage,
                TextAlignmentOptions.MidlineLeft,
                0.6f);
            top += ItemStride;
        }
        else
        {
            for (var index = 0; index < presentation.Items.Count; index++)
            {
                var item = presentation.Items[index];
                CreateItemRow(parent, top, edit, item);
                top += ItemStride;
            }
        }

        for (var index = 0; index < presentation.UnresolvableEntries.Count; index++)
        {
            var entry = presentation.UnresolvableEntries[index];
            CreateUnresolvableRow(parent, top, edit, entry, index);
            top += ItemStride;
        }
    }

    private void CreateItemRow(
        Transform parent,
        float top,
        ConfigEditValue edit,
        AutoItemsTemporaryItemPickerItem item)
    {
        var option = item.Option;
        var button = CreateTopButton(
            "PickerItem." + option.ItemId.ToString("N"),
            parent,
            0.58f,
            0.98f,
            top,
            ItemHeight,
            $"{option.DisplayName}\n{option.FamilyDisplay} · Stock {option.Stock}",
            () =>
            {
                edit.Stage(AutoItemsTemporaryItemPickerModel.Toggle(
                    edit.StagedSerialized,
                    option.ItemId));
                Changed(edit);
            },
            active: item.IsApproved);

        var label = DirectText(button.transform, "Label");
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = new Vector2(0.17f, 0.05f);
        labelRect.anchorMax = new Vector2(0.78f, 0.95f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSize = Math.Max(12f, _labelTemplate.fontSize * 0.56f);

        var iconObject = ModConfigUiFactory.CreateRectObject(
            "Icon",
            button.transform,
            new Vector2(0.025f, 0.14f),
            new Vector2(0.145f, 0.86f));
        var icon = iconObject.AddComponent<Image>();
        icon.sprite = option.Icon;
        icon.color = Color.white;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        ModConfigUiFactory.CreateText(
            "Approval",
            button.transform,
            new Vector2(0.80f, 0.08f),
            new Vector2(0.97f, 0.92f),
            _labelTemplate,
            item.IsApproved ? "Approved" : "Approve",
            TextAlignmentOptions.Midline,
            0.55f);
    }

    private void CreateUnresolvableRow(
        Transform parent,
        float top,
        ConfigEditValue edit,
        AutoItemsTemporaryItemUnresolvableEntry entry,
        int index)
    {
        var safeName = entry.IsUuid
            ? entry.ItemId.ToString("N")
            : "Invalid." + index;
        var button = CreateTopButton(
            "PickerUnresolvable." + safeName,
            parent,
            0.58f,
            0.98f,
            top,
            ItemHeight,
            $"{entry.Heading}\n{entry.StoredToken}",
            () =>
            {
                edit.Stage(AutoItemsTemporaryItemPickerModel.Remove(
                    edit.StagedSerialized,
                    entry));
                Changed(edit);
            });
        var label = DirectText(button.transform, "Label");
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = new Vector2(0.035f, 0.05f);
        labelRect.anchorMax = new Vector2(0.78f, 0.95f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSize = Math.Max(12f, _labelTemplate.fontSize * 0.5f);
        label.color = ModConfigPalette.Invalid;
        var remove = ModConfigUiFactory.CreateText(
            "Remove",
            button.transform,
            new Vector2(0.80f, 0.08f),
            new Vector2(0.97f, 0.92f),
            _labelTemplate,
            "Remove",
            TextAlignmentOptions.Midline,
            0.55f);
        remove.color = ModConfigPalette.Invalid;
    }

    private void CreateFailureState(
        Transform parent,
        float top,
        float height,
        string reason)
    {
        var panel = ModConfigUiFactory.CreateRectObject(
            "PickerDiscoveryFailure",
            parent,
            new Vector2(0.58f, 1f),
            new Vector2(0.98f, 1f),
            Color.white);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)panel.transform,
            top,
            height);
        var frame = panel.GetComponent<Image>()!;
        frame.sprite = ModConfigUiFactory.NativeVisuals.FeatureRailActiveFrame;
        frame.color = Color.white;
        frame.raycastTarget = false;
        var text = ModConfigUiFactory.CreateText(
            "Reason",
            panel.transform,
            new Vector2(0.035f, 0.08f),
            new Vector2(0.965f, 0.92f),
            _labelTemplate,
            reason,
            TextAlignmentOptions.TopLeft,
            FailureTextSizeScale,
            TextOverflowModes.Overflow);
        text.color = ModConfigPalette.Invalid;
    }

    private PickerLayout CalculateLayout(
        AutoItemsTemporaryItemPickerPresentation presentation,
        float editorWidth)
    {
        if (editorWidth <= 0f || float.IsNaN(editorWidth) || float.IsInfinity(editorWidth))
            throw new ArgumentOutOfRangeException(nameof(editorWidth));
        if (presentation.ContentState !=
            AutoItemsTemporaryItemPickerContentState.DiscoveryReadFailed)
        {
            return new PickerLayout(
                StateHeight,
                StateTop + StateHeight + StateContentGap,
                ItemHeight);
        }

        var statePreferred = MeasureTextHeight(
            presentation.ApprovalStateLine,
            editorWidth * StateWidthFraction,
            0.62f);
        var measuredStateHeight = Math.Max(StateHeight, statePreferred + 2f);
        var failurePreferred = MeasureTextHeight(
            presentation.ContentMessage,
            editorWidth * FailureTextWidthFraction,
            FailureTextSizeScale);
        var measuredFailureHeight = Math.Max(
            ItemHeight,
            failurePreferred / FailureTextHeightFraction);
        return new PickerLayout(
            measuredStateHeight,
            StateTop + measuredStateHeight + StateContentGap,
            measuredFailureHeight);
    }

    private float MeasureTextHeight(string value, float width, float sizeScale)
    {
        var templateSize = Math.Max(1f, _labelTemplate.fontSize);
        var renderedSize = Math.Max(12f, _labelTemplate.fontSize * sizeScale);
        var scale = renderedSize / templateSize;
        return _labelTemplate.GetPreferredValues(
            value,
            Math.Max(1f, width / scale),
            0f).y * scale;
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
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)button.transform, top, height);
        return button;
    }

    private TextMeshProUGUI CreateTopText(
        string name,
        Transform parent,
        float left,
        float right,
        float top,
        float height,
        string value,
        TextAlignmentOptions alignment,
        float sizeScale)
    {
        var text = ModConfigUiFactory.CreateText(
            name,
            parent,
            new Vector2(left, 1f),
            new Vector2(right, 1f),
            _labelTemplate,
            value,
            alignment,
            sizeScale,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)text.transform, top, height);
        return text;
    }

    private void Changed(ConfigEditValue edit)
    {
        _statusChanged(edit);
        _rebuildRequested();
    }

    private static TextMeshProUGUI DirectText(Transform parent, string name)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (!string.Equals(child.gameObject.name, name, StringComparison.Ordinal)) continue;
            return child.gameObject.GetComponent<TextMeshProUGUI>() ??
                throw new InvalidOperationException($"{name} was not a text component.");
        }
        throw new InvalidOperationException($"{name} was not created.");
    }

    private readonly record struct PickerLayout(
        float StateHeight,
        float ContentTop,
        float FailureHeight);
}
