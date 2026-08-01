using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OrbChronicle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

/// <summary>
/// Native-skinned Chronicle dashboard. It projects the neutral runtime port only; gameplay
/// observation and persistence remain outside the UI.
/// </summary>
internal sealed class ChronicleRunsPage : IDisposable
{
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;
    private readonly TextMeshProUGUI _template;
    private readonly IChronicleRuntime _runtime;
    private readonly List<GameObject> _objects = new();
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private TextMeshProUGUI? _elapsed;
    private string _structureKey = string.Empty;
    private float _rememberedOffset;
    private bool _confirmAbandon;
    private bool _visible;

    internal ChronicleRunsPage(
        RectTransform content,
        ScrollRect scroll,
        TextMeshProUGUI template,
        IChronicleRuntime runtime)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    internal string Status { get; private set; } =
        "Chronicle is ready. Timing uses the shared immutable world snapshot.";

    internal void Show(bool resetScroll)
    {
        Rebuild(resetScroll);
    }

    internal void Refresh()
    {
        if (!_visible) return;
        var snapshot = _runtime.Snapshot;
        _elapsed!.text = FormatDuration(snapshot.ElapsedTicks);
        var key = StructureKey(snapshot, _runtime.History);
        if (!string.Equals(key, _structureKey, StringComparison.Ordinal)) Rebuild(resetScroll: false);
    }

    internal void Hide()
    {
        if (!_visible) return;
        _rememberedOffset = Math.Max(0f, _content.anchoredPosition.y);
        _visible = false;
        foreach (var item in _objects) item.SetActive(false);
    }

    public void Dispose()
    {
        ModConfigUiFactory.ClearObjects(_objects);
        _elapsed = null;
        _visible = false;
    }

    private void Rebuild(bool resetScroll)
    {
        var requestedOffset = resetScroll ? 0f : _visible
            ? Math.Max(0f, _content.anchoredPosition.y)
            : _rememberedOffset;
        ModConfigUiFactory.ClearObjects(_objects);
        var snapshot = _runtime.Snapshot;
        var history = _runtime.History;
        var top = 6f;

        top += BuildHero(snapshot, history, top);
        top += BuildMilestones(snapshot, history.Comparison, top);
        foreach (var section in snapshot.ResourceSections)
            top += BuildResourceSection(section, history.Comparison, top);
        top += BuildArchive(history, top);

        _content.sizeDelta = new Vector2(0f, Math.Max(1f, top + 8f));
        var viewportHeight = _content.parent is RectTransform viewport ? viewport.rect.height : 0f;
        var restored = ModSettingsLayout.ClampScrollOffset(requestedOffset, top + 8f, viewportHeight);
        _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, restored);
        _scroll.verticalNormalizedPosition = ModSettingsLayout.CalculateVerticalNormalizedPosition(
            restored, top + 8f, viewportHeight);
        _structureKey = StructureKey(snapshot, history);
        _visible = true;
    }

    private float BuildHero(
        ChronicleRunSnapshot snapshot,
        ChronicleHistorySnapshot history,
        float top)
    {
        const float height = 182f;
        var card = Rect("ChronicleHero", top, height, new Color(0.075f, 0.095f, 0.135f, 0.98f));
        Text(card.transform, "Eyebrow", .025f, .79f, .42f, .96f,
            "CHRONICLE  /  " + snapshot.State.ToString().ToUpperInvariant(), .62f,
            TextAlignmentOptions.MidlineLeft);
        _elapsed = Text(card.transform, "Elapsed", .025f, .35f, .45f, .80f,
            FormatDuration(snapshot.ElapsedTicks), 1.55f, TextAlignmentOptions.MidlineLeft);
        var comparison = history.Comparison;
        var delta = comparison is null
            ? "No compatible comparison yet"
            : "Δ " + FormatSigned(snapshot.ElapsedTicks - comparison.ElapsedTicks) +
              "  vs " + history.ComparisonMode;
        Text(card.transform, "Pace", .48f, .57f, .975f, .91f,
            delta, .72f, TextAlignmentOptions.Midline);
        Text(card.transform, "Schemas", .48f, .35f, .975f, .58f,
            snapshot.MilestoneSchemaId + "\n" + snapshot.ResourceSchemaId,
            .48f, TextAlignmentOptions.Midline);

        var canStart = snapshot.State is ChronicleRunState.Dormant or ChronicleRunState.Finished or ChronicleRunState.Abandoned;
        Button(card.transform, "Start", .025f, .07f, .20f, .31f, "Start run", canStart,
            () => Apply(_runtime.Start()));
        var pauseLabel = snapshot.State == ChronicleRunState.Paused ? "Resume" : "Pause";
        var canPause = snapshot.State is ChronicleRunState.Running or ChronicleRunState.Paused;
        Button(card.transform, "PauseResume", .215f, .07f, .39f, .31f, pauseLabel, canPause,
            () => Apply(snapshot.State == ChronicleRunState.Paused ? _runtime.Resume() : _runtime.Pause()));
        Button(card.transform, "Abandon", .405f, .07f, .60f, .31f,
            _confirmAbandon ? "Confirm abandon" : "Abandon", canPause,
            () =>
            {
                if (!_confirmAbandon)
                {
                    _confirmAbandon = true;
                    Status = "Press Confirm abandon to discard the active timer.";
                    Rebuild(resetScroll: false);
                    return;
                }
                _confirmAbandon = false;
                Apply(_runtime.Abandon());
            });
        Button(card.transform, "Compare", .62f, .07f, .975f, .31f,
            "Compare: " + history.ComparisonMode, true,
            () =>
            {
                _runtime.CycleComparison();
                Status = "Comparison changed.";
                Rebuild(resetScroll: false);
            });
        return height + 8f;
    }

    private float BuildMilestones(
        ChronicleRunSnapshot snapshot,
        ChronicleRunRecord? comparison,
        float top)
    {
        const float header = 72f;
        const float rowHeight = 34f;
        var height = header + snapshot.Milestones.Count * rowHeight + 8f;
        var card = Rect("SplitMatrix", top, height, ModConfigPalette.Row);
        Text(card.transform, "Title", .025f, 1f - 54f / height, .975f, 1f,
            "MAJOR SPLITS", .82f, TextAlignmentOptions.MidlineLeft);
        Text(card.transform, "Columns", .47f, 1f - 68f / height, .975f, 1f - 38f / height,
            "CURRENT        COMPARE          DELTA", .48f, TextAlignmentOptions.Midline);
        for (var index = 0; index < snapshot.Milestones.Count; index++)
        {
            var split = snapshot.Milestones[index];
            var compare = comparison?.Milestones.FirstOrDefault(item =>
                string.Equals(item.Id, split.Id, StringComparison.Ordinal));
            var currentText = split.ElapsedTicks.HasValue ? FormatDuration(split.ElapsedTicks.Value) : StateGlyph(split.State);
            var compareText = compare?.ElapsedTicks is long compareTicks ? FormatDuration(compareTicks) : "—";
            var deltaText = split.ElapsedTicks.HasValue && compare?.ElapsedTicks is long baseline
                ? FormatSigned(split.ElapsedTicks.Value - baseline)
                : "—";
            var yMax = 1f - (header + index * rowHeight) / height;
            var yMin = 1f - (header + (index + 1) * rowHeight) / height;
            Text(card.transform, "SplitLabel" + index, .025f, yMin, .46f, yMax,
                (index + 1).ToString("00", CultureInfo.InvariantCulture) + "  " + split.Label,
                .58f, TextAlignmentOptions.MidlineLeft);
            Text(card.transform, "SplitValue" + index, .47f, yMin, .975f, yMax,
                currentText.PadRight(16) + compareText.PadRight(17) + deltaText,
                .55f, TextAlignmentOptions.Midline);
        }
        return height + 8f;
    }

    private float BuildResourceSection(
        ChronicleResourceSectionSnapshot section,
        ChronicleRunRecord? comparison,
        float top)
    {
        var collapsed = _collapsed.Contains(section.Id);
        const float headerHeight = 62f;
        const float rowHeight = 31f;
        var height = headerHeight + (collapsed ? 0f : section.Resources.Count * rowHeight) + 5f;
        var card = Rect("ResourceSection." + section.Id, top, height,
            new Color(0.06f, 0.075f, 0.105f, .98f));
        Button(card.transform, "SectionToggle", .012f, 1f - headerHeight / height, .988f, .985f,
            (collapsed ? "▸  " : "▾  ") + section.Label + "   ·   " + section.Relationship +
            "   ·   " + section.CapturedCount + "/" + section.Resources.Count,
            true,
            () =>
            {
                if (!_collapsed.Add(section.Id)) _collapsed.Remove(section.Id);
                Rebuild(resetScroll: false);
            });
        if (collapsed) return height + 6f;
        for (var index = 0; index < section.Resources.Count; index++)
        {
            var resource = section.Resources[index];
            var baseline = comparison?.Resources.FirstOrDefault(item =>
                string.Equals(item.SectionId, section.Id, StringComparison.Ordinal) &&
                string.Equals(item.Id, resource.Id, StringComparison.Ordinal));
            var yMax = 1f - (headerHeight + index * rowHeight) / height;
            var yMin = 1f - (headerHeight + (index + 1) * rowHeight) / height;
            var captured = resource.ElapsedTicks.HasValue ? FormatDuration(resource.ElapsedTicks.Value) : StateGlyph(resource.State);
            var amount = resource.Quantity.HasValue ? FormatBig(resource.Quantity.Value) : "—";
            var rate = resource.TrueRate.HasValue ? FormatBig(resource.TrueRate.Value) + "/s" : "—";
            var compareDelta = resource.ElapsedTicks.HasValue && baseline?.ElapsedTicks is long baselineTicks
                ? FormatSigned(resource.ElapsedTicks.Value - baselineTicks)
                : "—";
            var ratios = baseline is null
                ? string.Empty
                : "   Q" + RatioText(resource.Quantity, baseline.Quantity) +
                  " R" + RatioText(resource.TrueRate, baseline.TrueRate) +
                  " C" + RatioText(resource.Capacity, baseline.Capacity);
            Text(card.transform, "ResourceLabel" + index, .025f, yMin, .36f, yMax,
                resource.Label, .53f, TextAlignmentOptions.MidlineLeft);
            Text(card.transform, "ResourceValue" + index, .37f, yMin, .975f, yMax,
                captured + "   " + amount + "   " + rate + "   " + compareDelta + ratios,
                .49f, TextAlignmentOptions.Midline);
        }
        return height + 6f;
    }

    private float BuildArchive(ChronicleHistorySnapshot history, float top)
    {
        var recent = history.Runs.Reverse().Take(5).ToArray();
        const float rowHeight = 32f;
        var height = 58f + Math.Max(1, recent.Length) * rowHeight;
        var card = Rect("RunArchive", top, height, ModConfigPalette.Row);
        Text(card.transform, "Title", .025f, 1f - 52f / height, .975f, 1f,
            "RECENT ARCHIVE  ·  " + history.Runs.Count + " SAVED RUNS", .72f,
            TextAlignmentOptions.MidlineLeft);
        if (recent.Length == 0)
        {
            Text(card.transform, "Empty", .025f, .02f, .975f, 1f - 54f / height,
                "Finish a run to establish your first comparison.", .56f,
                TextAlignmentOptions.MidlineLeft);
        }
        for (var index = 0; index < recent.Length; index++)
        {
            var run = recent[index];
            var yMax = 1f - (58f + index * rowHeight) / height;
            var yMin = 1f - (58f + (index + 1) * rowHeight) / height;
            Button(card.transform, "Archive" + index, .025f, yMin, .975f, yMax,
                "#" + (history.Runs.Count - index).ToString("00", CultureInfo.InvariantCulture) +
                "   " + FormatDuration(run.ElapsedTicks) + "   " +
                new DateTime(run.CompletedAtUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) +
                "   " + run.RunId.Substring(0, 8),
                true,
                () =>
                {
                    if (_runtime.TrySelectComparison("Selected", run.RunId, out var reason))
                        Status = "Selected archived run " + run.RunId.Substring(0, 8) + " for comparison.";
                    else
                        Status = "Comparison selection rejected: " + reason;
                    Rebuild(resetScroll: false);
                });
        }
        return height + 8f;
    }

    private GameObject Rect(string name, float top, float height, Color color)
    {
        var item = ModConfigUiFactory.CreateRectObject(
            name, _content, new Vector2(0f, 1f), Vector2.one, color);
        var rect = (RectTransform)item.transform;
        ModConfigUiFactory.SetTopAnchoredHeight(rect, top, height);
        _objects.Add(item);
        return item;
    }

    private TextMeshProUGUI Text(
        Transform parent, string name, float x0, float y0, float x1, float y1,
        string value, float size, TextAlignmentOptions alignment) =>
        ModConfigUiFactory.CreateText(name, parent, new Vector2(x0, y0), new Vector2(x1, y1),
            _template, value, alignment, size, TextOverflowModes.Ellipsis);

    private Button Button(
        Transform parent, string name, float x0, float y0, float x1, float y1,
        string label, bool enabled, Action action)
    {
        var button = ModConfigUiFactory.CreateButton(name, parent, new Vector2(x0, y0),
            new Vector2(x1, y1), _template, label, () => action());
        button.interactable = enabled;
        return button;
    }

    private void Apply(ChronicleCommandOutcome outcome)
    {
        _confirmAbandon = false;
        Status = outcome.Accepted ? outcome.Reason : outcome.Code + ": " + outcome.Reason;
        Rebuild(resetScroll: false);
    }

    private static string StructureKey(ChronicleRunSnapshot snapshot, ChronicleHistorySnapshot history) =>
        snapshot.State + "|" + history.Revision + "|" +
        string.Join(",", snapshot.Milestones.Select(item => item.State + ":" + item.ElapsedTicks)) + "|" +
        string.Join(",", snapshot.ResourceSections.SelectMany(item => item.Resources)
            .Select(item => item.State + ":" + item.ElapsedTicks));

    private static string StateGlyph(object state) => state.ToString() switch
    {
        "Captured" or "Reached" => "✓",
        "Preexisting" => "prior",
        "Missing" => "missing",
        _ => "—",
    };

    private static string FormatDuration(long ticks)
    {
        var duration = TimeSpan.FromTicks(Math.Max(0, ticks));
        var hours = (long)duration.TotalHours;
        return hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
               duration.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
               duration.Seconds.ToString("00", CultureInfo.InvariantCulture) + "." +
               (duration.Milliseconds / 100).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSigned(long ticks) =>
        (ticks <= 0 ? "−" : "+") + FormatDuration(Math.Abs(ticks));

    private static string FormatBig(BigDouble value) =>
        value.Exponent is >= -2 and <= 5
            ? value.ToDouble().ToString("0.###", CultureInfo.InvariantCulture)
            : value.Mantissa.ToString("0.##", CultureInfo.InvariantCulture) + "e" +
              value.Exponent.ToString(CultureInfo.InvariantCulture);

    private static string RatioText(BigDouble? value, BigDouble? baseline) =>
        value.HasValue && baseline.HasValue && baseline.Value.Mantissa != 0
            ? FormatBig(value.Value / baseline.Value) + "×"
            : "—";
}
