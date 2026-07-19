using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrbModding.PerformanceEvidence;

public static class PerformanceEvidencePipeline
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumItems = 64;
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static PerformanceProfile ReadProfile(string path)
    {
        var bytes = ReadBounded(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = ParseDocument(bytes, path);
        return ParseProfile(document.RootElement, hash);
    }

    public static PerformanceEvidenceDocument ReadEvidence(string path)
    {
        var bytes = ReadBounded(path);
        using var document = ParseDocument(bytes, path);
        return ParseEvidence(document.RootElement, requireSorted: true);
    }

    public static PerformanceProfile ParseProfile(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = ParseDocument(bytes, "profile");
        return ParseProfile(document.RootElement, hash);
    }

    public static PerformanceEvidenceDocument ParseEvidence(string json, bool requireSorted = true)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var document = ParseDocument(bytes, "evidence");
        return ParseEvidence(document.RootElement, requireSorted);
    }

    public static string CanonicalizeEvidence(string json)
    {
        var evidence = ParseEvidence(json, requireSorted: false);
        SortWork(evidence.Start.Work);
        SortWork(evidence.End.Work);
        return JsonSerializer.Serialize(evidence, CanonicalOptions) + "\n";
    }

    public static PerformanceEvaluation Evaluate(
        PerformanceProfile profile,
        PerformanceEvidenceDocument evidence)
    {
        ValidateCompatibility(profile, evidence);
        var results = new List<MetricEvaluation>(profile.Rules.Count * 10);
        EvaluateCoordinatorTiming(results, profile, evidence);
        for (var index = 0; index < profile.Rules.Count; index++)
        {
            var rule = profile.Rules[index];
            var start = evidence.Start.Work[index];
            var end = evidence.End.Work[index];
            var identity = Identity(rule);

            if (end.IsDisposed)
            {
                results.Add(new(identity, "registration", "insufficient-samples", 1, 0, "target registration was disposed"));
                continue;
            }

            EvaluateZeroDelta(results, identity, "measurementFailures", start.MeasurementFailures, end.MeasurementFailures);
            EvaluateZeroDelta(results, identity, "starvationEvents", start.StarvationEvents, end.StarvationEvents);
            EvaluateZeroDelta(results, identity, "abandonedWorkItems", start.AbandonedWorkItems, end.AbandonedWorkItems);
            EvaluateCumulativeMaximum(results, identity, "maximumPendingWaitFrames", start.MaximumPendingWaitFrames, end.MaximumPendingWaitFrames, rule.MaximumPendingWaitFrames);
            results.Add(new(
                identity,
                "distinctDeferredFrames",
                "observe-only",
                Delta(start.DeferredFrames, end.DeferredFrames, identity, "deferredFrames"),
                0,
                "distinct deferred frames are diagnostic; isolated deferrals are not a wait streak"));
            EvaluateCumulativeMaximum(
                results,
                identity,
                "maximumConsecutiveDeferredFrames",
                start.MaximumConsecutiveDeferredFrames,
                end.MaximumConsecutiveDeferredFrames,
                rule.MaximumConsecutiveDeferredFrames);

            var native = rule.TimingMode == "observe-only";
            EvaluateTiming(results, identity, "workItemTiming", rule, start.WorkItemTiming, end.WorkItemTiming, native);
            EvaluateTiming(results, identity, "frameTiming", rule, start.FrameTiming, end.FrameTiming, native);

            var nativeOverruns = Delta(
                start.NativeHardBudgetOverruns,
                end.NativeHardBudgetOverruns,
                identity,
                "nativeHardBudgetOverruns");
            results.Add(new(
                identity,
                "nativeHardBudgetOverruns",
                native ? "observe-only" : nativeOverruns == 0 ? "within-target" : "exceeded",
                nativeOverruns,
                0,
                native ? "native duration remains observational pending desktop and Steam Deck evidence" : null));
        }

        return new PerformanceEvaluation(
            evidence.ProfileId,
            evidence.ProfileVersion,
            evidence.ProfileSha256,
            evidence.Metadata.CaptureKind,
            evidence.Metadata.SourceCommit,
            evidence.Metadata.Scenario,
            results);
    }

    public static string WriteEvaluationJson(PerformanceEvaluation evaluation) =>
        JsonSerializer.Serialize(evaluation, CanonicalOptions) + "\n";

    public static string WriteEvaluationMarkdown(PerformanceEvaluation evaluation)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Suite performance evidence");
        builder.AppendLine();
        builder.Append("Profile: ").Append(EncodeMarkdownText(evaluation.ProfileId)).Append(" v")
            .Append(evaluation.ProfileVersion).AppendLine();
        builder.Append("Capture: ").Append(EncodeMarkdownText(evaluation.CaptureKind)).Append(" - ")
            .Append(EncodeMarkdownText(evaluation.SourceCommit)).Append(" - ")
            .AppendLine(EncodeMarkdownText(evaluation.Scenario));
        builder.AppendLine();
        builder.AppendLine("| Work | Metric | Classification | Observed | Target |");
        builder.AppendLine("|---|---|---:|---:|---:|");
        foreach (var result in evaluation.Results)
        {
            builder.Append('|').Append(EncodeMarkdownText(result.WorkIdentity.Replace("\u001f", " / ", StringComparison.Ordinal)))
                .Append('|').Append(EncodeMarkdownText(result.Metric))
                .Append('|').Append(EncodeMarkdownText(result.Classification))
                .Append('|').Append(result.Observed.ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(result.Target.ToString("R", CultureInfo.InvariantCulture))
                .AppendLine("|");
        }

        return builder.ToString();
    }

    public static void WriteAtomic(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Output path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static PerformanceProfile ParseProfile(JsonElement root, string sourceSha256)
    {
        Object(root, "$profile", "schemaId", "schemaVersion", "profileId", "profileVersion", "scope", "policy", "rules");
        Exact(root, "schemaId", "orb-modsuite-suite-performance-profile", "$profile");
        Exact(root, "schemaVersion", 1, "$profile");
        Exact(root, "profileId", "supported-suite-beta-v1", "$profile");
        Exact(root, "profileVersion", 1, "$profile");
        Exact(root, "scope", "full-supported-suite", "$profile");
        var policy = ParsePolicy(Required(root, "policy", JsonValueKind.Object, "$profile"), "$profile.policy");
        if (policy.SoftBudgetMilliseconds != 0.75 || policy.HardBudgetMilliseconds != 1.0 || policy.NativeMutationAdmissionsPerFrame != 1)
        {
            throw Invalid("$profile.policy", "profile V1 requires the 0.75/1.0 ms and one-mutation policy");
        }
        var rulesElement = Required(root, "rules", JsonValueKind.Array, "$profile");
        if (rulesElement.GetArrayLength() != 12)
        {
            throw Invalid("$profile.rules", "profile V1 must contain exactly 12 rules");
        }

        var rules = new List<PerformanceRule>(rulesElement.GetArrayLength());
        foreach (var item in rulesElement.EnumerateArray())
        {
            var path = $"$profile.rules[{rules.Count}]";
            Object(item, path, "subsystem", "workName", "budgetClass", "executionKind", "maximumPendingWaitFrames", "maximumConsecutiveDeferredFrames", "timingMode", "minimumSamples", "p95Milliseconds", "p99Milliseconds", "maximumMilliseconds");
            var rule = new PerformanceRule(
                Text(item, "subsystem", path, 128),
                Text(item, "workName", path, 128),
                EnumText(item, "budgetClass", path, "soft-limited", "hard-limited"),
                EnumText(item, "executionKind", path, "cooperative", "non-preemptible-native", "non-preemptible-native-mutation"),
                Integer(item, "maximumPendingWaitFrames", path),
                Integer(item, "maximumConsecutiveDeferredFrames", path),
                EnumText(item, "timingMode", path, "enforce", "observe-only"),
                Integer(item, "minimumSamples", path),
                Number(item, "p95Milliseconds", path),
                Number(item, "p99Milliseconds", path),
                Number(item, "maximumMilliseconds", path));
            if (rule.P95Milliseconds > rule.P99Milliseconds || rule.P99Milliseconds > rule.MaximumMilliseconds)
            {
                throw Invalid(path, "timing targets are contradictory");
            }

            if (rule.ExecutionKind == "cooperative" != (rule.TimingMode == "enforce"))
            {
                throw Invalid(path, "only cooperative work may enforce timing in profile V1");
            }

            rules.Add(rule);
        }

        RequireSortedUnique(rules.Select(Identity).ToList(), "$profile.rules");
        return new PerformanceProfile(
            Text(root, "schemaId", "$profile", 128),
            checked((int)Integer(root, "schemaVersion", "$profile")),
            Text(root, "profileId", "$profile", 128),
            checked((int)Integer(root, "profileVersion", "$profile")),
            Text(root, "scope", "$profile", 128),
            policy,
            rules,
            sourceSha256);
    }

    private static PerformanceEvidenceDocument ParseEvidence(JsonElement root, bool requireSorted)
    {
        Object(root, "$evidence", "schemaId", "schemaVersion", "profileId", "profileVersion", "profileSha256", "scope", "metadata", "policy", "start", "end");
        Exact(root, "schemaId", "orb-modsuite-suite-performance-evidence", "$evidence");
        Exact(root, "schemaVersion", 1, "$evidence");
        Exact(root, "profileId", "supported-suite-beta-v1", "$evidence");
        Exact(root, "profileVersion", 1, "$evidence");
        Exact(root, "scope", "full-supported-suite", "$evidence");
        var profileSha = Text(root, "profileSha256", "$evidence", 64);
        if (profileSha.Length != 64 || profileSha.Any(character => !Uri.IsHexDigit(character)) || profileSha != profileSha.ToLowerInvariant())
        {
            throw Invalid("$evidence.profileSha256", "must be a lowercase SHA-256 digest");
        }

        var metadata = ParseMetadata(Required(root, "metadata", JsonValueKind.Object, "$evidence"));
        var policy = ParsePolicy(Required(root, "policy", JsonValueKind.Object, "$evidence"), "$evidence.policy");
        var start = ParsePoint(Required(root, "start", JsonValueKind.Object, "$evidence"), "$evidence.start", requireSorted);
        var end = ParsePoint(Required(root, "end", JsonValueKind.Object, "$evidence"), "$evidence.end", requireSorted);
        return new PerformanceEvidenceDocument(
            Text(root, "schemaId", "$evidence", 128),
            checked((int)Integer(root, "schemaVersion", "$evidence")),
            Text(root, "profileId", "$evidence", 128),
            checked((int)Integer(root, "profileVersion", "$evidence")),
            profileSha,
            Text(root, "scope", "$evidence", 128),
            metadata,
            policy,
            start,
            end);
    }

    private static EvidenceMetadata ParseMetadata(JsonElement value)
    {
        const string path = "$evidence.metadata";
        Object(value, path, "captureKind", "sourceCommit", "suiteVersion", "gameVersion", "scenario", "durationFrames", "capturedAtUtc");
        var captured = Text(value, "capturedAtUtc", path, 128);
        if (!DateTimeOffset.TryParseExact(captured, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
        {
            throw Invalid(path + ".capturedAtUtc", "must be canonical UTC with seven fractional digits");
        }

        return new EvidenceMetadata(
            EnumText(value, "captureKind", path, "synthetic", "windows-desktop", "steam-deck-proton"),
            Text(value, "sourceCommit", path, 128),
            Text(value, "suiteVersion", path, 128),
            Text(value, "gameVersion", path, 128),
            Text(value, "scenario", path, 64),
            Integer(value, "durationFrames", path),
            captured);
    }

    private static EvidencePolicy ParsePolicy(JsonElement value, string path)
    {
        Object(value, path, "softBudgetMilliseconds", "hardBudgetMilliseconds", "nativeMutationAdmissionsPerFrame");
        var soft = Number(value, "softBudgetMilliseconds", path);
        var hard = Number(value, "hardBudgetMilliseconds", path);
        if (soft > hard)
        {
            throw Invalid(path, "soft budget cannot exceed hard budget");
        }

        return new EvidencePolicy(soft, hard, Integer(value, "nativeMutationAdmissionsPerFrame", path));
    }

    private static EvidencePoint ParsePoint(JsonElement value, string path, bool requireSorted)
    {
        Object(value, path, "coordinator", "work");
        var coordinator = ParseCoordinator(Required(value, "coordinator", JsonValueKind.Object, path), path + ".coordinator");
        var workArray = Required(value, "work", JsonValueKind.Array, path);
        if (workArray.GetArrayLength() > MaximumItems)
        {
            throw Invalid(path + ".work", "cannot exceed 64 items");
        }

        var work = new List<EvidenceWork>(workArray.GetArrayLength());
        foreach (var item in workArray.EnumerateArray())
        {
            work.Add(ParseWork(item, $"{path}.work[{work.Count}]"));
        }

        var identities = work.Select(Identity).ToList();
        if (requireSorted)
        {
            RequireSortedUnique(identities, path + ".work");
        }
        else if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Count)
        {
            throw Invalid(path + ".work", "contains duplicate identities");
        }

        return new EvidencePoint(coordinator, work);
    }

    private static EvidenceCoordinator ParseCoordinator(JsonElement value, string path)
    {
        Object(value, path, "hasFrame", "frameIdentity", "frameElapsedMilliseconds", "hardBudgetExceeded", "nativeMutationAdmitted", "frameTiming");
        return new EvidenceCoordinator(
            Boolean(value, "hasFrame", path),
            SignedInteger(value, "frameIdentity", path),
            Number(value, "frameElapsedMilliseconds", path),
            Boolean(value, "hardBudgetExceeded", path),
            Boolean(value, "nativeMutationAdmitted", path),
            ParseTiming(Required(value, "frameTiming", JsonValueKind.Object, path), path + ".frameTiming"));
    }

    private static EvidenceWork ParseWork(JsonElement value, string path)
    {
        Object(value, path,
            "subsystem", "workName", "budgetClass", "executionKind", "isEnabled", "isPending", "isDisposed",
            "currentPendingWaitFrames", "maximumPendingWaitFrames", "starvationThresholdFrames", "isStarved", "starvationEvents",
            "admittedWorkItems", "completedWorkItems", "failedWorkItems", "abandonedWorkItems", "totalOperations",
            "deferredAttempts", "deferredFrames", "consecutiveDeferredFrames", "maximumConsecutiveDeferredFrames", "deferralsByReason",
            "nativeLeaseAdmissions", "nativeMutationLeaseAdmissions", "nativeCallsAttempted", "nativeMutationAttempts", "nativeMutationsCommitted",
            "nativeHardBudgetOverruns", "measurementFailures", "workItemTiming", "frameTiming");
        var item = new EvidenceWork(
            Text(value, "subsystem", path, 128), Text(value, "workName", path, 128),
            EnumText(value, "budgetClass", path, "soft-limited", "hard-limited"),
            EnumText(value, "executionKind", path, "cooperative", "non-preemptible-native", "non-preemptible-native-mutation"),
            Boolean(value, "isEnabled", path), Boolean(value, "isPending", path), Boolean(value, "isDisposed", path),
            Integer(value, "currentPendingWaitFrames", path), Integer(value, "maximumPendingWaitFrames", path), Integer(value, "starvationThresholdFrames", path),
            Boolean(value, "isStarved", path), Integer(value, "starvationEvents", path), Integer(value, "admittedWorkItems", path),
            Integer(value, "completedWorkItems", path), Integer(value, "failedWorkItems", path), Integer(value, "abandonedWorkItems", path),
            Integer(value, "totalOperations", path), Integer(value, "deferredAttempts", path), Integer(value, "deferredFrames", path),
            Integer(value, "consecutiveDeferredFrames", path), Integer(value, "maximumConsecutiveDeferredFrames", path),
            ParseDeferrals(Required(value, "deferralsByReason", JsonValueKind.Object, path), path + ".deferralsByReason"),
            Integer(value, "nativeLeaseAdmissions", path), Integer(value, "nativeMutationLeaseAdmissions", path), Integer(value, "nativeCallsAttempted", path),
            Integer(value, "nativeMutationAttempts", path), Integer(value, "nativeMutationsCommitted", path), Integer(value, "nativeHardBudgetOverruns", path),
            Integer(value, "measurementFailures", path), ParseTiming(Required(value, "workItemTiming", JsonValueKind.Object, path), path + ".workItemTiming"),
            ParseTiming(Required(value, "frameTiming", JsonValueKind.Object, path), path + ".frameTiming"));
        ValidateWork(item, path);
        return item;
    }

    private static DeferralCounts ParseDeferrals(JsonElement value, string path)
    {
        Object(value, path, "workInProgress", "waitingForTurn", "softBudgetExhausted", "hardBudgetExhausted", "nativeMutationAlreadyAdmitted");
        return new DeferralCounts(
            Integer(value, "workInProgress", path), Integer(value, "waitingForTurn", path), Integer(value, "softBudgetExhausted", path),
            Integer(value, "hardBudgetExhausted", path), Integer(value, "nativeMutationAlreadyAdmitted", path));
    }

    private static EvidenceTiming ParseTiming(JsonElement value, string path)
    {
        Object(value, path, "capacity", "sampleCount", "totalSamples", "operations", "totalOperations", "averageMilliseconds", "maximumMilliseconds", "p95Milliseconds", "p99Milliseconds");
        var timing = new EvidenceTiming(
            Integer(value, "capacity", path), Integer(value, "sampleCount", path), Integer(value, "totalSamples", path),
            Integer(value, "operations", path), Integer(value, "totalOperations", path), Number(value, "averageMilliseconds", path),
            Number(value, "maximumMilliseconds", path), Number(value, "p95Milliseconds", path), Number(value, "p99Milliseconds", path));
        ValidateTiming(timing, path);
        return timing;
    }

    private static void ValidateWork(EvidenceWork item, string path)
    {
        if (item.CompletedWorkItems > item.AdmittedWorkItems ||
            item.FailedWorkItems > item.AdmittedWorkItems - item.CompletedWorkItems)
            throw Invalid(path, "completed plus failed cannot exceed admitted");
        if (item.AbandonedWorkItems > item.FailedWorkItems)
            throw Invalid(path, "abandoned cannot exceed failed");
        if (item.NativeMutationsCommitted > item.NativeMutationAttempts || item.NativeMutationAttempts > item.NativeCallsAttempted)
            throw Invalid(path, "committed must be <= mutation attempts <= native calls attempted");
        if (item.MaximumPendingWaitFrames < item.CurrentPendingWaitFrames || item.MaximumConsecutiveDeferredFrames < item.ConsecutiveDeferredFrames)
            throw Invalid(path, "maximum wait/deferral counters are contradictory");
        var reasonCounts = new[]
        {
            item.DeferralsByReason.WorkInProgress,
            item.DeferralsByReason.WaitingForTurn,
            item.DeferralsByReason.SoftBudgetExhausted,
            item.DeferralsByReason.HardBudgetExhausted,
            item.DeferralsByReason.NativeMutationAlreadyAdmitted,
        };
        var reasonRemaining = item.DeferredAttempts;
        for (var index = 0; index < reasonCounts.Length; index++)
        {
            if (reasonCounts[index] > reasonRemaining)
                throw Invalid(path, "deferral reasons cannot exceed deferred attempts");
            reasonRemaining -= reasonCounts[index];
        }
        if (reasonRemaining < 0)
            throw Invalid(path, "deferral reasons cannot exceed deferred attempts");
    }

    private static void ValidateTiming(EvidenceTiming timing, string path)
    {
        ValidateTimingCounts(timing.Capacity, timing.SampleCount, timing.TotalSamples, timing.Operations, timing.TotalOperations, path);
        const double roundingTolerance = 1e-9;
        if (timing.P95Milliseconds > timing.P99Milliseconds + roundingTolerance ||
            timing.P99Milliseconds > timing.MaximumMilliseconds + roundingTolerance ||
            timing.AverageMilliseconds > timing.MaximumMilliseconds + roundingTolerance)
            throw Invalid(path, "timing distribution is contradictory");
    }

    private static void ValidateTimingCounts(long capacity, long sampleCount, long totalSamples, long operations, long totalOperations, string path)
    {
        if (capacity <= 0 || sampleCount != Math.Min(totalSamples, capacity) || operations > totalOperations)
            throw Invalid(path, "rolling-window counts are contradictory");
    }

    private static void ValidateCompatibility(PerformanceProfile profile, PerformanceEvidenceDocument evidence)
    {
        if (profile.SchemaVersion != 1 || evidence.SchemaVersion != 1 || profile.ProfileVersion != 1 || evidence.ProfileVersion != 1)
            throw Invalid("compatibility", "unknown schema or profile version");
        if (profile.ProfileId != evidence.ProfileId || profile.SourceSha256 != evidence.ProfileSha256)
            throw Invalid("compatibility", "evidence/profile id or SHA-256 mismatch");
        if (profile.Scope != evidence.Scope || evidence.Scope != "full-supported-suite")
            throw Invalid("compatibility", "scope mismatch");
        if (profile.Policy != evidence.Policy)
            throw Invalid("compatibility", "coordinator policy mismatch");
        if (evidence.Start.Work.Count != profile.Rules.Count || evidence.End.Work.Count != profile.Rules.Count)
            throw Invalid("compatibility", "full-supported-suite evidence has missing or unknown work");

        for (var index = 0; index < profile.Rules.Count; index++)
        {
            var expected = Identity(profile.Rules[index]);
            if (Identity(evidence.Start.Work[index]) != expected || Identity(evidence.End.Work[index]) != expected)
                throw Invalid("compatibility", $"missing or unknown work identity at ordinal {index}: expected {expected}");
            ValidateMonotonic(evidence.Start.Work[index], evidence.End.Work[index], expected);
        }

        ValidateTimingPair(
            evidence.Start.Coordinator.FrameTiming,
            evidence.End.Coordinator.FrameTiming,
            "coordinator",
            "frameTiming");
    }

    private static void ValidateMonotonic(EvidenceWork start, EvidenceWork end, string identity)
    {
        ValidateWork(start, identity + ".start");
        ValidateWork(end, identity + ".end");
        if (end.MaximumPendingWaitFrames < start.MaximumPendingWaitFrames)
            throw Invalid("compatibility", $"lifetime maximum moved backward for {identity} / maximumPendingWaitFrames");
        if (end.MaximumConsecutiveDeferredFrames < start.MaximumConsecutiveDeferredFrames)
            throw Invalid("compatibility", $"lifetime maximum moved backward for {identity} / maximumConsecutiveDeferredFrames");
        if (end.StarvationThresholdFrames != start.StarvationThresholdFrames)
            throw Invalid("compatibility", $"starvation threshold changed for {identity}");
        _ = Delta(start.StarvationEvents, end.StarvationEvents, identity, "starvationEvents");
        _ = Delta(start.AdmittedWorkItems, end.AdmittedWorkItems, identity, "admittedWorkItems");
        _ = Delta(start.CompletedWorkItems, end.CompletedWorkItems, identity, "completedWorkItems");
        _ = Delta(start.FailedWorkItems, end.FailedWorkItems, identity, "failedWorkItems");
        _ = Delta(start.AbandonedWorkItems, end.AbandonedWorkItems, identity, "abandonedWorkItems");
        _ = Delta(start.TotalOperations, end.TotalOperations, identity, "totalOperations");
        _ = Delta(start.DeferredAttempts, end.DeferredAttempts, identity, "deferredAttempts");
        _ = Delta(start.DeferredFrames, end.DeferredFrames, identity, "deferredFrames");
        _ = Delta(start.DeferralsByReason.WorkInProgress, end.DeferralsByReason.WorkInProgress, identity, "deferralsByReason.workInProgress");
        _ = Delta(start.DeferralsByReason.WaitingForTurn, end.DeferralsByReason.WaitingForTurn, identity, "deferralsByReason.waitingForTurn");
        _ = Delta(start.DeferralsByReason.SoftBudgetExhausted, end.DeferralsByReason.SoftBudgetExhausted, identity, "deferralsByReason.softBudgetExhausted");
        _ = Delta(start.DeferralsByReason.HardBudgetExhausted, end.DeferralsByReason.HardBudgetExhausted, identity, "deferralsByReason.hardBudgetExhausted");
        _ = Delta(start.DeferralsByReason.NativeMutationAlreadyAdmitted, end.DeferralsByReason.NativeMutationAlreadyAdmitted, identity, "deferralsByReason.nativeMutationAlreadyAdmitted");
        _ = Delta(start.NativeLeaseAdmissions, end.NativeLeaseAdmissions, identity, "nativeLeaseAdmissions");
        _ = Delta(start.NativeMutationLeaseAdmissions, end.NativeMutationLeaseAdmissions, identity, "nativeMutationLeaseAdmissions");
        _ = Delta(start.NativeCallsAttempted, end.NativeCallsAttempted, identity, "nativeCallsAttempted");
        _ = Delta(start.NativeMutationAttempts, end.NativeMutationAttempts, identity, "nativeMutationAttempts");
        _ = Delta(start.NativeMutationsCommitted, end.NativeMutationsCommitted, identity, "nativeMutationsCommitted");
        _ = Delta(start.NativeHardBudgetOverruns, end.NativeHardBudgetOverruns, identity, "nativeHardBudgetOverruns");
        _ = Delta(start.MeasurementFailures, end.MeasurementFailures, identity, "measurementFailures");
        ValidateTimingPair(start.WorkItemTiming, end.WorkItemTiming, identity, "workItemTiming");
        ValidateTimingPair(start.FrameTiming, end.FrameTiming, identity, "frameTiming");
    }

    private static void ValidateTimingPair(EvidenceTiming start, EvidenceTiming end, string identity, string metric)
    {
        ValidateTiming(start, identity + ".start." + metric);
        ValidateTiming(end, identity + ".end." + metric);
        if (start.Capacity != end.Capacity)
            throw Invalid("compatibility", $"rolling capacity changed for {identity} / {metric}");
        _ = Delta(start.TotalSamples, end.TotalSamples, identity, metric + ".totalSamples");
        _ = Delta(start.TotalOperations, end.TotalOperations, identity, metric + ".totalOperations");
    }

    private static void EvaluateZeroDelta(List<MetricEvaluation> results, string identity, string metric, long start, long end)
    {
        var observed = Delta(start, end, identity, metric);
        results.Add(new(identity, metric, observed == 0 ? "within-target" : "exceeded", observed, 0, null));
    }

    private static void EvaluateCumulativeMaximum(List<MetricEvaluation> results, string identity, string metric, long start, long end, long target)
    {
        var classification = end <= target
            ? "within-target"
            : end > start
                ? "exceeded"
                : "insufficient-window";
        var note = classification == "insufficient-window"
            ? "the lifetime maximum already exceeded the target before this capture"
            : null;
        results.Add(new(identity, metric, classification, end, target, note));
    }

    private static void EvaluateTiming(List<MetricEvaluation> results, string identity, string prefix, PerformanceRule rule, EvidenceTiming start, EvidenceTiming end, bool observeOnly)
    {
        var addedSamples = Delta(start.TotalSamples, end.TotalSamples, identity, prefix + ".totalSamples");
        if (start.SampleCount != 0 && addedSamples < end.Capacity)
        {
            results.Add(new(identity, prefix, "insufficient-window", addedSamples, end.Capacity, "rolling percentiles may contain pre-capture samples"));
            return;
        }

        if (addedSamples < rule.MinimumSamples || end.SampleCount < rule.MinimumSamples)
        {
            results.Add(new(identity, prefix, "insufficient-samples", addedSamples, rule.MinimumSamples, null));
            return;
        }

        if (observeOnly)
        {
            AddObserveOnlyTimingMetric(results, identity, prefix + ".p95Milliseconds", end.P95Milliseconds, rule.P95Milliseconds);
            AddObserveOnlyTimingMetric(results, identity, prefix + ".p99Milliseconds", end.P99Milliseconds, rule.P99Milliseconds);
            AddObserveOnlyTimingMetric(results, identity, prefix + ".maximumMilliseconds", end.MaximumMilliseconds, rule.MaximumMilliseconds);
            return;
        }

        AddTimingMetric(results, identity, prefix + ".p95Milliseconds", end.P95Milliseconds, rule.P95Milliseconds);
        AddTimingMetric(results, identity, prefix + ".p99Milliseconds", end.P99Milliseconds, rule.P99Milliseconds);
        AddTimingMetric(results, identity, prefix + ".maximumMilliseconds", end.MaximumMilliseconds, rule.MaximumMilliseconds);
    }

    private static void AddTimingMetric(List<MetricEvaluation> results, string identity, string metric, double observed, double target) =>
        results.Add(new(identity, metric, observed <= target ? "within-target" : "exceeded", observed, target, null));

    private static void AddObserveOnlyTimingMetric(List<MetricEvaluation> results, string identity, string metric, double observed, double target) =>
        results.Add(new(identity, metric, "observe-only", observed, target, "native timing is not enforced in profile V1"));

    private static void EvaluateCoordinatorTiming(
        List<MetricEvaluation> results,
        PerformanceProfile profile,
        PerformanceEvidenceDocument evidence)
    {
        const string identity = "coordinator";
        const string prefix = "activeFrameTiming";
        var start = evidence.Start.Coordinator.FrameTiming;
        var end = evidence.End.Coordinator.FrameTiming;
        var addedSamples = Delta(start.TotalSamples, end.TotalSamples, identity, prefix + ".totalSamples");
        if (start.SampleCount != 0 && addedSamples < end.Capacity)
        {
            results.Add(new(identity, prefix, "insufficient-window", addedSamples, end.Capacity, "rolling active-frame percentiles may contain pre-capture samples"));
            return;
        }

        var minimumSamples = profile.Rules[0].MinimumSamples;
        if (addedSamples < minimumSamples || end.SampleCount < minimumSamples)
        {
            results.Add(new(identity, prefix, "insufficient-samples", addedSamples, minimumSamples, null));
            return;
        }

        AddTimingMetric(results, identity, prefix + ".p95Milliseconds", end.P95Milliseconds, evidence.Policy.SoftBudgetMilliseconds);
        AddTimingMetric(results, identity, prefix + ".p99Milliseconds", end.P99Milliseconds, evidence.Policy.HardBudgetMilliseconds);
        AddTimingMetric(results, identity, prefix + ".maximumMilliseconds", end.MaximumMilliseconds, evidence.Policy.HardBudgetMilliseconds);
    }

    private static long Delta(long start, long end, string identity, string metric)
    {
        if (end < start)
            throw Invalid("compatibility", $"counter moved backward for {identity} / {metric}");
        return end - start;
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes)
            throw Invalid(path, "exceeds 1 MiB");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static JsonDocument ParseDocument(byte[] bytes, string source)
    {
        if (bytes.Length > MaximumBytes)
            throw Invalid(source, "exceeds 1 MiB");
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            throw Invalid(source, "UTF-8 BOM is not allowed");
        try
        {
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
        }
        catch (JsonException exception)
        {
            throw Invalid(source, $"invalid strict JSON: {exception.Message}");
        }
    }

    private static void Object(JsonElement value, string path, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid(path, "must be an object");
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedExact = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!exact.Add(property.Name))
                throw Invalid(path, $"duplicate property '{property.Name}'");
            if (!folded.Add(property.Name))
                throw Invalid(path, $"case-collision property '{property.Name}'");
            if (!allowedExact.Contains(property.Name))
                throw Invalid(path, $"unknown property '{property.Name}'");
        }
        foreach (var name in allowed)
            if (!exact.Contains(name)) throw Invalid(path, $"missing property '{name}'");
    }

    private static JsonElement Required(JsonElement value, string name, JsonValueKind kind, string path)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != kind)
            throw Invalid(path + "." + name, $"must be {kind}");
        return property;
    }

    private static string Text(JsonElement value, string name, string path, int maximum)
    {
        var property = Required(value, name, JsonValueKind.String, path);
        var result = property.GetString()!;
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximum || result.Any(char.IsControl))
            throw Invalid(path + "." + name, $"must contain 1-{maximum} non-control characters");
        return result;
    }

    private static string EnumText(JsonElement value, string name, string path, params string[] allowed)
    {
        var result = Text(value, name, path, 128);
        if (!allowed.Contains(result, StringComparer.Ordinal))
            throw Invalid(path + "." + name, $"unknown enum '{result}'");
        return result;
    }

    private static long Integer(JsonElement value, string name, string path)
    {
        var result = SignedInteger(value, name, path);
        if (result < 0) throw Invalid(path + "." + name, "cannot be negative");
        return result;
    }

    private static long SignedInteger(JsonElement value, string name, string path)
    {
        var property = Required(value, name, JsonValueKind.Number, path);
        if (!property.TryGetInt64(out var result)) throw Invalid(path + "." + name, "must be an Int64");
        return result;
    }

    private static double Number(JsonElement value, string name, string path)
    {
        var property = Required(value, name, JsonValueKind.Number, path);
        if (!property.TryGetDouble(out var result) || !double.IsFinite(result) || result < 0)
            throw Invalid(path + "." + name, "must be finite and non-negative");
        return result;
    }

    private static bool Boolean(JsonElement value, string name, string path)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False)
            throw Invalid(path + "." + name, "must be a Boolean");
        return property.GetBoolean();
    }

    private static void Exact(JsonElement value, string name, string expected, string path)
    {
        if (Text(value, name, path, 128) != expected) throw Invalid(path + "." + name, $"must equal '{expected}'");
    }

    private static void Exact(JsonElement value, string name, long expected, string path)
    {
        if (Integer(value, name, path) != expected) throw Invalid(path + "." + name, $"must equal {expected}");
    }

    private static void RequireSortedUnique(IReadOnlyList<string> identities, string path)
    {
        for (var index = 1; index < identities.Count; index++)
        {
            var comparison = string.CompareOrdinal(identities[index - 1], identities[index]);
            if (comparison == 0) throw Invalid(path, $"duplicate identity '{identities[index]}'");
            if (comparison > 0) throw Invalid(path, "identities must be ordinally sorted");
        }
    }

    private static void SortWork(List<EvidenceWork> work) => work.Sort((left, right) => string.CompareOrdinal(Identity(left), Identity(right)));
    private static string Identity(PerformanceRule rule) => $"{rule.Subsystem}\u001f{rule.WorkName}\u001f{rule.BudgetClass}\u001f{rule.ExecutionKind}";
    private static string Identity(EvidenceWork work) => $"{work.Subsystem}\u001f{work.WorkName}\u001f{work.BudgetClass}\u001f{work.ExecutionKind}";
    private static string EncodeMarkdownText(string value)
    {
        var builder = new StringBuilder(value.Length + 16);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\r' || character == '\n')
            {
                builder.Append(' ');
            }
            else if ((character >= 'a' && character <= 'z') ||
                     (character >= 'A' && character <= 'Z') ||
                     (character >= '0' && character <= '9') ||
                     character == ' ')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append("&#x")
                    .Append(((int)character).ToString("X", CultureInfo.InvariantCulture))
                    .Append(';');
            }
        }

        return builder.ToString();
    }
    private static InvalidDataException Invalid(string path, string message) => new($"{path}: {message}");
}

public sealed record PerformanceProfile(string SchemaId, int SchemaVersion, string ProfileId, int ProfileVersion, string Scope, EvidencePolicy Policy, List<PerformanceRule> Rules, string SourceSha256);
public sealed record PerformanceRule(string Subsystem, string WorkName, string BudgetClass, string ExecutionKind, long MaximumPendingWaitFrames, long MaximumConsecutiveDeferredFrames, string TimingMode, long MinimumSamples, double P95Milliseconds, double P99Milliseconds, double MaximumMilliseconds);
public sealed record PerformanceEvidenceDocument(string SchemaId, int SchemaVersion, string ProfileId, int ProfileVersion, string ProfileSha256, string Scope, EvidenceMetadata Metadata, EvidencePolicy Policy, EvidencePoint Start, EvidencePoint End);
public sealed record EvidenceMetadata(string CaptureKind, string SourceCommit, string SuiteVersion, string GameVersion, string Scenario, long DurationFrames, string CapturedAtUtc);
public sealed record EvidencePolicy(double SoftBudgetMilliseconds, double HardBudgetMilliseconds, long NativeMutationAdmissionsPerFrame);
public sealed record EvidencePoint(EvidenceCoordinator Coordinator, List<EvidenceWork> Work);
public sealed record EvidenceCoordinator(bool HasFrame, long FrameIdentity, double FrameElapsedMilliseconds, bool HardBudgetExceeded, bool NativeMutationAdmitted, EvidenceTiming FrameTiming);
public sealed record DeferralCounts(long WorkInProgress, long WaitingForTurn, long SoftBudgetExhausted, long HardBudgetExhausted, long NativeMutationAlreadyAdmitted);
public sealed record EvidenceTiming(long Capacity, long SampleCount, long TotalSamples, long Operations, long TotalOperations, double AverageMilliseconds, double MaximumMilliseconds, double P95Milliseconds, double P99Milliseconds);
public sealed record EvidenceWork(
    string Subsystem, string WorkName, string BudgetClass, string ExecutionKind, bool IsEnabled, bool IsPending, bool IsDisposed,
    long CurrentPendingWaitFrames, long MaximumPendingWaitFrames, long StarvationThresholdFrames, bool IsStarved, long StarvationEvents,
    long AdmittedWorkItems, long CompletedWorkItems, long FailedWorkItems, long AbandonedWorkItems, long TotalOperations,
    long DeferredAttempts, long DeferredFrames, long ConsecutiveDeferredFrames, long MaximumConsecutiveDeferredFrames, DeferralCounts DeferralsByReason,
    long NativeLeaseAdmissions, long NativeMutationLeaseAdmissions, long NativeCallsAttempted, long NativeMutationAttempts, long NativeMutationsCommitted,
    long NativeHardBudgetOverruns, long MeasurementFailures, EvidenceTiming WorkItemTiming, EvidenceTiming FrameTiming);
public sealed record MetricEvaluation(string WorkIdentity, string Metric, string Classification, double Observed, double Target, string? Note);
public sealed record PerformanceEvaluation(string ProfileId, int ProfileVersion, string ProfileSha256, string CaptureKind, string SourceCommit, string Scenario, List<MetricEvaluation> Results);
