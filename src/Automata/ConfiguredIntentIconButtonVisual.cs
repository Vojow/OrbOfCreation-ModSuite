using System;
using System.Collections.Generic;
using System.Linq;
using OrbModConfig;
using OrbModding.Common;
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
}

internal enum ConfiguredIntentFrameTreatment
{
    InactiveRecessed = 0,
    ActiveRaised = 1,
}

internal readonly record struct ConfiguredIntentPresentation(
    ConfiguredIntentIconState State,
    ConfiguredIntentFrameTreatment FrameTreatment,
    Color Color,
    string TooltipLabel);

/// <summary>
/// Owns every pixel written by one suite-created quick control. The inactive/active frame pair is
/// the audited <c>UIViewRadioButton.baseImage</c>/<c>activeImage</c> vocabulary; color is secondary.
/// </summary>
internal sealed class ConfiguredIntentIconButtonVisual
{
    internal static readonly Color OffColor = new(0.55f, 0.55f, 0.55f, 1.0f);
    internal static readonly Color OnColor = new(0.4f, 1.0f, 0.55f, 1.0f);
    internal static readonly Color UnhealthyColor = new(1.0f, 0.3f, 0.3f, 1.0f);
    internal static readonly Color StoppedColor = new(1.0f, 0.55f, 0.2f, 1.0f);
    internal static readonly Color ReadyColor = new(1.0f, 0.78f, 0.28f, 1.0f);

    private readonly Image _frame;
    private readonly IReadOnlyList<Image> _glyphs;
    private readonly Sprite _inactiveFrame;
    private readonly Sprite _activeFrame;
    private ConfiguredIntentPresentation? _rendered;

    private ConfiguredIntentIconButtonVisual(
        Image frame,
        IReadOnlyList<Image> glyphs,
        Sprite inactiveFrame,
        Sprite activeFrame)
    {
        _frame = frame;
        _glyphs = glyphs;
        _inactiveFrame = inactiveFrame;
        _activeFrame = activeFrame;
    }

    internal ConfiguredIntentPresentation? Rendered => _rendered;

    internal static bool TryCreate(
        GameObject root,
        Button button,
        IEnumerable<Image> glyphs,
        NativeButtonStateVisualPrimitives? stateVisuals,
        out ConfiguredIntentIconButtonVisual? visual,
        out string reason)
    {
        visual = null;
        if (root is null)
        {
            reason = "quick control root is unavailable";
            return false;
        }
        if (button is null)
        {
            reason = "quick control has no Unity Button";
            return false;
        }
        if (stateVisuals?.InactiveFrame is null || stateVisuals.ActiveFrame is null)
        {
            reason =
                "audited UIViewRadioButton inactive/active state frame pair is unconstructible";
            return false;
        }
        var frame = root.GetComponent<Image>();
        if (frame is null)
        {
            reason = "suite-owned quick control has no root Image";
            return false;
        }
        var ownedGlyphs = (glyphs ?? throw new ArgumentNullException(nameof(glyphs)))
            .Where(image => image is not null)
            .ToArray();
        if (ownedGlyphs.Length == 0)
        {
            reason = "suite-owned quick control has no state-colored glyph";
            return false;
        }

        ConfiguredIntentButtonVisualOwnership.Claim(button);
        frame.type = Image.Type.Sliced;
        frame.color = Color.white;
        frame.raycastTarget = true;
        foreach (var glyph in ownedGlyphs)
        {
            glyph.raycastTarget = false;
            glyph.preserveAspect = true;
        }
        visual = new ConfiguredIntentIconButtonVisual(
            frame,
            ownedGlyphs,
            stateVisuals.InactiveFrame,
            stateVisuals.ActiveFrame);
        reason = string.Empty;
        return true;
    }

    internal void Render(ConfiguredIntentPresentation presentation, bool force = false)
    {
        if (!force && _rendered == presentation) return;
        _rendered = presentation;
        _frame.sprite = presentation.FrameTreatment == ConfiguredIntentFrameTreatment.ActiveRaised
            ? _activeFrame
            : _inactiveFrame;
        _frame.color = Color.white;
        foreach (var glyph in _glyphs) glyph.color = presentation.Color;
    }

    internal static ConfiguredIntentPresentation FromFeatureStatus(
        in FeatureStatusSnapshot status)
    {
        var presentation = FeatureStatusPresenter.Present(status);
        if (!presentation.IsConfiguredOn)
            return Present(
                ConfiguredIntentIconState.Off,
                ConfiguredIntentFrameTreatment.InactiveRecessed,
                OffColor,
                "OFF");
        if (AutomataFeatureStatusVisuals.IsEmergencyStopped(status))
            return Present(
                ConfiguredIntentIconState.Stopped,
                ConfiguredIntentFrameTreatment.ActiveRaised,
                StoppedColor,
                "ON / STOPPED");
        return presentation.RuntimeState is FeatureRuntimePresentationState.Degraded
            or FeatureRuntimePresentationState.Unavailable
            or FeatureRuntimePresentationState.Faulted
            ? Present(
                ConfiguredIntentIconState.Unhealthy,
                ConfiguredIntentFrameTreatment.ActiveRaised,
                UnhealthyColor,
                "ON / FAULTED")
            : Present(
                ConfiguredIntentIconState.On,
                ConfiguredIntentFrameTreatment.ActiveRaised,
                OnColor,
                "ON / OPERATIONAL");
    }

    internal static ConfiguredIntentPresentation FromEmergencyStop(
        EmergencyStopControl control)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        return control.IsStopped
            ? Present(
                ConfiguredIntentIconState.Stopped,
                ConfiguredIntentFrameTreatment.ActiveRaised,
                StoppedColor,
                "STOPPED")
            : Present(
                ConfiguredIntentIconState.StopReady,
                ConfiguredIntentFrameTreatment.InactiveRecessed,
                ReadyColor,
                "READY / STOP ALL");
    }

    internal static string TooltipLabelFor(in FeatureStatusSnapshot status) =>
        FromFeatureStatus(status).TooltipLabel;

    private static ConfiguredIntentPresentation Present(
        ConfiguredIntentIconState state,
        ConfiguredIntentFrameTreatment frame,
        Color color,
        string tooltipLabel) =>
        new(state, frame, color, tooltipLabel);
}
