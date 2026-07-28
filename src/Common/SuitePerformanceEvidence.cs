using System;
using System.Globalization;
using System.Text;

namespace OrbModding.Common;

public enum SuitePerformanceCaptureKind
{
    Synthetic,
    WindowsDesktop,
    SteamDeckProton,
}

public readonly struct SuitePerformanceWorkIdentity
{
    internal SuitePerformanceWorkIdentity(
        string subsystem,
        string workName,
        SuiteBudgetClass budgetClass,
        SuiteWorkExecutionKind executionKind,
        int maximumPendingWaitFrames)
    {
        Subsystem = subsystem;
        WorkName = workName;
        BudgetClass = budgetClass;
        ExecutionKind = executionKind;
        MaximumPendingWaitFrames = maximumPendingWaitFrames;
    }

    public string Subsystem { get; }
    public string WorkName { get; }
    public SuiteBudgetClass BudgetClass { get; }
    public SuiteWorkExecutionKind ExecutionKind { get; }
    public int MaximumPendingWaitFrames { get; }
}

/// <summary>
/// Stable V1 identities consumed directly by production registration sites and
/// by the offline profile audit. Changing one therefore cannot silently drift
/// runtime registration away from checked evidence policy.
/// </summary>
public static class SuitePerformanceWorkIdentities
{
    public static readonly SuitePerformanceWorkIdentity MentorMutation = new(
        "OrbMentor", "Grant one mastery XP mutation", SuiteBudgetClass.HardLimited, SuiteWorkExecutionKind.NonPreemptibleNativeMutation, 12);
    public static readonly SuitePerformanceWorkIdentity MentorEvaluate = new(
        "OrbMentor", "Reconcile, resolve, and plan XP", SuiteBudgetClass.SoftLimited, SuiteWorkExecutionKind.Cooperative, 12);
    public static readonly SuitePerformanceWorkIdentity ModConfigWork = new(
        "OrbModConfig", "Install or repair UI", SuiteBudgetClass.SoftLimited, SuiteWorkExecutionKind.Cooperative, 30);
    public static readonly SuitePerformanceWorkIdentity GameplayInvalidationDelivery = new(
        "OrbModding.Common", "Deliver gameplay invalidations", SuiteBudgetClass.SoftLimited, SuiteWorkExecutionKind.Cooperative, 12);

    public const int SupportedSuiteV1Count = 4;

    public static SuitePerformanceWorkIdentity GetSupportedSuiteV1(int index) => index switch
    {
        0 => MentorMutation,
        1 => MentorEvaluate,
        2 => ModConfigWork,
        3 => GameplayInvalidationDelivery,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal static int ResolveStarvationThresholdFrames(
        string subsystem,
        string workName,
        SuiteBudgetClass budgetClass,
        SuiteWorkExecutionKind executionKind,
        int fallbackThresholdFrames)
    {
        for (var index = 0; index < SupportedSuiteV1Count; index++)
        {
            var identity = GetSupportedSuiteV1(index);
            if (identity.BudgetClass == budgetClass &&
                identity.ExecutionKind == executionKind &&
                string.Equals(identity.Subsystem, subsystem, StringComparison.Ordinal) &&
                string.Equals(identity.WorkName, workName, StringComparison.Ordinal))
            {
                return Math.Min(fallbackThresholdFrames, identity.MaximumPendingWaitFrames);
            }
        }

        return fallbackThresholdFrames;
    }

    internal static bool HasEnforcedAdmissionBound(string subsystem, string workName) =>
        string.Equals(subsystem, ModConfigWork.Subsystem, StringComparison.Ordinal) &&
        string.Equals(workName, ModConfigWork.WorkName, StringComparison.Ordinal);
}

/// <summary>
/// Bounded metadata supplied by an explicit, low-frequency diagnostics action.
/// The schema deliberately has no host, user, save, or path fields.
/// </summary>
public sealed class SuitePerformanceCaptureMetadata
{
    public SuitePerformanceCaptureMetadata(
        SuitePerformanceCaptureKind captureKind,
        string sourceCommit,
        string suiteVersion,
        string gameVersion,
        string scenario,
        long durationFrames,
        DateTimeOffset capturedAtUtc)
    {
        if (!Enum.IsDefined(typeof(SuitePerformanceCaptureKind), captureKind))
        {
            throw new ArgumentOutOfRangeException(nameof(captureKind));
        }

        CaptureKind = captureKind;
        SourceCommit = ValidateText(sourceCommit, nameof(sourceCommit), 128);
        SuiteVersion = ValidateText(suiteVersion, nameof(suiteVersion), 128);
        GameVersion = ValidateText(gameVersion, nameof(gameVersion), 128);
        Scenario = ValidateText(scenario, nameof(scenario), 64);
        if (durationFrames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationFrames));
        }

        if (capturedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Capture time must be expressed in UTC.", nameof(capturedAtUtc));
        }

        DurationFrames = durationFrames;
        CapturedAtUtc = capturedAtUtc;
    }

    public SuitePerformanceCaptureKind CaptureKind { get; }
    public string SourceCommit { get; }
    public string SuiteVersion { get; }
    public string GameVersion { get; }
    public string Scenario { get; }
    public long DurationFrames { get; }
    public DateTimeOffset CapturedAtUtc { get; }

    private static string ValidateText(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException($"{parameterName} must contain 1-{maximumLength} characters.", parameterName);
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsControl(value[index]))
            {
                throw new ArgumentException($"{parameterName} cannot contain control characters.", parameterName);
            }
        }

        return value;
    }
}

public sealed class SuitePerformanceEvidencePoint
{
    private readonly RegistrationPerformanceSnapshot[] _work;

    internal SuitePerformanceEvidencePoint(
        SuitePerformanceCoordinator owner,
        SuiteCoordinatorSnapshot coordinator,
        RollingPerformanceDistributionSnapshot coordinatorFrameTiming,
        RegistrationPerformanceSnapshot[] work)
    {
        Owner = owner;
        Coordinator = coordinator;
        CoordinatorFrameTiming = coordinatorFrameTiming;
        _work = work;
    }

    internal SuitePerformanceCoordinator Owner { get; }
    public SuiteCoordinatorSnapshot Coordinator { get; }
    public RollingPerformanceDistributionSnapshot CoordinatorFrameTiming { get; }
    public int WorkCount => _work.Length;
    public RegistrationPerformanceSnapshot GetWork(int index) => _work[index];
}

/// <summary>
/// Immutable V1 suite evidence. Capture and serialization are explicit diagnostic
/// operations and are never called by coordinator admission or completion paths.
/// </summary>
public sealed class SuitePerformanceEvidence
{
    public const string SchemaId = "orb-modsuite-suite-performance-evidence";
    public const int SchemaVersion = 1;
    public const string ProfileId = "supported-suite-beta-v1";
    public const int ProfileVersion = 1;
    public const string ProfileSha256 = "dc47d076aba6a53c81eddb605355cda584cf8ae2fb79e699a542c1922a2f6bab";

    private readonly SuitePerformanceEvidencePoint _start;
    private readonly SuitePerformanceEvidencePoint _end;

    private SuitePerformanceEvidence(
        SuitePerformanceCaptureMetadata metadata,
        SuitePerformanceEvidencePoint start,
        SuitePerformanceEvidencePoint end)
    {
        Metadata = metadata;
        _start = start;
        _end = end;
    }

    public SuitePerformanceCaptureMetadata Metadata { get; }
    public SuitePerformanceEvidencePoint Start => _start;
    public SuitePerformanceEvidencePoint End => _end;

    public static SuitePerformanceEvidencePoint StartCapture(SuitePerformanceCoordinator coordinator)
    {
        if (coordinator is null)
        {
            throw new ArgumentNullException(nameof(coordinator));
        }

        return CapturePoint(coordinator);
    }

    public static SuitePerformanceEvidence Capture(
        SuitePerformanceCoordinator coordinator,
        SuitePerformanceCaptureMetadata metadata,
        SuitePerformanceEvidencePoint start)
    {
        if (coordinator is null)
        {
            throw new ArgumentNullException(nameof(coordinator));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (start is null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (!ReferenceEquals(start.Owner, coordinator))
        {
            throw new InvalidOperationException("Performance evidence start and end must use the same coordinator instance.");
        }

        var end = CapturePoint(coordinator);

        if (start.WorkCount != end.WorkCount)
        {
            throw new InvalidOperationException("Coordinator registrations changed during the performance evidence capture.");
        }

        for (var index = 0; index < start.WorkCount; index++)
        {
            var startWork = start.GetWork(index);
            var endWork = end.GetWork(index);
            if (CompareIdentity(startWork, endWork) != 0 || startWork.RegistrationId != endWork.RegistrationId)
            {
                throw new InvalidOperationException("Coordinator registration identity changed during the performance evidence capture.");
            }
        }

        return new SuitePerformanceEvidence(metadata, start, end);
    }

    private static SuitePerformanceEvidencePoint CapturePoint(SuitePerformanceCoordinator coordinator)
    {
        var work = coordinator.GetRegistrationSnapshots();
        if (work.Length > 64)
        {
            throw new InvalidOperationException("Suite performance evidence cannot contain more than 64 work identities.");
        }
        Array.Sort(work, CompareIdentity);
        for (var index = 1; index < work.Length; index++)
        {
            if (CompareIdentity(work[index - 1], work[index]) == 0)
            {
                throw new InvalidOperationException("Suite performance evidence cannot contain duplicate work identities.");
            }
        }

        for (var index = 0; index < work.Length; index++)
        {
            ValidateIdentityText(work[index].Subsystem, "subsystem");
            ValidateIdentityText(work[index].WorkName, "work name");
        }

        return new SuitePerformanceEvidencePoint(
            coordinator,
            coordinator.GetSnapshot(),
            coordinator.GetFrameDistributionSnapshot(),
            work);
    }

    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(8192 + ((_start.WorkCount + _end.WorkCount) * 1536));
        builder.Append('{');
        Property(builder, "schemaId", SchemaId, first: true);
        NumberProperty(builder, "schemaVersion", SchemaVersion);
        Property(builder, "profileId", ProfileId);
        NumberProperty(builder, "profileVersion", ProfileVersion);
        Property(builder, "profileSha256", ProfileSha256);
        builder.Append(",\"scope\":\"full-supported-suite\"");
        builder.Append(",\"metadata\":{");
        Property(builder, "captureKind", CaptureKindName(Metadata.CaptureKind), first: true);
        Property(builder, "sourceCommit", Metadata.SourceCommit);
        Property(builder, "suiteVersion", Metadata.SuiteVersion);
        Property(builder, "gameVersion", Metadata.GameVersion);
        Property(builder, "scenario", Metadata.Scenario);
        NumberProperty(builder, "durationFrames", Metadata.DurationFrames);
        Property(builder, "capturedAtUtc", Metadata.CapturedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
        builder.Append('}');
        builder.Append(",\"policy\":{");
        DoubleProperty(builder, "softBudgetMilliseconds", _end.Coordinator.SoftBudgetMilliseconds, first: true);
        DoubleProperty(builder, "hardBudgetMilliseconds", _end.Coordinator.HardBudgetMilliseconds);
        NumberProperty(builder, "nativeMutationAdmissionsPerFrame", 1);
        builder.Append('}');
        builder.Append(",\"start\":");
        AppendPoint(builder, _start);
        builder.Append(",\"end\":");
        AppendPoint(builder, _end);
        builder.Append("}\n");
        return builder.ToString();
    }

    private static void AppendPoint(StringBuilder builder, SuitePerformanceEvidencePoint point)
    {
        var coordinator = point.Coordinator;
        builder.Append("{\"coordinator\":{");
        BooleanProperty(builder, "hasFrame", coordinator.HasFrame, first: true);
        NumberProperty(builder, "frameIdentity", coordinator.FrameIdentity);
        DoubleProperty(builder, "frameElapsedMilliseconds", coordinator.FrameElapsedMilliseconds);
        BooleanProperty(builder, "hardBudgetExceeded", coordinator.HardBudgetExceeded);
        BooleanProperty(builder, "nativeMutationAdmitted", coordinator.NativeMutationAdmitted);
        builder.Append(",\"frameTiming\":");
        AppendTiming(builder, point.CoordinatorFrameTiming);
        builder.Append("},\"work\":[");
        for (var index = 0; index < point.WorkCount; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }

            AppendWork(builder, point.GetWork(index));
        }

        builder.Append("]}");
    }

    private static int CompareIdentity(RegistrationPerformanceSnapshot left, RegistrationPerformanceSnapshot right)
    {
        var comparison = string.CompareOrdinal(left.Subsystem, right.Subsystem);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.WorkName, right.WorkName);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(BudgetClassName(left.BudgetClass), BudgetClassName(right.BudgetClass));
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(ExecutionKindName(left.ExecutionKind), ExecutionKindName(right.ExecutionKind));
    }

    private static void AppendWork(StringBuilder builder, RegistrationPerformanceSnapshot work)
    {
        builder.Append('{');
        Property(builder, "subsystem", work.Subsystem, first: true);
        Property(builder, "workName", work.WorkName);
        Property(builder, "budgetClass", BudgetClassName(work.BudgetClass));
        Property(builder, "executionKind", ExecutionKindName(work.ExecutionKind));
        BooleanProperty(builder, "isEnabled", work.IsEnabled);
        BooleanProperty(builder, "isPending", work.IsPending);
        BooleanProperty(builder, "isDisposed", work.IsDisposed);
        NumberProperty(builder, "currentPendingWaitFrames", work.CurrentPendingWaitFrames);
        NumberProperty(builder, "maximumPendingWaitFrames", work.MaximumPendingWaitFrames);
        NumberProperty(builder, "starvationThresholdFrames", work.StarvationThresholdFrames);
        BooleanProperty(builder, "isStarved", work.IsStarved);
        NumberProperty(builder, "starvationEvents", work.StarvationEvents);
        NumberProperty(builder, "admittedWorkItems", work.AdmittedWorkItems);
        NumberProperty(builder, "completedWorkItems", work.CompletedWorkItems);
        NumberProperty(builder, "failedWorkItems", work.FailedWorkItems);
        NumberProperty(builder, "abandonedWorkItems", work.AbandonedWorkItems);
        NumberProperty(builder, "totalOperations", work.TotalOperations);
        NumberProperty(builder, "deferredAttempts", work.DeferredAttempts);
        NumberProperty(builder, "deferredFrames", work.DeferredFrames);
        NumberProperty(builder, "consecutiveDeferredFrames", work.ConsecutiveDeferredFrames);
        NumberProperty(builder, "maximumConsecutiveDeferredFrames", work.MaximumConsecutiveDeferredFrames);
        builder.Append(",\"deferralsByReason\":{");
        NumberProperty(builder, "workInProgress", work.DeferralsByReason.WorkInProgress, first: true);
        NumberProperty(builder, "waitingForTurn", work.DeferralsByReason.WaitingForTurn);
        NumberProperty(builder, "softBudgetExhausted", work.DeferralsByReason.SoftBudgetExhausted);
        NumberProperty(builder, "hardBudgetExhausted", work.DeferralsByReason.HardBudgetExhausted);
        NumberProperty(builder, "nativeMutationAlreadyAdmitted", work.DeferralsByReason.NativeMutationAlreadyAdmitted);
        builder.Append('}');
        NumberProperty(builder, "nativeLeaseAdmissions", work.NativeLeaseAdmissions);
        NumberProperty(builder, "nativeMutationLeaseAdmissions", work.NativeMutationLeaseAdmissions);
        NumberProperty(builder, "nativeCallsAttempted", work.NativeCallsAttempted);
        NumberProperty(builder, "nativeMutationAttempts", work.NativeMutationAttempts);
        NumberProperty(builder, "nativeMutationsCommitted", work.NativeMutationsCommitted);
        NumberProperty(builder, "nativeHardBudgetOverruns", work.NativeHardBudgetOverruns);
        NumberProperty(builder, "measurementFailures", work.MeasurementFailures);
        builder.Append(",\"workItemTiming\":");
        AppendTiming(builder, work.WorkItemTiming);
        builder.Append(",\"frameTiming\":");
        AppendTiming(builder, work.FrameTiming);
        builder.Append('}');
    }

    private static void AppendTiming(StringBuilder builder, RollingPerformanceDistributionSnapshot timing)
    {
        builder.Append('{');
        NumberProperty(builder, "capacity", timing.Capacity, first: true);
        NumberProperty(builder, "sampleCount", timing.SampleCount);
        NumberProperty(builder, "totalSamples", timing.TotalSamples);
        NumberProperty(builder, "operations", timing.Operations);
        NumberProperty(builder, "totalOperations", timing.TotalOperations);
        DoubleProperty(builder, "averageMilliseconds", timing.AverageMilliseconds);
        DoubleProperty(builder, "maximumMilliseconds", timing.MaximumMilliseconds);
        DoubleProperty(builder, "p95Milliseconds", timing.P95Milliseconds);
        DoubleProperty(builder, "p99Milliseconds", timing.P99Milliseconds);
        builder.Append('}');
    }

    private static void AppendTiming(StringBuilder builder, RollingPerformanceSnapshot timing)
    {
        builder.Append('{');
        NumberProperty(builder, "capacity", timing.Capacity, first: true);
        NumberProperty(builder, "sampleCount", timing.SampleCount);
        NumberProperty(builder, "totalSamples", timing.TotalSamples);
        NumberProperty(builder, "operations", timing.Operations);
        NumberProperty(builder, "totalOperations", timing.TotalOperations);
        DoubleProperty(builder, "averageMilliseconds", timing.AverageMilliseconds);
        DoubleProperty(builder, "maximumMilliseconds", timing.MaximumMilliseconds);
        DoubleProperty(builder, "percentile", timing.Percentile);
        DoubleProperty(builder, "percentileMilliseconds", timing.PercentileMilliseconds);
        builder.Append('}');
    }

    private static string CaptureKindName(SuitePerformanceCaptureKind value) => value switch
    {
        SuitePerformanceCaptureKind.Synthetic => "synthetic",
        SuitePerformanceCaptureKind.WindowsDesktop => "windows-desktop",
        SuitePerformanceCaptureKind.SteamDeckProton => "steam-deck-proton",
        _ => throw new InvalidOperationException("Unknown capture kind."),
    };

    private static string BudgetClassName(SuiteBudgetClass value) => value switch
    {
        SuiteBudgetClass.SoftLimited => "soft-limited",
        SuiteBudgetClass.HardLimited => "hard-limited",
        _ => throw new InvalidOperationException("Unknown budget class."),
    };

    private static string ExecutionKindName(SuiteWorkExecutionKind value) => value switch
    {
        SuiteWorkExecutionKind.Cooperative => "cooperative",
        SuiteWorkExecutionKind.NonPreemptibleNative => "non-preemptible-native",
        SuiteWorkExecutionKind.NonPreemptibleNativeMutation => "non-preemptible-native-mutation",
        _ => throw new InvalidOperationException("Unknown execution kind."),
    };

    private static void Property(StringBuilder builder, string name, string value, bool first = false)
    {
        if (!first)
        {
            builder.Append(',');
        }

        AppendQuoted(builder, name);
        builder.Append(':');
        AppendQuoted(builder, value);
    }

    private static void NumberProperty(StringBuilder builder, string name, long value, bool first = false)
    {
        if (!first)
        {
            builder.Append(',');
        }

        AppendQuoted(builder, name);
        builder.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void DoubleProperty(StringBuilder builder, string name, double value, bool first = false)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
        {
            throw new InvalidOperationException("Performance evidence cannot contain invalid timing values.");
        }

        if (!first)
        {
            builder.Append(',');
        }

        AppendQuoted(builder, name);
        builder.Append(':').Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void BooleanProperty(StringBuilder builder, string name, bool value, bool first = false)
    {
        if (!first)
        {
            builder.Append(',');
        }

        AppendQuoted(builder, name);
        builder.Append(value ? ":true" : ":false");
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static void ValidateIdentityText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new InvalidOperationException($"Performance evidence {field} must contain 1-128 characters.");
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsControl(value[index]))
            {
                throw new InvalidOperationException($"Performance evidence {field} cannot contain control characters.");
            }
        }
    }
}
