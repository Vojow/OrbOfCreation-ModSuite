using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OrbChronicle;

internal enum ChronicleComparisonMode
{
    PersonalBest = 0,
    Previous = 1,
    Selected = 2,
}

internal sealed class ChronicleRecordedMilestone
{
    internal ChronicleRecordedMilestone(string id, string label, long? elapsedTicks)
    {
        Id = id;
        Label = label;
        ElapsedTicks = elapsedTicks;
    }

    internal string Id { get; }
    internal string Label { get; }
    internal long? ElapsedTicks { get; }
    internal double? ElapsedSeconds => ElapsedTicks.HasValue
        ? ElapsedTicks.Value / (double)TimeSpan.TicksPerSecond
        : null;
}

internal sealed class ChronicleRecordedResource
{
    internal ChronicleRecordedResource(
        string sectionId,
        string id,
        string label,
        long? elapsedTicks,
        BigDouble? quantity,
        BigDouble? trueRate,
        BigDouble? capacity)
    {
        SectionId = sectionId;
        Id = id;
        Label = label;
        ElapsedTicks = elapsedTicks;
        Quantity = quantity;
        TrueRate = trueRate;
        Capacity = capacity;
    }

    internal string SectionId { get; }
    internal string Id { get; }
    internal string Label { get; }
    internal long? ElapsedTicks { get; }
    internal BigDouble? Quantity { get; }
    internal BigDouble? TrueRate { get; }
    internal BigDouble? Capacity { get; }
}

internal sealed class ChronicleRunRecord
{
    internal ChronicleRunRecord(
        string runId,
        long completedAtUtcTicks,
        long elapsedTicks,
        string milestoneSchemaId,
        string resourceSchemaId,
        string clockId,
        ChronicleRecordedMilestone[] milestones,
        ChronicleRecordedResource[] resources,
        string runeSchemaId,
        ChronicleRuneLevelEvent[] runeTimeline,
        ChronicleRuneBuildMix runeMix,
        bool runeTimelineTruncated)
    {
        if (!Guid.TryParseExact(runId, "D", out _))
            throw new ArgumentException("A recorded run requires a canonical run ID.", nameof(runId));
        if (completedAtUtcTicks <= 0 || completedAtUtcTicks > DateTime.MaxValue.Ticks || elapsedTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(completedAtUtcTicks));
        RunId = runId;
        CompletedAtUtcTicks = completedAtUtcTicks;
        ElapsedTicks = elapsedTicks;
        MilestoneSchemaId = milestoneSchemaId;
        ResourceSchemaId = resourceSchemaId;
        ClockId = clockId;
        RuneSchemaId = runeSchemaId ?? string.Empty;
        Milestones = Array.AsReadOnly((ChronicleRecordedMilestone[])milestones.Clone());
        Resources = Array.AsReadOnly((ChronicleRecordedResource[])resources.Clone());
        RuneTimeline = Array.AsReadOnly((ChronicleRuneLevelEvent[])runeTimeline.Clone());
        RuneMix = runeMix ?? throw new ArgumentNullException(nameof(runeMix));
        RuneTimelineTruncated = runeTimelineTruncated;
    }

    internal string RunId { get; }
    internal long CompletedAtUtcTicks { get; }
    internal long ElapsedTicks { get; }
    internal double ElapsedSeconds => ElapsedTicks / (double)TimeSpan.TicksPerSecond;
    internal string MilestoneSchemaId { get; }
    internal string ResourceSchemaId { get; }
    internal string ClockId { get; }
    internal string RuneSchemaId { get; }
    internal IReadOnlyList<ChronicleRecordedMilestone> Milestones { get; }
    internal IReadOnlyList<ChronicleRecordedResource> Resources { get; }
    internal IReadOnlyList<ChronicleRuneLevelEvent> RuneTimeline { get; }
    internal ChronicleRuneBuildMix RuneMix { get; }
    internal bool RuneTimelineTruncated { get; }

    internal bool IsCompatible(ChronicleRunSnapshot current) =>
        string.Equals(MilestoneSchemaId, current.MilestoneSchemaId, StringComparison.Ordinal) &&
        string.Equals(ResourceSchemaId, current.ResourceSchemaId, StringComparison.Ordinal) &&
        string.Equals(ClockId, current.ClockId, StringComparison.Ordinal);

    internal bool IsRuneCompatible(ChronicleRunSnapshot current) =>
        string.Equals(RuneSchemaId, current.RuneSchemaId, StringComparison.Ordinal);

    internal static ChronicleRunRecord Capture(ChronicleRunSnapshot snapshot, long completedAtUtcTicks)
    {
        if (snapshot.State != ChronicleRunState.Finished)
            throw new ArgumentException("Only finished Chronicle runs can enter history.", nameof(snapshot));
        var milestones = snapshot.Milestones.Select(candidate => new ChronicleRecordedMilestone(
            candidate.Id,
            candidate.Label,
            candidate.State == ChronicleMilestoneState.Reached ? candidate.ElapsedTicks : null)).ToArray();
        var resources = snapshot.ResourceSections.SelectMany(section => section.Resources.Select(candidate =>
            new ChronicleRecordedResource(
                section.Id,
                candidate.Id,
                candidate.Label,
                candidate.State == ChronicleResourceKpiState.Captured ? candidate.ElapsedTicks : null,
                candidate.Quantity,
                candidate.TrueRate,
                candidate.Capacity))).ToArray();
        return new ChronicleRunRecord(
            snapshot.RunId,
            completedAtUtcTicks,
            snapshot.ElapsedTicks,
            snapshot.MilestoneSchemaId,
            snapshot.ResourceSchemaId,
            snapshot.ClockId,
            milestones,
            resources,
            snapshot.RuneSchemaId,
            snapshot.RuneTimeline.ToArray(),
            snapshot.RuneMix,
            snapshot.RuneTimelineTruncated);
    }
}

internal sealed class ChronicleResourceComparison
{
    internal ChronicleResourceComparison(
        string sectionId,
        string resourceId,
        long? discoveryDeltaTicks,
        BigDouble? quantityDelta,
        BigDouble? quantityRatio,
        BigDouble? trueRateDelta,
        BigDouble? trueRateRatio,
        BigDouble? capacityDelta,
        BigDouble? capacityRatio)
    {
        SectionId = sectionId;
        ResourceId = resourceId;
        DiscoveryDeltaTicks = discoveryDeltaTicks;
        QuantityDelta = quantityDelta;
        QuantityRatio = quantityRatio;
        TrueRateDelta = trueRateDelta;
        TrueRateRatio = trueRateRatio;
        CapacityDelta = capacityDelta;
        CapacityRatio = capacityRatio;
    }

    internal string SectionId { get; }
    internal string ResourceId { get; }
    internal long? DiscoveryDeltaTicks { get; }
    internal BigDouble? QuantityDelta { get; }
    internal BigDouble? QuantityRatio { get; }
    internal BigDouble? TrueRateDelta { get; }
    internal BigDouble? TrueRateRatio { get; }
    internal BigDouble? CapacityDelta { get; }
    internal BigDouble? CapacityRatio { get; }
}

internal sealed class ChronicleHistorySnapshot
{
    internal ChronicleHistorySnapshot(
        long revision,
        ChronicleComparisonMode comparisonMode,
        string selectedRunId,
        string status,
        ChronicleRunRecord? personalBest,
        ChronicleRunRecord? comparison,
        ChronicleResourceComparison[] resourceComparisons,
        ChronicleRunRecord[] runs)
    {
        Revision = revision;
        ComparisonMode = comparisonMode;
        SelectedRunId = selectedRunId;
        Status = status;
        PersonalBest = personalBest;
        Comparison = comparison;
        ResourceComparisons = Array.AsReadOnly(
            (ChronicleResourceComparison[])resourceComparisons.Clone());
        Runs = Array.AsReadOnly((ChronicleRunRecord[])runs.Clone());
    }

    internal long Revision { get; }
    internal ChronicleComparisonMode ComparisonMode { get; }
    internal string SelectedRunId { get; }
    internal string Status { get; }
    internal ChronicleRunRecord? PersonalBest { get; }
    internal ChronicleRunRecord? Comparison { get; }
    internal IReadOnlyList<ChronicleResourceComparison> ResourceComparisons { get; }
    internal IReadOnlyList<ChronicleRunRecord> Runs { get; }
}

internal sealed class ChronicleHistory
{
    private const int RetentionLimit = 50;
    private readonly string _path;
    private readonly Action<string> _logWarning;
    private readonly List<ChronicleRunRecord> _runs = new();
    private ChronicleComparisonMode _comparisonMode = ChronicleComparisonMode.PersonalBest;
    private string _selectedRunId = string.Empty;
    private string _status = "History is ready.";
    private string _eventFingerprint = string.Empty;
    private bool _writeBlocked;
    private long _revision = 1;

    internal ChronicleHistory(string path, Action<string> logWarning)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A Chronicle history path is required.", nameof(path))
            : Path.GetFullPath(path);
        _logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        Load();
    }

    internal ChronicleHistorySnapshot Project(ChronicleRunSnapshot current)
    {
        var compatible = _runs.Where(run => run.IsCompatible(current)).ToArray();
        var personalBest = compatible.OrderBy(run => run.ElapsedTicks).FirstOrDefault();
        ChronicleRunRecord? comparison = _comparisonMode switch
        {
            ChronicleComparisonMode.PersonalBest => personalBest,
            ChronicleComparisonMode.Previous => compatible.LastOrDefault(run =>
                !string.Equals(run.RunId, current.RunId, StringComparison.Ordinal)),
            ChronicleComparisonMode.Selected => compatible.FirstOrDefault(run =>
                string.Equals(run.RunId, _selectedRunId, StringComparison.Ordinal)),
            _ => null,
        };
        return new ChronicleHistorySnapshot(
            _revision,
            _comparisonMode,
            _selectedRunId,
            _status,
            personalBest,
            comparison,
            BuildResourceComparisons(current, comparison),
            _runs.ToArray());
    }

    private static ChronicleResourceComparison[] BuildResourceComparisons(
        ChronicleRunSnapshot current,
        ChronicleRunRecord? comparison)
    {
        if (comparison is null) return Array.Empty<ChronicleResourceComparison>();
        return current.ResourceSections.SelectMany(section => section.Resources.Select(resource =>
        {
            var baseline = comparison.Resources.FirstOrDefault(item =>
                string.Equals(item.SectionId, section.Id, StringComparison.Ordinal) &&
                string.Equals(item.Id, resource.Id, StringComparison.Ordinal));
            return new ChronicleResourceComparison(
                section.Id,
                resource.Id,
                resource.ElapsedTicks.HasValue && baseline?.ElapsedTicks is long baselineTicks
                    ? resource.ElapsedTicks.Value - baselineTicks
                    : null,
                Difference(resource.Quantity, baseline?.Quantity),
                Ratio(resource.Quantity, baseline?.Quantity),
                Difference(resource.TrueRate, baseline?.TrueRate),
                Ratio(resource.TrueRate, baseline?.TrueRate),
                Difference(resource.Capacity, baseline?.Capacity),
                Ratio(resource.Capacity, baseline?.Capacity));
        })).ToArray();
    }

    private static BigDouble? Difference(BigDouble? current, BigDouble? baseline) =>
        current.HasValue && baseline.HasValue ? current.Value - baseline.Value : null;

    private static BigDouble? Ratio(BigDouble? current, BigDouble? baseline) =>
        current.HasValue && baseline.HasValue && baseline.Value.Mantissa != 0
            ? current.Value / baseline.Value
            : null;

    internal void Observe(ChronicleRunSnapshot current)
    {
        var fingerprint = EventFingerprint(current);
        if (string.Equals(fingerprint, _eventFingerprint, StringComparison.Ordinal)) return;
        _eventFingerprint = fingerprint;
        if (current.State == ChronicleRunState.Finished &&
            _runs.All(run => !string.Equals(run.RunId, current.RunId, StringComparison.Ordinal)))
        {
            _runs.Add(ChronicleRunRecord.Capture(current, DateTime.UtcNow.Ticks));
            while (_runs.Count > RetentionLimit) _runs.RemoveAt(0);
            _status = "Finished run archived.";
        }
        _revision++;
        Save(current);
    }

    internal void CycleComparison(ChronicleRunSnapshot current)
    {
        _comparisonMode = (ChronicleComparisonMode)(((int)_comparisonMode + 1) % 3);
        if (_comparisonMode == ChronicleComparisonMode.Selected)
        {
            var compatible = _runs.Where(run => run.IsCompatible(current)).ToArray();
            if (compatible.Length > 0)
            {
                var index = Array.FindIndex(compatible, run =>
                    string.Equals(run.RunId, _selectedRunId, StringComparison.Ordinal));
                _selectedRunId = compatible[(index + 1 + compatible.Length) % compatible.Length].RunId;
            }
        }
        _revision++;
        Save(current);
    }

    internal bool TrySelect(ChronicleRunSnapshot current, string mode, string runId, out string reason)
    {
        if (!Enum.TryParse(mode, ignoreCase: true, out ChronicleComparisonMode parsed))
        {
            reason = "comparison mode must be PersonalBest, Previous, or Selected";
            return false;
        }
        if (parsed == ChronicleComparisonMode.Selected)
        {
            var match = _runs.FirstOrDefault(run =>
                string.Equals(run.RunId, runId, StringComparison.Ordinal) && run.IsCompatible(current));
            if (match is null)
            {
                reason = "selected run is absent or schema-incompatible";
                return false;
            }
            _selectedRunId = match.RunId;
        }
        _comparisonMode = parsed;
        _revision++;
        Save(current);
        reason = string.Empty;
        return true;
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var root = JObject.Parse(File.ReadAllText(_path));
            var schemaVersion = (int?)root["schemaVersion"] ?? 0;
            if (schemaVersion is not (1 or 2)) throw new InvalidDataException("unsupported schema");
            _comparisonMode = Enum.TryParse(
                (string?)root["comparisonMode"], true, out ChronicleComparisonMode parsed)
                ? parsed
                : ChronicleComparisonMode.PersonalBest;
            _selectedRunId = (string?)root["selectedRunId"] ?? string.Empty;
            var runTokens = root["runs"] as JArray ?? new JArray();
            if (runTokens.Count > RetentionLimit)
                throw new InvalidDataException("history retention limit exceeded");
            foreach (var token in runTokens)
            {
                var run = ParseRun(
                    token as JObject ?? throw new InvalidDataException("run entry invalid"),
                    schemaVersion);
                if (_runs.Any(candidate => string.Equals(candidate.RunId, run.RunId, StringComparison.Ordinal)))
                    throw new InvalidDataException("duplicate run ID");
                _runs.Add(run);
            }
            if (root["active"] is JObject active &&
                (string.Equals((string?)active["state"], "Running", StringComparison.Ordinal) ||
                 string.Equals((string?)active["state"], "Paused", StringComparison.Ordinal)))
            {
                _status = "An interrupted run was preserved but not resumed because save identity is not proven.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidDataException or FormatException or OverflowException or
            ArgumentException)
        {
            _writeBlocked = true;
            _status = "History is read-only because its sidecar is invalid: " + exception.Message;
            _logWarning(_status);
        }
    }

    private void Save(ChronicleRunSnapshot current)
    {
        if (_writeBlocked) return;
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            var root = new JObject
            {
                ["schemaVersion"] = 2,
                ["comparisonMode"] = _comparisonMode.ToString(),
                ["selectedRunId"] = _selectedRunId,
                ["active"] = SerializeActive(current),
                ["runs"] = new JArray(_runs.Select(SerializeRun)),
            };
            File.WriteAllText(temporary, root.ToString(Formatting.Indented));
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _status = "History write failed; timing continues in memory: " + exception.Message;
            _logWarning(_status);
        }
    }

    private static JObject SerializeActive(ChronicleRunSnapshot snapshot) => new()
    {
        ["runId"] = snapshot.RunId,
        ["state"] = snapshot.State.ToString(),
        ["elapsedTicks"] = snapshot.ElapsedTicks,
        ["milestoneSchemaId"] = snapshot.MilestoneSchemaId,
        ["resourceSchemaId"] = snapshot.ResourceSchemaId,
        ["runeSchemaId"] = snapshot.RuneSchemaId,
        ["clockId"] = snapshot.ClockId,
    };

    private static JObject SerializeRun(ChronicleRunRecord run) => new()
    {
        ["runId"] = run.RunId,
        ["completedAtUtcTicks"] = run.CompletedAtUtcTicks,
        ["elapsedTicks"] = run.ElapsedTicks,
        ["milestoneSchemaId"] = run.MilestoneSchemaId,
        ["resourceSchemaId"] = run.ResourceSchemaId,
        ["runeSchemaId"] = run.RuneSchemaId,
        ["clockId"] = run.ClockId,
        ["milestones"] = new JArray(run.Milestones.Select(item => new JObject
        {
            ["id"] = item.Id,
            ["label"] = item.Label,
            ["elapsedTicks"] = item.ElapsedTicks,
        })),
        ["resources"] = new JArray(run.Resources.Select(item => new JObject
        {
            ["sectionId"] = item.SectionId,
            ["id"] = item.Id,
            ["label"] = item.Label,
            ["elapsedTicks"] = item.ElapsedTicks,
            ["quantity"] = SerializeBig(item.Quantity),
            ["trueRate"] = SerializeBig(item.TrueRate),
            ["capacity"] = SerializeBig(item.Capacity),
        })),
        ["runeTimelineTruncated"] = run.RuneTimelineTruncated,
        ["runeMix"] = SerializeRuneMix(run.RuneMix),
        ["runeTimeline"] = new JArray(run.RuneTimeline.Select(item => new JObject
        {
            ["sequence"] = item.Sequence,
            ["targetUuid"] = item.TargetUuid,
            ["label"] = item.Label,
            ["archetype"] = item.Archetype.ToString(),
            ["elapsedTicks"] = item.ElapsedTicks,
            ["levelBefore"] = item.LevelBefore,
            ["levelAfter"] = item.LevelAfter,
            ["masteryLevel"] = item.MasteryLevel,
            ["discoveryRarityLevel"] = item.DiscoveryRarityLevel,
        })),
    };

    private static ChronicleRunRecord ParseRun(JObject value, int schemaVersion)
    {
        var milestones = value["milestones"] as JArray ??
                         throw new InvalidDataException("milestones missing");
        var resources = value["resources"] as JArray ??
                        throw new InvalidDataException("resources missing");
        var runeTimeline = schemaVersion >= 2
            ? value["runeTimeline"] as JArray ?? throw new InvalidDataException("rune timeline missing")
            : new JArray();
        if (milestones.Count > 64 || resources.Count > 256 ||
            runeTimeline.Count > ChronicleRunTracker.MaximumRuneEvents)
            throw new InvalidDataException("run entry exceeds bounded schema size");
        return new ChronicleRunRecord(
            RequireString(value, "runId"),
            RequireLong(value, "completedAtUtcTicks"),
            RequireLong(value, "elapsedTicks"),
            RequireString(value, "milestoneSchemaId"),
            RequireString(value, "resourceSchemaId"),
            RequireString(value, "clockId"),
            milestones.Cast<JObject>().Select(item => new ChronicleRecordedMilestone(
                RequireString(item, "id"),
                RequireString(item, "label"),
                (long?)item["elapsedTicks"])).ToArray(),
            resources.Cast<JObject>().Select(item => new ChronicleRecordedResource(
                RequireString(item, "sectionId"),
                RequireString(item, "id"),
                RequireString(item, "label"),
                (long?)item["elapsedTicks"],
                ParseBig(item["quantity"]),
                ParseBig(item["trueRate"]),
                ParseBig(item["capacity"]))).ToArray(),
            schemaVersion >= 2 ? RequireString(value, "runeSchemaId") : string.Empty,
            runeTimeline.Cast<JObject>().Select(ParseRuneEvent).ToArray(),
            schemaVersion >= 2
                ? ParseRuneMix(value["runeMix"] as JObject ??
                    throw new InvalidDataException("rune mix missing"))
                : new ChronicleRuneBuildMix(0, 0, 0, 0),
            schemaVersion >= 2 && ((bool?)value["runeTimelineTruncated"] ?? false));
    }

    private static JObject SerializeRuneMix(ChronicleRuneBuildMix mix) => new()
    {
        ["tempoLevels"] = mix.TempoLevels,
        ["scalingLevels"] = mix.ScalingLevels,
        ["investmentLevels"] = mix.InvestmentLevels,
        ["otherLevels"] = mix.OtherLevels,
    };

    private static ChronicleRuneBuildMix ParseRuneMix(JObject value) => new(
        RequireLong(value, "tempoLevels"),
        RequireLong(value, "scalingLevels"),
        RequireLong(value, "investmentLevels"),
        RequireLong(value, "otherLevels"));

    private static ChronicleRuneLevelEvent ParseRuneEvent(JObject value)
    {
        if (!Guid.TryParseExact(RequireString(value, "targetUuid"), "D", out var targetId))
            throw new InvalidDataException("rune target UUID invalid");
        if (!Enum.TryParse(
                RequireString(value, "archetype"),
                ignoreCase: false,
                out ChronicleRuneArchetype archetype))
        {
            throw new InvalidDataException("rune archetype invalid");
        }
        return new ChronicleRuneLevelEvent(
            RequireInt(value, "sequence"),
            targetId,
            RequireString(value, "label"),
            archetype,
            RequireLong(value, "elapsedTicks"),
            RequireInt(value, "levelBefore"),
            RequireInt(value, "levelAfter"),
            RequireInt(value, "masteryLevel"),
            RequireInt(value, "discoveryRarityLevel"));
    }

    private static JToken SerializeBig(BigDouble? value) => !value.HasValue
        ? JValue.CreateNull()
        : new JObject
        {
            ["mantissa"] = value.Value.Mantissa,
            ["exponent"] = value.Value.Exponent,
        };

    private static BigDouble? ParseBig(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null) return null;
        var value = (JObject)token;
        return new BigDouble((double)value["mantissa"]!, (long)value["exponent"]!);
    }

    private static string EventFingerprint(ChronicleRunSnapshot current) =>
        current.RunId + "|" + current.State + "|" +
        string.Join(",", current.Milestones.Select(item => item.State + ":" + item.ElapsedTicks)) + "|" +
        string.Join(",", current.ResourceSections.SelectMany(section => section.Resources)
            .Select(item => item.State + ":" + item.ElapsedTicks)) + "|" +
        current.RuneTimeline.Count + ":" + current.RuneMix.TotalLevels + ":" +
        current.RuneTimelineTruncated;

    private static string RequireString(JObject value, string property) =>
        (string?)value[property] is { Length: > 0 } result
            ? result
            : throw new InvalidDataException(property + " missing");

    private static long RequireLong(JObject value, string property) =>
        (long?)value[property] ?? throw new InvalidDataException(property + " missing");

    private static int RequireInt(JObject value, string property) =>
        (int?)value[property] ?? throw new InvalidDataException(property + " missing");
}
