using System;
using System.Collections.Generic;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal readonly record struct RuntimeFeatureHealthItem(
    FeatureStatusSnapshot Status,
    RuntimeDiagnosticsSeverity Severity);

internal static class RuntimeFeatureHealthProjection
{
    public static IReadOnlyList<RuntimeFeatureHealthItem> Build(RuntimeDiagnosticsDashboard dashboard)
    {
        if (dashboard is null) throw new ArgumentNullException(nameof(dashboard));
        var items = new List<RuntimeFeatureHealthItem>();
        foreach (var card in dashboard.Cards)
        {
            foreach (var status in card.FeatureStatuses)
            {
                items.Add(new RuntimeFeatureHealthItem(
                    status,
                    RuntimeDiagnosticsProjection.Severity(status.State)));
            }
        }
        items.Sort(Comparer<RuntimeFeatureHealthItem>.Create(Compare));
        return items;
    }

    private static int Compare(RuntimeFeatureHealthItem left, RuntimeFeatureHealthItem right)
    {
        var severity = right.Severity.CompareTo(left.Severity);
        return severity != 0
            ? severity
            : string.Compare(
                left.Status.DisplayName,
                right.Status.DisplayName,
                StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class RuntimeFeatureHealthGridView : IDisposable
{
    private const float HeaderHeight = 32f;
    private const float CellHeight = 78f;
    private const float RowGap = 6f;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly List<Cell> _cells = new();
    private readonly TextMeshProUGUI _template;

    public RuntimeFeatureHealthGridView(RectTransform parent, TextMeshProUGUI template)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _root = ModConfigUiFactory.CreateRectObject(
            "FeatureHealthGrid",
            parent,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f));
        _rect = (RectTransform)_root.transform;
        _rect.pivot = new Vector2(0.5f, 1f);
        ModConfigUiFactory.CreateText(
            "Title",
            _root.transform,
            new Vector2(0.015f, 1f),
            new Vector2(0.985f, 1f),
            template,
            "Feature health",
            TextAlignmentOptions.TopLeft,
            0.82f);
    }

    public float Layout(
        IReadOnlyList<RuntimeFeatureHealthItem> items,
        float topOffset,
        int siblingIndex)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        while (_cells.Count < items.Count) _cells.Add(new Cell(_rect, _template));
        for (var index = 0; index < _cells.Count; index++)
        {
            if (index >= items.Count)
            {
                _cells[index].SetActive(false);
                continue;
            }
            _cells[index].SetActive(true);
            _cells[index].Render(items[index], index);
        }

        var rows = Math.Max(1, (items.Count + 1) / 2);
        var height = HeaderHeight + rows * (CellHeight + RowGap);
        _rect.anchoredPosition = new Vector2(0f, -topOffset);
        _rect.sizeDelta = new Vector2(0f, height);
        _root.transform.SetSiblingIndex(siblingIndex);
        return height;
    }

    public void Dispose()
    {
        foreach (var cell in _cells) cell.Dispose();
        _cells.Clear();
        UnityEngine.Object.Destroy(_root);
    }

    private sealed class Cell : IDisposable
    {
        private static readonly Color Failure = new(0.32f, 0.10f, 0.10f, 0.98f);
        private static readonly Color Attention = new(0.28f, 0.20f, 0.08f, 0.98f);
        private static readonly Color Waiting = new(0.10f, 0.15f, 0.22f, 0.98f);
        private readonly GameObject _root;
        private readonly RectTransform _rect;
        private readonly Image _frame;
        private readonly TextMeshProUGUI _title;
        private readonly TextMeshProUGUI _body;

        public Cell(RectTransform parent, TextMeshProUGUI template)
        {
            _root = ModConfigUiFactory.CreateRectObject(
                "FeatureHealth",
                parent,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                ModConfigPalette.Row);
            _rect = (RectTransform)_root.transform;
            _rect.pivot = new Vector2(0.5f, 1f);
            _frame = _root.GetComponent<Image>()!;
            ModConfigNativeRailFactory.SkinPanel(
                _frame,
                ModConfigUiFactory.NativeVisuals.FeatureRailBaseFrame,
                ModConfigPalette.Row);
            _title = ModConfigUiFactory.CreateText(
                "Title",
                _root.transform,
                new Vector2(0.035f, 0.52f),
                new Vector2(0.965f, 0.94f),
                template,
                string.Empty,
                TextAlignmentOptions.MidlineLeft,
                0.72f);
            _body = ModConfigUiFactory.CreateText(
                "Body",
                _root.transform,
                new Vector2(0.035f, 0.08f),
                new Vector2(0.965f, 0.52f),
                template,
                string.Empty,
                TextAlignmentOptions.MidlineLeft,
                0.52f);
        }

        public void SetActive(bool active) => _root.SetActive(active);

        public void Render(RuntimeFeatureHealthItem item, int index)
        {
            var column = index % 2;
            var row = index / 2;
            _rect.anchorMin = new Vector2(column == 0 ? 0.01f : 0.505f, 1f);
            _rect.anchorMax = new Vector2(column == 0 ? 0.495f : 0.99f, 1f);
            _rect.anchoredPosition = new Vector2(0f, -HeaderHeight - row * (CellHeight + RowGap));
            _rect.sizeDelta = new Vector2(0f, CellHeight);
            var presentation = FeatureStatusPresenter.Present(item.Status);
            _title.text = item.Status.DisplayName;
            _body.text = presentation.ConfiguredLabel + " · " + presentation.RuntimeLabel +
                         (item.Status.Reason.IsEmpty ? string.Empty : " · " + item.Status.Reason.Summary);
            _frame.color = item.Severity switch
            {
                RuntimeDiagnosticsSeverity.Failure => Failure,
                RuntimeDiagnosticsSeverity.Attention => Attention,
                RuntimeDiagnosticsSeverity.Waiting => Waiting,
                _ => ModConfigPalette.Row,
            };
        }

        public void Dispose() => UnityEngine.Object.Destroy(_root);
    }
}
