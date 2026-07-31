using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrbModConfig;

internal enum ModConfigStartStatusTone
{
    Ready = 0,
    Attention = 1,
    Failure = 2,
}

internal sealed class ModConfigStartStatusPresentation
{
    internal ModConfigStartStatusPresentation(
        ModConfigStartStatusTone tone,
        params string[] rows)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if (rows.Length is < 3 or > 5)
            throw new ArgumentException("A Start status card requires three to five visible rows.", nameof(rows));
        if (rows.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A visible Start status row cannot be blank.", nameof(rows));
        Rows = rows.ToArray();
        Tone = tone;
    }

    internal IReadOnlyList<string> Rows { get; }
    internal ModConfigStartStatusTone Tone { get; }
}

internal static class ModConfigStartStatusPresenter
{
#if SERVICE_CYCLE_PROFILE
    internal static ModConfigStartStatusPresentation Build(
        string releaseVersion,
        bool controlPlaneReady,
        bool auditedBuild,
        bool runtimeActivationAllowed,
        AutomaticSaveBackupStatus saveBackup,
        bool gameMcpServerReady,
        int processId)
    {
        var compatibility = Compatibility(
            controlPlaneReady,
            auditedBuild,
            runtimeActivationAllowed);
        var mcpStatus = !controlPlaneReady
            ? "MCP unavailable"
            : gameMcpServerReady
                ? "MCP ready"
                : "MCP starting";
        var endpoint = gameMcpServerReady
            ? "Agent: 127.0.0.1:19106/mcp"
            : "Agent endpoint unavailable · see log";
        var tone = Tone(
            controlPlaneReady,
            auditedBuild,
            runtimeActivationAllowed,
            saveBackup,
            gameMcpServerReady);
        return new ModConfigStartStatusPresentation(
            tone,
            Headline(releaseVersion),
            "Performance-debug build",
            mcpStatus + "  ·  " + compatibility,
            AutomaticSaveBackupWording.StartSummary(saveBackup),
            endpoint + "  ·  PID " + processId + "  ·  Localhost only");
    }
#else
    internal static ModConfigStartStatusPresentation Build(
        string releaseVersion,
        bool controlPlaneReady,
        bool auditedBuild,
        bool runtimeActivationAllowed,
        AutomaticSaveBackupStatus saveBackup)
    {
        var tone = Tone(
            controlPlaneReady,
            auditedBuild,
            runtimeActivationAllowed,
            saveBackup,
            auxiliarySurfaceReady: true);
        return new ModConfigStartStatusPresentation(
            tone,
            Headline(releaseVersion),
            "Release build",
            Compatibility(controlPlaneReady, auditedBuild, runtimeActivationAllowed),
            AutomaticSaveBackupWording.StartSummary(saveBackup));
    }
#endif

    private static ModConfigStartStatusTone Tone(
        bool controlPlaneReady,
        bool auditedBuild,
        bool runtimeActivationAllowed,
        AutomaticSaveBackupStatus saveBackup,
        bool auxiliarySurfaceReady)
    {
        if (!controlPlaneReady || !saveBackup.AllowsAutomation)
            return ModConfigStartStatusTone.Failure;
        if (saveBackup.HasRetentionFailure)
            return ModConfigStartStatusTone.Attention;
        return auditedBuild && runtimeActivationAllowed && auxiliarySurfaceReady
            ? ModConfigStartStatusTone.Ready
            : ModConfigStartStatusTone.Attention;
    }

    private static string Headline(string releaseVersion) =>
        "Orb ModSuite  ·  v" + (releaseVersion ?? string.Empty);

    private static string Compatibility(
        bool controlPlaneReady,
        bool auditedBuild,
        bool runtimeActivationAllowed) =>
        !controlPlaneReady
            ? "Control-plane error · see log"
            : auditedBuild
                ? "Audited game verified"
                : runtimeActivationAllowed
                    ? "Unverified game accepted"
                    : "Unverified game · actions blocked";
}

/// <summary>
/// Owns the suite's title-screen identity card. Every visible primitive is suite-owned, while the
/// frame sprites, colors, font, and spacing vocabulary come from exact active Start-scene controls.
/// </summary>
internal sealed class ModConfigStartStatusView : IDisposable
{
    internal const string RootObjectName = "OrbModSuite.StartStatus";
    private const string NativeTitlePanelPath = "Canvas/Panel/Viewport/SaveSlot";

    private GameObject? _root;
    private Image? _statusFrame;
    private TextMeshProUGUI? _headline;
    private TextMeshProUGUI? _mode;
    private readonly List<TextMeshProUGUI> _detailRows = new();
    private TextMeshProUGUI? _nativeBodyText;
    private NativeStartCardPalette? _native;

    internal bool IsAlive =>
        _root is not null &&
        _root.activeInHierarchy;

    internal string NativeVersionPath { get; private set; } = string.Empty;

    internal bool TryRender(
        ModConfigStartStatusPresentation presentation,
        out string reason)
    {
        if (SceneManager.GetActiveScene().name != "Start")
        {
            Dispose();
            reason = "the native Start scene is not active";
            return false;
        }
        if (!IsAlive && !TryCreate(out reason)) return false;

        var frame = presentation.Tone switch
        {
            ModConfigStartStatusTone.Failure => _native!.Failure,
            ModConfigStartStatusTone.Attention => _native!.Attention,
            _ => _native!.Ready,
        };
        _statusFrame!.sprite = frame.Sprite;
        _statusFrame.color = frame.Color;
        _mode!.color = frame.TextColor;
        _headline!.text = presentation.Rows[0];
        _mode.text = presentation.Rows[1];
        RenderDetailRows(presentation.Rows);
        reason = string.Empty;
        return true;
    }

    private bool TryCreate(out string reason)
    {
        Dispose();
        var versionLabels = ActiveStartLabels()
            .Where(label => IsNativeVersionText(label.text))
            .ToArray();
        if (versionLabels.Length != 1)
        {
            reason =
                "expected exactly one active native Start version label but found " +
                versionLabels.Length;
            return false;
        }
        var canvas = GameObject.Find("Canvas");
        if (canvas is null || !canvas.activeInHierarchy)
        {
            reason = "the native Start Canvas is unavailable";
            return false;
        }
        if (!TryCaptureNativeVisuals(versionLabels[0], out var native, out reason))
            return false;

        NativeVersionPath = NativeObjectPath.Build(versionLabels[0]);
        RemoveOwnedChild(canvas.transform);
        _root = ModConfigUiFactory.CreateRectObject(
            RootObjectName,
            canvas.transform,
            Vector2.one,
            Vector2.one,
            native.Panel.Color);
        var panel = _root.GetComponent<Image>()!;
        panel.sprite = native.Panel.Sprite;
        panel.type = Image.Type.Sliced;
        panel.raycastTarget = false;
        var rootRect = (RectTransform)_root.transform;
        rootRect.pivot = Vector2.one;
        rootRect.anchoredPosition = new Vector2(-24f, -82f);
        rootRect.sizeDelta = new Vector2(470f, 248f);
        rootRect.SetAsLastSibling();

        _statusFrame = ModConfigUiFactory.CreateRectObject(
                "StatusFrame",
                _root.transform,
                new Vector2(0.055f, 0.49f),
                new Vector2(0.945f, 0.75f),
                native.Ready.Color)
            .GetComponent<Image>()!;
        _statusFrame.sprite = native.Ready.Sprite;
        _statusFrame.type = Image.Type.Sliced;
        _statusFrame.raycastTarget = false;

        _headline = CreateText(
            "Headline",
            new Vector2(0.065f, 0.78f),
            new Vector2(0.935f, 0.94f),
            native.BodyText,
            0.82f);
        _mode = CreateText(
            "Mode",
            new Vector2(0.085f, 0.515f),
            new Vector2(0.915f, 0.725f),
            native.ReadyText,
            0.58f,
            TextAlignmentOptions.Midline);
        _nativeBodyText = native.BodyText;
        _native = native.Palette;
        reason = string.Empty;
        return true;
    }

    private void RenderDetailRows(IReadOnlyList<string> rows)
    {
        var detailCount = rows.Count - 2;
        while (_detailRows.Count > detailCount)
        {
            var lastIndex = _detailRows.Count - 1;
            UnityEngine.Object.Destroy(_detailRows[lastIndex].gameObject);
            _detailRows.RemoveAt(lastIndex);
        }
        while (_detailRows.Count < detailCount)
        {
            var index = _detailRows.Count;
            _detailRows.Add(CreateText(
                DetailName(index),
                DetailAnchorMin(index),
                DetailAnchorMax(index),
                _nativeBodyText!,
                DetailSizeScale(index)));
        }
        for (var index = 0; index < detailCount; index++)
            _detailRows[index].text = rows[index + 2];
    }

    private static string DetailName(int index) => index switch
    {
        0 => "Health",
        1 => "Endpoint",
        _ => "Process",
    };

    private static Vector2 DetailAnchorMin(int index) => index switch
    {
        0 => new Vector2(0.07f, 0.32f),
        1 => new Vector2(0.07f, 0.17f),
        _ => new Vector2(0.07f, 0.04f),
    };

    private static Vector2 DetailAnchorMax(int index) => index switch
    {
        0 => new Vector2(0.93f, 0.47f),
        1 => new Vector2(0.93f, 0.32f),
        _ => new Vector2(0.93f, 0.18f),
    };

    private static float DetailSizeScale(int index) => index switch
    {
        0 => 0.72f,
        1 => 0.62f,
        _ => 0.58f,
    };

    private TextMeshProUGUI CreateText(
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextMeshProUGUI template,
        float sizeScale,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft) =>
        ModConfigUiFactory.CreateText(
            name,
            _root!.transform,
            anchorMin,
            anchorMax,
            template,
            string.Empty,
            alignment,
            sizeScale,
            TextOverflowModes.Ellipsis);

    private static bool TryCaptureNativeVisuals(
        TextMeshProUGUI version,
        out NativeStartCardVisuals visuals,
        out string reason)
    {
        visuals = null!;
        if (!TryCapturePanelFrame(out var panel, out reason) ||
            !TryCaptureButtonFrame(
                "Continue",
                out var ready,
                out var readyText,
                out reason) ||
            !TryCaptureButtonFrame(
                "Export Save",
                out var attention,
                out _,
                out reason) ||
            !TryCaptureButtonFrame(
                "Quit",
                out var failure,
                out _,
                out reason))
        {
            return false;
        }
        visuals = new NativeStartCardVisuals(
            panel,
            ready,
            attention,
            failure,
            version,
            readyText);
        reason = string.Empty;
        return true;
    }

    private static bool TryCapturePanelFrame(
        out NativeStartFrame panel,
        out string reason)
    {
        panel = default;
        var panelObject = GameObject.Find(NativeTitlePanelPath);
        var image = panelObject?.GetComponent<Image>();
        if (panelObject is null ||
            !panelObject.activeInHierarchy ||
            !string.Equals(panelObject.scene.name, "Start", StringComparison.Ordinal) ||
            image is null ||
            image.sprite is null ||
            image.type != Image.Type.Sliced)
        {
            reason = "native title panel capture failed: exact active object '" +
                NativeTitlePanelPath +
                "' is not a Start-scene sliced Image with a non-null sprite";
            return false;
        }
        panel = new NativeStartFrame(image.sprite, image.color, Color.white);
        reason = string.Empty;
        return true;
    }

    private static bool TryCaptureButtonFrame(
        string labelText,
        out NativeStartFrame frame,
        out TextMeshProUGUI text,
        out string reason)
    {
        frame = default;
        text = null!;
        var labels = FindExactLabels(labelText)
            .Where(label => IsMainTitleControl(label))
            .ToArray();
        var candidates = new List<NativeButtonCandidate>();
        for (var labelIndex = 0; labelIndex < labels.Length; labelIndex++)
        {
            var label = labels[labelIndex];
            Button? button = null;
            for (var current = label.transform;
                 current is not null;
                 current = current.parent)
            {
                button = current.GetComponent<Button>();
                if (button is not null) break;
            }
            var image = button?.targetGraphic as Image ?? button?.GetComponent<Image>();
            if (button is null ||
                image is null ||
                image.sprite is null ||
                image.type != Image.Type.Sliced)
            {
                continue;
            }

            var existing = -1;
            for (var index = 0; index < candidates.Count; index++)
            {
                if (!ReferenceEquals(candidates[index].Button, button)) continue;
                existing = index;
                break;
            }
            var candidate = new NativeButtonCandidate(button, image, label);
            if (existing < 0)
            {
                candidates.Add(candidate);
            }
            else if (TextBrightness(label) > TextBrightness(candidates[existing].Text))
            {
                candidates[existing] = candidate;
            }
        }

        if (candidates.Count != 1)
        {
            var labelPaths = labels.Length == 0
                ? "none"
                : string.Join(", ", labels.Select(NativeObjectPath.Build));
            var framePaths = candidates.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    candidates.Select(candidate => NativeObjectPath.Build(candidate.Image)));
            reason = "native '" + labelText + "' frame capture failed: " +
                labels.Length + " exact active text layers resolved to " +
                candidates.Count + " distinct sliced Button frames; text paths: " +
                labelPaths + "; frame paths: " + framePaths;
            return false;
        }

        var selected = candidates[0];
        frame = new NativeStartFrame(
            selected.Image.sprite!,
            selected.Image.color,
            selected.Text.color);
        text = selected.Text;
        reason = string.Empty;
        return true;
    }

    private static TextMeshProUGUI[] FindExactLabels(string text) =>
        ActiveStartLabels()
            .Where(candidate =>
                string.Equals(candidate.text?.Trim(), text, StringComparison.Ordinal))
            .ToArray();

    private static float TextBrightness(TextMeshProUGUI text) =>
        text.color.r + text.color.g + text.color.b;

    private static bool IsMainTitleControl(TextMeshProUGUI label) =>
        NativeObjectPath.Build(label)
            .StartsWith("Canvas/Panel/", StringComparison.Ordinal);

    private static IEnumerable<TextMeshProUGUI> ActiveStartLabels() =>
        Resources.FindObjectsOfTypeAll(typeof(TextMeshProUGUI))
            .OfType<TextMeshProUGUI>()
            .Where(label =>
                label.enabled &&
                label.gameObject.activeInHierarchy &&
                label.gameObject.scene.name == "Start" &&
                NativeObjectPath.Build(label)
                    .IndexOf(RootObjectName, StringComparison.Ordinal) < 0);

    private static bool IsNativeVersionText(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value.Length is >= 4 and <= 24 &&
            value[0] == 'v' &&
            char.IsDigit(value[1]) &&
            value.IndexOf('.') > 1;
    }

    private static void RemoveOwnedChild(Transform parent)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, RootObjectName, StringComparison.Ordinal))
                UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    public void Dispose()
    {
        if (_root is not null) UnityEngine.Object.Destroy(_root);
        _root = null;
        _statusFrame = null;
        _headline = null;
        _mode = null;
        _detailRows.Clear();
        _nativeBodyText = null;
        _native = null;
        NativeVersionPath = string.Empty;
    }

    private readonly struct NativeStartFrame
    {
        internal NativeStartFrame(
            Sprite sprite,
            Color color,
            Color textColor)
        {
            Sprite = sprite;
            Color = color;
            TextColor = textColor;
        }

        internal Sprite Sprite { get; }
        internal Color Color { get; }
        internal Color TextColor { get; }
    }

    private readonly struct NativeButtonCandidate
    {
        internal NativeButtonCandidate(
            Button button,
            Image image,
            TextMeshProUGUI text)
        {
            Button = button;
            Image = image;
            Text = text;
        }

        internal Button Button { get; }
        internal Image Image { get; }
        internal TextMeshProUGUI Text { get; }
    }

    private sealed class NativeStartCardVisuals
    {
        internal NativeStartCardVisuals(
            NativeStartFrame panel,
            NativeStartFrame ready,
            NativeStartFrame attention,
            NativeStartFrame failure,
            TextMeshProUGUI bodyText,
            TextMeshProUGUI readyText)
        {
            Palette = new NativeStartCardPalette(panel, ready, attention, failure);
            BodyText = bodyText;
            ReadyText = readyText;
        }

        internal NativeStartCardPalette Palette { get; }
        internal NativeStartFrame Panel => Palette.Panel;
        internal NativeStartFrame Ready => Palette.Ready;
        internal TextMeshProUGUI BodyText { get; }
        internal TextMeshProUGUI ReadyText { get; }
    }

    private sealed class NativeStartCardPalette
    {
        internal NativeStartCardPalette(
            NativeStartFrame panel,
            NativeStartFrame ready,
            NativeStartFrame attention,
            NativeStartFrame failure)
        {
            Panel = panel;
            Ready = ready;
            Attention = attention;
            Failure = failure;
        }

        internal NativeStartFrame Panel { get; }
        internal NativeStartFrame Ready { get; }
        internal NativeStartFrame Attention { get; }
        internal NativeStartFrame Failure { get; }
    }
}
