using System;
using TMPro;
using UnityEngine;

namespace OrbModConfig;

internal sealed class RuntimeDiagnosticsCardView : IDisposable
{
    private const float RowGap = 8f;
    private const float RowInset = 12f;
    private const float MinimumRowHeight = 112f;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly TextMeshProUGUI _title;
    private readonly TextMeshProUGUI _body;
    private RuntimeDiagnosticsCard? _card;
    private long _cardRevision;
    private string _titleValue = string.Empty;
    private string _bodyValue = string.Empty;
    private float _measuredWidth;
    private float _height = MinimumRowHeight;

    public RuntimeDiagnosticsCardView(RectTransform parent, TextMeshProUGUI template)
    {
        _root = ModConfigUiFactory.CreateRectObject(
            "RuntimeCard",
            parent,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f),
            ModConfigPalette.Row);
        _rect = (RectTransform)_root.transform;
        _rect.pivot = new Vector2(0.5f, 1f);
        _title = ModConfigUiFactory.CreateText(
            "Title",
            _root.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.982f, 1f),
            template,
            string.Empty,
            TextAlignmentOptions.TopLeft,
            0.78f,
            TextOverflowModes.Overflow);
        _body = ModConfigUiFactory.CreateText(
            "Body",
            _root.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.982f, 1f),
            template,
            string.Empty,
            TextAlignmentOptions.TopLeft,
            0.54f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_title.transform, RowInset, 30f);
    }

    public int RenderGeneration { get; private set; }

    public float LayoutCard(
        RuntimeDiagnosticsCard card,
        float contentWidth,
        float topOffset,
        int siblingIndex,
        int renderGeneration)
    {
        var cardChanged = !ReferenceEquals(_card, card) || _cardRevision != card.Revision;
        if (cardChanged)
        {
            _card = card;
            _cardRevision = card.Revision;
            _titleValue = RuntimeDiagnosticsCardText.Title(card);
            _bodyValue = RuntimeDiagnosticsCardText.Body(card);
        }
        return Layout(contentWidth, topOffset, siblingIndex, renderGeneration, cardChanged);
    }

    public float LayoutStatic(
        string title,
        string body,
        float contentWidth,
        float topOffset,
        int siblingIndex,
        int renderGeneration)
    {
        _card = null;
        _cardRevision = 0;
        var changed = !string.Equals(_titleValue, title, StringComparison.Ordinal) ||
            !string.Equals(_bodyValue, body, StringComparison.Ordinal);
        _titleValue = title;
        _bodyValue = body;
        return Layout(contentWidth, topOffset, siblingIndex, renderGeneration, changed);
    }

    public void Dispose() => UnityEngine.Object.Destroy(_root);

    private float Layout(
        float contentWidth,
        float topOffset,
        int siblingIndex,
        int renderGeneration,
        bool contentChanged)
    {
        RenderGeneration = renderGeneration;
        var width = Math.Max(320f, contentWidth * 0.94f);
        if (contentChanged || ModSettingsLayout.DescriptionWidthChanged(_measuredWidth, width))
        {
            _title.text = _titleValue;
            _body.text = _bodyValue;
            var preferred = _body.GetPreferredValues(_bodyValue, width, 0f).y;
            _height = Math.Max(MinimumRowHeight, 52f + preferred + RowInset);
            _rect.sizeDelta = new Vector2(0f, _height - RowGap);
            ModConfigUiFactory.SetTopAnchoredHeight(
                (RectTransform)_body.transform,
                44f,
                Math.Max(1f, _height - 54f));
            _measuredWidth = width;
        }
        _rect.anchoredPosition = new Vector2(0f, -topOffset);
        _root.transform.SetSiblingIndex(siblingIndex);
        return _height;
    }
}
