using System;
using OrbModding.Common.Runtime.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class DifferentialVerificationControlView : IDisposable
{
    private const float RowGap = 8f;
    private const float RowInset = 12f;
    private const float MinimumRowHeight = 112f;
    private const string Body =
        "Checks automation calculations against the current game state. " +
        "The result is written to the game log and can be included in a bug report.";
    private readonly IDifferentialVerificationControl _control;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly TextMeshProUGUI _body;
    private readonly TextMeshProUGUI _buttonLabel;
    private readonly Button _button;
    private float _measuredWidth;
    private float _height = MinimumRowHeight;

    internal DifferentialVerificationControlView(
        RectTransform parent,
        TextMeshProUGUI template,
        IDifferentialVerificationControl control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _root = ModConfigUiFactory.CreateRectObject(
            "DifferentialVerification",
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
            "Game math check",
            TextAlignmentOptions.TopLeft,
            0.78f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)title.transform, RowInset, 30f);
        _button = ModConfigUiFactory.CreateButton(
            "VerifyAction",
            _root.transform,
            new Vector2(0.73f, 1f),
            new Vector2(0.982f, 1f),
            template,
            "Check game math",
            OnClicked);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)_button.transform, 8f, 36f);
        _buttonLabel = _button.GetComponentInChildren<TextMeshProUGUI>() ??
            throw new InvalidOperationException("The game-math check button has no label.");
        _body = ModConfigUiFactory.CreateText(
            "Body",
            _root.transform,
            new Vector2(0.018f, 1f),
            new Vector2(0.982f, 1f),
            template,
            Body,
            TextAlignmentOptions.TopLeft,
            0.54f,
            TextOverflowModes.Overflow);
    }

    internal float Layout(float contentWidth, float topOffset, int siblingIndex)
    {
        ApplyAction();
        var width = Math.Max(320f, contentWidth * 0.94f);
        if (ModSettingsLayout.DescriptionWidthChanged(_measuredWidth, width))
        {
            var preferred = _body.GetPreferredValues(Body, width, 0f).y;
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
        _control.RequestRun();
        ApplyAction();
    }

    private void ApplyAction()
    {
        _buttonLabel.text = _control.RunRequested ? "Check queued" : "Check game math";
        _button.interactable = !_control.RunRequested;
    }
}
