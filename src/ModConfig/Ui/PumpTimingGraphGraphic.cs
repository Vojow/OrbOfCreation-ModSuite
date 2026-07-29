using System;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class PumpTimingGraphGraphic : MaskableGraphic
{
    private PumpTimingGraphColumn[] _columns = Array.Empty<PumpTimingGraphColumn>();
    private int _count;
    private long _scaleTicks;

    internal void SetColumns(
        PumpTimingGraphColumn[] columns,
        int count,
        long scaleTicks)
    {
        _columns = columns ?? throw new ArgumentNullException(nameof(columns));
        if (count < 0 || count > columns.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (scaleTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(scaleTicks));
        _count = count;
        _scaleTicks = scaleTicks;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        if (_count == 0) return;

        var bounds = rectTransform.rect;
        var pitch = bounds.width / _count;
        var barWidth = pitch * 0.78f;
        for (var index = 0; index < _count; index++)
        {
            var column = _columns[index];
            var left = bounds.xMin + index * pitch;
            var right = Math.Min(bounds.xMax, left + barWidth);
            var top = bounds.yMin + bounds.height *
                PumpTimingGraphProjection.Height(column.DurationTicks, _scaleTicks);
            AddQuad(
                helper,
                left,
                right,
                bounds.yMin,
                top,
                column.DurationTicks > _scaleTicks ? OverScale : ColorFor(column.Phase));
        }
    }

    private static void AddQuad(
        VertexHelper helper,
        float left,
        float right,
        float bottom,
        float top,
        Color32 color)
    {
        var first = helper.currentVertCount;
        helper.AddVert(new Vector3(left, bottom), color, Vector2.zero);
        helper.AddVert(new Vector3(left, top), color, Vector2.zero);
        helper.AddVert(new Vector3(right, top), color, Vector2.zero);
        helper.AddVert(new Vector3(right, bottom), color, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }

    /// <summary>A frame past the plot's scale, drawn full height so it reads as clipped, not as tied.</summary>
    private static readonly Color OverScale = new(0.78f, 0.29f, 0.24f, 1f);

    private static Color ColorFor(PumpTimingGraphPhase phase) => phase switch
    {
        PumpTimingGraphPhase.Action => ModConfigPalette.ActiveButton,
        PumpTimingGraphPhase.Response => new Color(0.22f, 0.48f, 0.33f, 1f),
        PumpTimingGraphPhase.Capture => new Color(0.20f, 0.34f, 0.52f, 1f),
        _ => ModConfigPalette.Button,
    };
}
