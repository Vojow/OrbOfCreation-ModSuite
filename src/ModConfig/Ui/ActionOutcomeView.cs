using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class ActionOutcomeView : IDisposable
{
    private const float HeaderHeight = 38f;
#if SERVICE_CYCLE_PROFILE
    private const float CellHeight = 94f;
#else
    private const float CellHeight = 78f;
#endif
    private const float RowGap = 7f;
    private const float TimingHeight = 34f;
    private readonly IServiceActionOutcomeWindowSource _outcomes;
    private readonly IServiceCyclePumpTimingSource _timings;
    private readonly TextMeshProUGUI _template;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly TextMeshProUGUI _timing;
    private readonly List<Cell> _cells = new();
    private ServiceActionOutcomeSnapshot[] _outcomeBuffer;
    private ServiceCyclePumpTimingSample[] _timingBuffer;
    private long _outcomeRevision = -1;
    private long _timingRevision = -1;
    private ActionOutcomeSurfacePresentation _presentation;

    internal ActionOutcomeView(
        RectTransform parent,
        TextMeshProUGUI template,
        IServiceActionOutcomeWindowSource outcomes,
        IServiceCyclePumpTimingSource timings)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
        _timings = timings ?? throw new ArgumentNullException(nameof(timings));
        if (timings.Capacity <= 0)
            throw new ArgumentException("Pump timing source capacity must be positive.", nameof(timings));
        _outcomeBuffer = new ServiceActionOutcomeSnapshot[Math.Max(1, outcomes.ServiceCount)];
        _timingBuffer = new ServiceCyclePumpTimingSample[timings.Capacity];
        _root = ModConfigUiFactory.CreateRectObject(
            "ActionOutcomes",
            parent,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f));
        _rect = (RectTransform)_root.transform;
        _rect.pivot = new Vector2(0.5f, 1f);
        var title = ModConfigUiFactory.CreateText(
            "Title",
            _root.transform,
            new Vector2(0.015f, 1f),
            new Vector2(0.985f, 1f),
            template,
            ActionOutcomeSurfacePresentation.Title,
            TextAlignmentOptions.TopLeft,
            0.82f);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)title.transform, 0f, 32f);
        _timing = ModConfigUiFactory.CreateText(
            "TimingSummary",
            _root.transform,
            new Vector2(0.015f, 1f),
            new Vector2(0.985f, 1f),
            template,
            ActionOutcomeSurfacePresentation.EmptyTiming,
            TextAlignmentOptions.MidlineLeft,
            0.48f,
            TextOverflowModes.Overflow);
        _timing.color = new Color(template.color.r, template.color.g, template.color.b, 0.72f);
        Refresh();
    }

    internal float Layout(float contentWidth, float topOffset, int siblingIndex)
    {
        _ = contentWidth;
        Refresh();
        var rows = Math.Max(1, (_presentation.Rows.Length + 1) / 2);
        var height = HeaderHeight + rows * (CellHeight + RowGap) + TimingHeight;
        _rect.anchoredPosition = new Vector2(0f, -topOffset);
        _rect.sizeDelta = new Vector2(0f, height);
        _root.transform.SetSiblingIndex(siblingIndex);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)_timing.transform,
            height - TimingHeight,
            TimingHeight);
        return height;
    }

    internal void Refresh()
    {
        EnsureCapacity();
        if (_outcomeRevision == _outcomes.Revision && _timingRevision == _timings.Revision) return;
        var outcomes = _outcomes.CopyTo(_outcomeBuffer);
        var timings = _timings.CopyTo(_timingBuffer);
        _outcomeRevision = outcomes.Revision;
        _timingRevision = timings.Revision;
        _presentation = ActionOutcomeSurfacePresentation.Build(
            _outcomeBuffer.AsSpan(0, outcomes.WrittenCount),
            _timingBuffer.AsSpan(0, timings.WrittenCount));
        while (_cells.Count < _presentation.Rows.Length) _cells.Add(new Cell(_rect, _template));
        for (var index = 0; index < _cells.Count; index++)
        {
            var active = index < _presentation.Rows.Length;
            _cells[index].SetActive(active);
            if (active) _cells[index].Render(_presentation.Rows[index], index);
        }
        _timing.text = _presentation.TimingSummary;
    }

    public void Dispose()
    {
        foreach (var cell in _cells) cell.Dispose();
        _cells.Clear();
        UnityEngine.Object.Destroy(_root);
    }

    private void EnsureCapacity()
    {
        if (_outcomeBuffer.Length < _outcomes.ServiceCount)
            _outcomeBuffer = new ServiceActionOutcomeSnapshot[_outcomes.ServiceCount];
        if (_timingBuffer.Length < _timings.Capacity)
            _timingBuffer = new ServiceCyclePumpTimingSample[_timings.Capacity];
    }

    private sealed class Cell : IDisposable
    {
        private static readonly Color CompletedFrame = new(0.08f, 0.22f, 0.16f, 0.98f);
        private static readonly Color QuietIssueFrame = new(0.14f, 0.13f, 0.10f, 0.98f);
        private static readonly Color FaultedFrame = new(0.32f, 0.10f, 0.10f, 0.98f);
        private static readonly Color CompletedSegment = new(0.22f, 0.50f, 0.34f, 1f);
        private static readonly Color SkippedSegment = new(0.25f, 0.27f, 0.31f, 1f);
        private static readonly Color RejectedSegment = new(0.44f, 0.30f, 0.13f, 1f);
        private readonly GameObject _root;
        private readonly RectTransform _rect;
        private readonly Image _frame;
        private readonly TextMeshProUGUI _title;
        private readonly TextMeshProUGUI _body;
        private readonly RectTransform _completed;
        private readonly RectTransform _skipped;
        private readonly RectTransform _rejected;
        private readonly RectTransform _faulted;
        private readonly RectTransform _waiting;

        internal Cell(RectTransform parent, TextMeshProUGUI template)
        {
            _root = ModConfigUiFactory.CreateRectObject(
                "ActionOutcome",
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
                "Service",
                _root.transform,
                new Vector2(0.035f, 0.62f),
                new Vector2(0.965f, 0.94f),
                template,
                string.Empty,
                TextAlignmentOptions.MidlineLeft,
                0.70f);
            _body = ModConfigUiFactory.CreateText(
                "Outcome",
                _root.transform,
                new Vector2(0.035f, 0.20f),
                new Vector2(0.965f, 0.64f),
                template,
                string.Empty,
                TextAlignmentOptions.MidlineLeft,
                0.50f,
                TextOverflowModes.Overflow);
            var track = ModConfigUiFactory.CreateRectObject(
                "OutcomeRail",
                _root.transform,
                new Vector2(0.035f, 0.08f),
                new Vector2(0.965f, 0.16f),
                ModConfigPalette.Bar);
            _completed = Segment("Completed", track.transform, CompletedSegment);
            _skipped = Segment("Skipped", track.transform, SkippedSegment);
            _rejected = Segment("NotCompleted", track.transform, RejectedSegment);
            _faulted = Segment("NeedsAttention", track.transform, ModConfigPalette.Invalid);
            _waiting = Segment("Waiting", track.transform, ModConfigPalette.Button);
        }

        internal void SetActive(bool active) => _root.SetActive(active);

        internal void Render(ActionOutcomeRowPresentation row, int index)
        {
            var column = index % 2;
            var gridRow = index / 2;
            _rect.anchorMin = new Vector2(column == 0 ? 0.01f : 0.505f, 1f);
            _rect.anchorMax = new Vector2(column == 0 ? 0.495f : 0.99f, 1f);
            _rect.anchoredPosition = new Vector2(
                0f,
                -HeaderHeight - gridRow * (CellHeight + RowGap));
            _rect.sizeDelta = new Vector2(0f, CellHeight);
            _title.text = row.DisplayName;
            _body.text = row.Detail.Length == 0 ? row.Summary : row.Summary + "\n" + row.Detail;
            _frame.color = row.Tone switch
            {
                ActionOutcomeTone.Completed => CompletedFrame,
                ActionOutcomeTone.QuietIssue => QuietIssueFrame,
                ActionOutcomeTone.Faulted => FaultedFrame,
                _ => ModConfigPalette.Row,
            };
            RenderRail(row);
        }

        public void Dispose() => UnityEngine.Object.Destroy(_root);

        private void RenderRail(ActionOutcomeRowPresentation row)
        {
            var total = SaturatingAdd(
                SaturatingAdd(row.Committed, row.Skipped),
                SaturatingAdd(row.Rejected, row.Faulted));
            _waiting.gameObject.SetActive(total == 0);
            SetSegment(_waiting, 0f, 1f);
            var start = 0d;
            start = SetSegment(_completed, start, row.Committed, total);
            start = SetSegment(_skipped, start, row.Skipped, total);
            start = SetSegment(_rejected, start, row.Rejected, total);
            _ = SetSegment(_faulted, start, row.Faulted, total);
        }

        private static RectTransform Segment(string name, Transform parent, Color color) =>
            (RectTransform)ModConfigUiFactory.CreateRectObject(
                name,
                parent,
                Vector2.zero,
                Vector2.one,
                color).transform;

        private static double SetSegment(
            RectTransform rect,
            double start,
            long value,
            long total)
        {
            rect.gameObject.SetActive(value > 0 && total > 0);
            var end = total <= 0 ? start : start + value / (double)total;
            SetSegment(rect, (float)start, (float)Math.Min(1d, end));
            return end;
        }

        private static void SetSegment(RectTransform rect, float start, float end)
        {
            rect.anchorMin = new Vector2(start, 0f);
            rect.anchorMax = new Vector2(end, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static long SaturatingAdd(long left, long right) =>
            right > long.MaxValue - left ? long.MaxValue : left + right;
    }
}
