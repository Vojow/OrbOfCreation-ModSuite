using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class HostTraceDumpControlView : IDisposable
{
    private const float RowGap = 8f;
    private const float RowInset = 12f;
    private const float MinimumRowHeight = 112f;
    private readonly IHostTraceDumpControl _control;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly TextMeshProUGUI _body;
    private readonly TextMeshProUGUI _buttonLabel;
    private readonly Button _button;
    private string _bodyValue = string.Empty;
    private float _measuredWidth;
    private float _height = MinimumRowHeight;

    public HostTraceDumpControlView(
        RectTransform parent,
        TextMeshProUGUI template,
        IHostTraceDumpControl control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _root = ModConfigUiFactory.CreateRectObject(
            "HostTraceDump",
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
            new Vector2(0.72f, 1f),
            template,
            "Recent events",
            TextAlignmentOptions.TopLeft,
            0.78f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)title.transform, RowInset, 30f);
        _button = ModConfigUiFactory.CreateButton(
            "DumpAction",
            _root.transform,
            new Vector2(0.73f, 1f),
            new Vector2(0.982f, 1f),
            template,
            "Dump recent events",
            OnClicked);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_button.transform, 8f, 36f);
        _buttonLabel = _button.GetComponentInChildren<TextMeshProUGUI>() ??
            throw new InvalidOperationException("The dump action button has no label.");
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
    }

    public float Layout(float contentWidth, float topOffset, int siblingIndex)
    {
        var presentation = HostTraceDumpPresenter.Build(_control.Status, _control.DumpRequested);
        var contentChanged = !string.Equals(_bodyValue, presentation.Body, StringComparison.Ordinal);
        _bodyValue = presentation.Body;
        ApplyAction(presentation);

        var width = Math.Max(320f, contentWidth * 0.94f);
        if (contentChanged || ModSettingsLayout.DescriptionWidthChanged(_measuredWidth, width))
        {
            _body.text = _bodyValue;
            var preferred = _body.GetPreferredValues(_bodyValue, width, 0f).y;
            _height = Math.Max(MinimumRowHeight, 52f + preferred + RowInset);
            _rect.sizeDelta = new Vector2(0f, _height - RowGap);
            ModConfigUiFactory.SetTopAnchoredHeight(
                (RectTransform)_body.transform,
                48f,
                Math.Max(1f, _height - 58f));
            _measuredWidth = width;
        }

        _rect.anchoredPosition = new Vector2(0f, -topOffset);
        _root.transform.SetSiblingIndex(siblingIndex);
        return _height;
    }

    public void Dispose() => UnityEngine.Object.Destroy(_root);

    private void OnClicked()
    {
        if (_control.RequestDump() == HostTraceDumpRequestResult.Accepted)
            ApplyAction(HostTraceDumpPresenter.Build(_control.Status, _control.DumpRequested));
    }

    private void ApplyAction(HostTraceDumpPresentation presentation)
    {
        _buttonLabel.text = presentation.ButtonLabel;
        _button.interactable = presentation.ButtonEnabled;
    }
}
