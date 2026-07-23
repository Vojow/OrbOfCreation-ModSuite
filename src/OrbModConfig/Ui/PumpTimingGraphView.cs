using System;
using System.Globalization;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using TMPro;
using UnityEngine;

namespace OrbModConfig;

internal sealed class PumpTimingGraphView : IDisposable
{
    private const double FramesPerSecondReference = 60d;
    private const float Height = 224f;
    private const float Gap = 8f;
    private readonly IServiceCyclePumpTimingSource _source;
    private readonly ServiceCyclePumpTimingSample[] _samples;
    private readonly long[] _sortedTicks;
    private readonly PumpTimingGraphColumn[] _columns;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly TextMeshProUGUI _summary;
    private readonly PumpTimingGraphGraphic _graphic;
    private long _revision = -1;

    public PumpTimingGraphView(
        RectTransform parent,
        TextMeshProUGUI template,
        IServiceCyclePumpTimingSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (source.Capacity <= 0)
            throw new ArgumentException("Pump timing source capacity must be positive.", nameof(source));
        _samples = new ServiceCyclePumpTimingSample[source.Capacity];
        _sortedTicks = new long[source.Capacity];
        _columns = new PumpTimingGraphColumn[source.Capacity];
        _root = ModConfigUiFactory.CreateRectObject(
            "PumpTiming",
            parent,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f),
            ModConfigPalette.Row);
        _rect = (RectTransform)_root.transform;
        _rect.pivot = new Vector2(0.5f, 1f);
        var title = ModConfigUiFactory.CreateText(
            "Title",
            _root.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.982f, 1f),
            template,
            "Recent ServiceCycle pump time",
            TextAlignmentOptions.TopLeft,
            0.78f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)title.transform, 12f, 30f);
        _summary = ModConfigUiFactory.CreateText(
            "Summary",
            _root.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.982f, 1f),
            template,
            "Waiting for ServiceCycle frames.",
            TextAlignmentOptions.TopLeft,
            0.5f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_summary.transform, 44f, 42f);

        var plot = ModConfigUiFactory.CreateRectObject(
            "Plot",
            _root.transform,
            new Vector2(0.018f, 0.08f),
            new Vector2(0.982f, 0.61f),
            ModConfigPalette.Background);
        var timeline = new GameObject("Timeline", typeof(RectTransform));
        timeline.transform.SetParent(plot.transform, false);
        var timelineRect = (RectTransform)timeline.transform;
        timelineRect.anchorMin = Vector2.zero;
        timelineRect.anchorMax = Vector2.one;
        timelineRect.offsetMin = Vector2.zero;
        timelineRect.offsetMax = Vector2.zero;
        _graphic = timeline.AddComponent<PumpTimingGraphGraphic>();
        _graphic.raycastTarget = false;
        Refresh();
    }

    public float Layout(float contentWidth, float topOffset, int siblingIndex)
    {
        _rect.anchoredPosition = new Vector2(0f, -topOffset);
        _rect.sizeDelta = new Vector2(0f, Height - Gap);
        _root.transform.SetSiblingIndex(siblingIndex);
        Refresh();
        return Height;
    }

    public void Refresh()
    {
        if (_revision == _source.Revision) return;
        var copy = _source.CopyTo(_samples);
        _revision = copy.Revision;
        var count = copy.WrittenCount;
        if (count == 0)
        {
            _summary.text = "Waiting for ServiceCycle frames.";
            _graphic.SetColumns(_columns, 0, 0);
            return;
        }

        long totalTicks = 0;
        long maximumTicks = 0;
        for (var index = 0; index < count; index++)
        {
            var ticks = _samples[index].TotalDuration.Ticks;
            _sortedTicks[index] = ticks;
            totalTicks = AddSaturating(totalTicks, ticks);
            if (ticks > maximumTicks) maximumTicks = ticks;
        }
        Array.Sort(_sortedTicks, 0, count);
        var p95Ticks = _sortedTicks[Math.Max(0, (int)Math.Ceiling(count * 0.95) - 1)];
        var columnCount = PumpTimingGraphProjection.Build(
            _samples.AsSpan(0, count),
            _columns);
        _graphic.SetColumns(_columns, columnCount, maximumTicks);
        _summary.text = string.Format(
            CultureInfo.InvariantCulture,
            "{0} exact frames (~{1:F1}s @ 60 FPS)  |  avg {2:F3} ms  |  p95 {3:F3} ms  |  max/scale {4:F3} ms\nGray idle  ·  Blue capture attempt  ·  Green response  ·  Orange action  ·  all retained frames shown",
            count,
            count / FramesPerSecondReference,
            Milliseconds(totalTicks / (double)count),
            Milliseconds(p95Ticks),
            Milliseconds(maximumTicks));
    }

    public void Dispose() => UnityEngine.Object.Destroy(_root);

    private static double Milliseconds(double ticks) =>
        ticks / TimeSpan.TicksPerMillisecond;

    private static long AddSaturating(long total, long ticks) =>
        ticks > long.MaxValue - total ? long.MaxValue : total + ticks;
}
