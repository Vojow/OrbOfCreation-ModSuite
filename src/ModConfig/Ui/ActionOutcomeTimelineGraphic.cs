using System;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class ActionOutcomeTimelineGraphic : MaskableGraphic
{
    private static readonly Color Baseline = new(0.25f, 0.28f, 0.34f, 0.72f);
    private static readonly Color Selection = new(0.42f, 0.48f, 0.58f, 0.16f);
    private ActionOutcomeBucketPresentation[] _buckets = Array.Empty<ActionOutcomeBucketPresentation>();
    private long _maximumCommitted;
    private int _selectedBucket = -1;

    internal void SetTimeline(
        ActionOutcomeBucketPresentation[] buckets,
        long maximumCommitted)
    {
        _buckets = buckets ?? throw new ArgumentNullException(nameof(buckets));
        if (maximumCommitted < 0) throw new ArgumentOutOfRangeException(nameof(maximumCommitted));
        _maximumCommitted = maximumCommitted;
        SetVerticesDirty();
    }

    internal void SetSelectedBucket(int bucketIndex)
    {
        if (bucketIndex < -1 || bucketIndex >= _buckets.Length)
            throw new ArgumentOutOfRangeException(nameof(bucketIndex));
        if (_selectedBucket == bucketIndex) return;
        _selectedBucket = bucketIndex;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        if (_buckets.Length == 0) return;

        var bounds = rectTransform.rect;
        var pitch = bounds.width / _buckets.Length;
        var width = Math.Max(1f, pitch * 0.68f);
        var baseline = bounds.yMin + 8f;
        var plotHeight = Math.Max(1f, bounds.height - 13f);
        for (var bucketIndex = 0; bucketIndex < _buckets.Length; bucketIndex++)
        {
            var bucket = _buckets[bucketIndex];
            var center = bounds.xMin + (bucketIndex + 0.5f) * pitch;
            var left = center - width * 0.5f;
            var right = center + width * 0.5f;
            if (bucketIndex == _selectedBucket)
                AddQuad(
                    helper,
                    center - pitch * 0.47f,
                    center + pitch * 0.47f,
                    bounds.yMin + 3f,
                    bounds.yMax - 2f,
                    Selection);
            AddQuad(helper, left, right, baseline - 1f, baseline, Baseline);

            var bottom = baseline;
            for (var stackIndex = 0; stackIndex < bucket.Stacks.Length; stackIndex++)
            {
                var stack = bucket.Stacks[stackIndex];
                var height = _maximumCommitted <= 0
                    ? 0f
                    : plotHeight * (float)(stack.Committed / (double)_maximumCommitted);
                var top = Math.Min(bounds.yMax - 4f, bottom + height);
                AddQuad(helper, left, right, bottom, top, ColorFor(stack.Color));
                bottom = top;
            }

            if (bucket.HasFault)
                AddFaultMarker(helper, center, baseline - 1f);
        }
    }

    internal static Color ColorFor(ActionOutcomeServiceColor color) => color switch
    {
        ActionOutcomeServiceColor.Leaf => new Color(0.32f, 0.64f, 0.42f, 1f),
        ActionOutcomeServiceColor.Amber => new Color(0.82f, 0.61f, 0.24f, 1f),
        ActionOutcomeServiceColor.Sky => new Color(0.34f, 0.58f, 0.82f, 1f),
        ActionOutcomeServiceColor.Violet => new Color(0.58f, 0.43f, 0.78f, 1f),
        ActionOutcomeServiceColor.Cyan => new Color(0.28f, 0.65f, 0.68f, 1f),
        ActionOutcomeServiceColor.Orange => new Color(0.82f, 0.45f, 0.24f, 1f),
        ActionOutcomeServiceColor.Rose => new Color(0.76f, 0.39f, 0.55f, 1f),
        ActionOutcomeServiceColor.Teal => new Color(0.27f, 0.55f, 0.52f, 1f),
        _ => ModConfigPalette.Button,
    };

    private static void AddFaultMarker(VertexHelper helper, float center, float baseline)
    {
        const float halfWidth = 4.5f;
        const float height = 7f;
        var first = helper.currentVertCount;
        helper.AddVert(new Vector3(center - halfWidth, baseline), ModConfigPalette.Invalid, Vector2.zero);
        helper.AddVert(new Vector3(center + halfWidth, baseline), ModConfigPalette.Invalid, Vector2.zero);
        helper.AddVert(new Vector3(center, baseline + height), ModConfigPalette.Invalid, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);
    }

    private static void AddQuad(
        VertexHelper helper,
        float left,
        float right,
        float bottom,
        float top,
        Color32 color)
    {
        if (top <= bottom || right <= left) return;
        var first = helper.currentVertCount;
        helper.AddVert(new Vector3(left, bottom), color, Vector2.zero);
        helper.AddVert(new Vector3(left, top), color, Vector2.zero);
        helper.AddVert(new Vector3(right, top), color, Vector2.zero);
        helper.AddVert(new Vector3(right, bottom), color, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }
}
