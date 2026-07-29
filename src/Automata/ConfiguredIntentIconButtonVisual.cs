using System;
using OrbModConfig;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal enum ConfiguredIntentIconState
{
    Off = 0,
    On = 1,
    Unhealthy = 2,
    Stopped = 3,
    StopReady = 4,
    ResumeArmed = 5,
}

/// <summary>Owns every pixel written by one icon-only quick control.</summary>
internal sealed class ConfiguredIntentIconButtonVisual
{
    private static readonly Color Off = new(0.55f, 0.55f, 0.55f, 1.0f);
    private static readonly Color On = new(0.4f, 1.0f, 0.55f, 1.0f);
    private static readonly Color Unhealthy = new(1.0f, 0.3f, 0.3f, 1.0f);
    private static readonly Color Stopped = new(1.0f, 0.55f, 0.2f, 1.0f);
    private static readonly Color Ready = new(1.0f, 0.78f, 0.28f, 1.0f);
    private static readonly Color Armed = new(0.45f, 0.9f, 1.0f, 1.0f);

    private readonly Image _frame;
    private readonly Image? _icon;
    private readonly TextMeshProUGUI? _symbol;
    private readonly Sprite _baseFrame;
    private ConfiguredIntentIconState? _rendered;

    private ConfiguredIntentIconButtonVisual(
        Image frame,
        Image? icon,
        TextMeshProUGUI? symbol,
        Sprite baseFrame)
    {
        _frame = frame;
        _icon = icon;
        _symbol = symbol;
        _baseFrame = baseFrame;
    }

    internal static bool TryCreateFeature(
        GameObject root,
        Button button,
        Image? icon,
        TextMeshProUGUI? text,
        Sprite? iconSprite,
        out ConfiguredIntentIconButtonVisual? visual,
        out string reason)
    {
        if (iconSprite is null)
        {
            visual = null;
            reason = "feature icon unavailable";
            return false;
        }
        return TryCreate(
            root,
            button,
            icon,
            text,
            iconSprite,
            symbol: null,
            out visual,
            out reason);
    }

    internal static bool TryCreateStop(
        GameObject root,
        Button button,
        Image? icon,
        TextMeshProUGUI? text,
        out ConfiguredIntentIconButtonVisual? visual,
        out string reason) =>
        TryCreate(
            root,
            button,
            icon,
            text,
            iconSprite: null,
            symbol: "×",
            out visual,
            out reason);

    private static bool TryCreate(
        GameObject root,
        Button button,
        Image? icon,
        TextMeshProUGUI? text,
        Sprite? iconSprite,
        string? symbol,
        out ConfiguredIntentIconButtonVisual? visual,
        out string reason)
    {
        visual = null;
        if (!NativeViewAdapter.TryCaptureSpellButtonVisuals(out var native, out reason))
            return false;
        var frame = root.GetComponent<Image>();
        if (frame is null)
        {
            reason = "cloned quick control has no root Image";
            return false;
        }
        ConfiguredIntentButtonVisualOwnership.Claim(button, native!.ImageEffectsType);
        frame.sprite = native.SpellBaseFrame;
        frame.color = Color.white;

        if (icon is not null)
        {
            icon.sprite = iconSprite;
            icon.enabled = iconSprite is not null;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }
        if (text is not null)
        {
            text.text = symbol ?? string.Empty;
            text.enabled = !string.IsNullOrWhiteSpace(symbol);
            text.gameObject.SetActive(text.enabled);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.transform.SetAsLastSibling();
        }

        visual = new ConfiguredIntentIconButtonVisual(
            frame,
            iconSprite is null ? null : icon,
            string.IsNullOrWhiteSpace(symbol) ? null : text,
            native.SpellBaseFrame);
        reason = string.Empty;
        return true;
    }

    internal void Render(ConfiguredIntentIconState state, bool force = false)
    {
        if (!force && _rendered == state) return;
        _rendered = state;
        var color = ColorFor(state);
        _frame.sprite = _baseFrame;
        _frame.color = Color.white;
        if (_icon is not null) _icon.color = color;
        if (_symbol is not null) _symbol.color = color;
    }

    internal static ConfiguredIntentIconState FromFeatureStatus(in FeatureStatusSnapshot status)
    {
        var presentation = FeatureStatusPresenter.Present(status);
        if (!presentation.IsConfiguredOn) return ConfiguredIntentIconState.Off;
        if (AutomataFeatureStatusVisuals.IsEmergencyStopped(status))
            return ConfiguredIntentIconState.Stopped;
        return presentation.RuntimeState is FeatureRuntimePresentationState.Degraded
            or FeatureRuntimePresentationState.Unavailable
            or FeatureRuntimePresentationState.Faulted
            ? ConfiguredIntentIconState.Unhealthy
            : ConfiguredIntentIconState.On;
    }

    private static Color ColorFor(ConfiguredIntentIconState state) => state switch
    {
        ConfiguredIntentIconState.On => On,
        ConfiguredIntentIconState.Unhealthy => Unhealthy,
        ConfiguredIntentIconState.Stopped => Stopped,
        ConfiguredIntentIconState.StopReady => Ready,
        ConfiguredIntentIconState.ResumeArmed => Armed,
        _ => Off,
    };

}
