using System;
using System.Collections.Generic;
using System.Linq;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.UI;

namespace OrbAutomata;

internal delegate bool QuickControlIconResolver(
    AutomationFeatureControlRegistration registration,
    out Sprite? icon,
    out string reason);

/// <summary>
/// Suite-owned, single-column gameplay controls under the audited native HelpButtons anchor.
/// Every live entry has already acquired both structural state frames and its complete glyph.
/// </summary>
internal sealed class QuickControlColumn : IDisposable
{
    internal const string ObjectName = "OrbModSuite.QuickControls";
    internal const string EmergencyStopId = "emergency-stop";
    internal const float ControlSize = 52f;
    internal const float Spacing = 6f;
    internal const float SafetyGap = 12f;
    internal const float AnchorOffsetY = -158f;

    private readonly GameObject _root;
    private readonly Dictionary<string, Entry> _entries;
    private readonly IReadOnlyDictionary<string, string> _failures;
    private bool _disposed;

    private QuickControlColumn(
        GameObject root,
        Dictionary<string, Entry> entries,
        IReadOnlyDictionary<string, string> failures)
    {
        _root = root;
        _entries = entries;
        _failures = failures;
    }

    internal bool IsAlive => !_disposed && _root != null;
    internal IReadOnlyCollection<string> ControlIds => _entries.Keys;
    internal IReadOnlyDictionary<string, string> Failures => _failures;

    internal static bool TryCreate(
        AutomationFeatureControlRegistry registry,
        EmergencyStopControl emergencyStop,
        QuickControlNativePrimitives? native,
        bool allowFeatureControls,
        out QuickControlColumn? column,
        out string reason,
        QuickControlIconResolver? resolveIcon = null)
    {
        column = null;
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (emergencyStop is null) throw new ArgumentNullException(nameof(emergencyStop));
        if (native?.Anchor is null)
        {
            reason = "audited top-left HelpButtons anchor is unavailable";
            return false;
        }
        if (native.StateVisuals?.InactiveFrame is null ||
            native.StateVisuals.ActiveFrame is null)
        {
            reason =
                "audited UIViewRadioButton inactive/active state frame pair is unconstructible";
            return false;
        }

        resolveIcon ??= ResolveNativeIcon;
        GameObject? root = null;
        try
        {
            root = new GameObject(ObjectName);
            root.SetActive(false);
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(native.Anchor, false);
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, AnchorOffsetY);

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var failures = new Dictionary<string, string>(StringComparer.Ordinal);
            var slot = 0;
            if (allowFeatureControls)
            {
                foreach (var registration in registry.Features)
                {
                    if (!resolveIcon(registration, out var icon, out var iconReason) ||
                        icon is null)
                    {
                        failures[registration.FeatureId] =
                            $"{registration.DisplayName}: audited sprite unavailable: {iconReason}";
                        slot++;
                        continue;
                    }
                    if (!TryCreateFeatureEntry(
                            rootRect,
                            slot++,
                            registration,
                            icon,
                            native.StateVisuals,
                            out var entry,
                            out var entryReason))
                    {
                        failures[registration.FeatureId] =
                            $"{registration.DisplayName}: state visual unavailable: {entryReason}";
                        continue;
                    }
                    entries.Add(registration.FeatureId, entry!);
                }
            }

            var stopSlot = allowFeatureControls ? registry.Features.Count : 0;
            if (!TryCreateEmergencyEntry(
                    rootRect,
                    stopSlot,
                    emergencyStop,
                    native.StateVisuals,
                    out var stopEntry,
                    out var stopReason))
            {
                failures[EmergencyStopId] =
                    "Suite emergency stop: state visual unavailable: " + stopReason;
            }
            else
            {
                entries.Add(EmergencyStopId, stopEntry!);
            }

            var featureHeight = allowFeatureControls
                ? registry.Features.Count * (ControlSize + Spacing)
                : 0f;
            rootRect.sizeDelta = new Vector2(
                ControlSize,
                featureHeight + ControlSize + (allowFeatureControls ? SafetyGap : 0f));
            column = new QuickControlColumn(root, entries, failures);
            column.Render(force: true);
            root.SetActive(true);
            reason = failures.Count == 0
                ? string.Empty
                : string.Join(
                    "; ",
                    failures.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => pair.Value));
            return true;
        }
        catch (Exception ex)
        {
            if (root is not null) UnityEngine.Object.Destroy(root);
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    internal void Render(bool force = false)
    {
        if (!IsAlive) return;
        foreach (var entry in _entries.Values) entry.Render(force);
    }

    internal bool TryGetButton(string controlId, out Button button)
    {
        if (_entries.TryGetValue(controlId, out var entry))
        {
            button = entry.Button;
            return true;
        }
        button = null!;
        return false;
    }

    internal bool TryGetPresentation(
        string controlId,
        out ConfiguredIntentPresentation presentation)
    {
        if (_entries.TryGetValue(controlId, out var entry) &&
            entry.Visual.Rendered is { } rendered)
        {
            presentation = rendered;
            return true;
        }
        presentation = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries.Values) entry.Button.onClick.RemoveAllListeners();
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    private static bool TryCreateFeatureEntry(
        RectTransform parent,
        int slot,
        AutomationFeatureControlRegistration registration,
        Sprite iconSprite,
        NativeButtonStateVisualPrimitives stateVisuals,
        out Entry? entry,
        out string reason)
    {
        var root = CreateControlObject(
            "Feature." + registration.FeatureId,
            parent,
            PositionFor(slot, safety: false));
        var button = root.AddComponent<Button>();
        var iconObject = CreateGlyphObject(
            "Icon",
            root.transform,
            new Vector2(0.16f, 0.16f),
            new Vector2(0.84f, 0.84f));
        var icon = iconObject.AddComponent<Image>();
        icon.sprite = iconSprite;
        if (!ConfiguredIntentIconButtonVisual.TryCreate(
                root,
                button,
                new[] { icon },
                stateVisuals,
                out var visual,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        root.AddComponent<HoverTooltip>().Setup(registration.Tooltip);
        Entry? created = null;
        created = new Entry(
            button,
            visual!,
            () => ConfiguredIntentIconButtonVisual.FromFeatureStatus(registration.Status));
        button.onClick.AddListener(() =>
        {
            registration.Toggle();
            created.Render(force: true);
        });
        entry = created;
        return true;
    }

    private static bool TryCreateEmergencyEntry(
        RectTransform parent,
        int slot,
        EmergencyStopControl control,
        NativeButtonStateVisualPrimitives stateVisuals,
        out Entry? entry,
        out string reason)
    {
        var root = CreateControlObject(
            "Safety.EmergencyStop",
            parent,
            PositionFor(slot, safety: slot > 0));
        var button = root.AddComponent<Button>();
        var barObject = CreateGlyphObject(
            "ExclamationBar",
            root.transform,
            new Vector2(0.44f, 0.27f),
            new Vector2(0.56f, 0.73f));
        var dotObject = CreateGlyphObject(
            "ExclamationDot",
            root.transform,
            new Vector2(0.42f, 0.13f),
            new Vector2(0.58f, 0.24f));
        var bar = barObject.AddComponent<Image>();
        var dot = dotObject.AddComponent<Image>();
        if (!ConfiguredIntentIconButtonVisual.TryCreate(
                root,
                button,
                new[] { bar, dot },
                stateVisuals,
                out var visual,
                out reason))
        {
            UnityEngine.Object.Destroy(root);
            entry = null;
            return false;
        }
        root.AddComponent<HoverTooltip>().Setup(new EmergencyStopTooltip(control));
        Entry? created = null;
        created = new Entry(
            button,
            visual!,
            () => ConfiguredIntentIconButtonVisual.FromEmergencyStop(control));
        button.onClick.AddListener(() =>
        {
            control.Activate();
            created.Render(force: true);
        });
        entry = created;
        return true;
    }

    private static GameObject CreateControlObject(
        string name,
        Transform parent,
        Vector2 position)
    {
        var root = new GameObject(name);
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(ControlSize, ControlSize);
        root.AddComponent<Image>();
        return root;
    }

    private static GameObject CreateGlyphObject(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        var glyph = new GameObject(name);
        var rect = (RectTransform)glyph.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return glyph;
    }

    private static Vector2 PositionFor(int slot, bool safety)
    {
        var gap = safety ? SafetyGap : 0f;
        return new Vector2(0f, -(slot * (ControlSize + Spacing) + gap));
    }

    private static bool ResolveNativeIcon(
        AutomationFeatureControlRegistration registration,
        out Sprite? icon,
        out string reason) =>
        NativeFeatureIconResolver.TryResolve(
            registration.PageLabel,
            capturedRail: null,
            out icon,
            out reason);

    private sealed class Entry
    {
        private readonly Func<ConfiguredIntentPresentation> _readPresentation;

        internal Entry(
            Button button,
            ConfiguredIntentIconButtonVisual visual,
            Func<ConfiguredIntentPresentation> readPresentation)
        {
            Button = button;
            Visual = visual;
            _readPresentation = readPresentation;
        }

        internal Button Button { get; }
        internal ConfiguredIntentIconButtonVisual Visual { get; }
        internal void Render(bool force) => Visual.Render(_readPresentation(), force);
    }
}
