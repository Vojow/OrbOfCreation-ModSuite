using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public static class ServiceCycleReplayArtifactFormat
{
    public const ushort SchemaVersion = 1;
    public const ushort EmbeddedSemanticSchemaVersion = 5;
    public const int HeaderBytes = 96;
    public const int DirectoryEntryBytes = 40;
    public const int RequiredSectionCount = 6;
    public const int ManifestBytes = 224;
    public const int CodecManifestEntryBytes = 24;
    public const int RecordIndexEntryBytes = 88;
    public const int CycleFooterBytes = 768;
    public const int ProjectionEntryBytes = 24;
    public const int MaximumArtifactBytes = 134_217_728;
}

public enum ServiceCycleReplaySectionKind : ushort
{
    Manifest = 1,
    SemanticTrace = 2,
    CodecManifest = 3,
    ReplayRecordIndex = 4,
    ReplayPayload = 5,
    CycleFooters = 6,
}

public enum ServiceCycleReplayCodecRole : ushort
{
    CycleInput = 1,
    State = 2,
    Action = 3,
}

public readonly struct ServiceCycleReplayCodecManifestEntry : IEquatable<ServiceCycleReplayCodecManifestEntry>
{
    public ServiceCycleReplayCodecManifestEntry(
        int traceServiceKey,
        ServiceCycleReplayCodecRole role,
        ServiceCycleReplayCodecDescriptor descriptor)
    {
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        if (role is < ServiceCycleReplayCodecRole.CycleInput or > ServiceCycleReplayCodecRole.Action)
            throw new ArgumentOutOfRangeException(nameof(role));
        if (!descriptor.IsValid) throw new ArgumentException("A valid replay codec descriptor is required.", nameof(descriptor));
        TraceServiceKey = traceServiceKey;
        Role = role;
        Descriptor = descriptor;
    }

    public int TraceServiceKey { get; }
    public ServiceCycleReplayCodecRole Role { get; }
    public ServiceCycleReplayCodecDescriptor Descriptor { get; }
    public bool IsCanonical => TraceServiceKey > 0 &&
        Role is >= ServiceCycleReplayCodecRole.CycleInput and <= ServiceCycleReplayCodecRole.Action &&
        Descriptor.IsValid;
    public bool Equals(ServiceCycleReplayCodecManifestEntry other) =>
        TraceServiceKey == other.TraceServiceKey && Role == other.Role && Descriptor == other.Descriptor;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayCodecManifestEntry other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TraceServiceKey, (int)Role, Descriptor);
    public static bool operator ==(
        ServiceCycleReplayCodecManifestEntry left,
        ServiceCycleReplayCodecManifestEntry right) => left.Equals(right);
    public static bool operator !=(
        ServiceCycleReplayCodecManifestEntry left,
        ServiceCycleReplayCodecManifestEntry right) => !left.Equals(right);
}

public readonly struct ServiceCycleReplayArtifactLimits
{
    public ServiceCycleReplayArtifactLimits(
        int maximumArtifactBytes,
        int maximumSemanticEvents,
        int maximumCodecEntries,
        int maximumRecords,
        int maximumCycleFooters)
    {
        if (maximumArtifactBytes is <= 0 or > ServiceCycleReplayArtifactFormat.MaximumArtifactBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes));
        if (maximumSemanticEvents < 0 || maximumCodecEntries < 0 || maximumRecords < 0 || maximumCycleFooters < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSemanticEvents));
        MaximumArtifactBytes = maximumArtifactBytes;
        MaximumSemanticEvents = maximumSemanticEvents;
        MaximumCodecEntries = maximumCodecEntries;
        MaximumRecords = maximumRecords;
        MaximumCycleFooters = maximumCycleFooters;
    }

    public static ServiceCycleReplayArtifactLimits Default => new(
        ServiceCycleReplayArtifactFormat.MaximumArtifactBytes,
        400_000,
        3_072,
        1_000_000,
        100_000);

    public int MaximumArtifactBytes { get; }
    public int MaximumSemanticEvents { get; }
    public int MaximumCodecEntries { get; }
    public int MaximumRecords { get; }
    public int MaximumCycleFooters { get; }
}

/// <summary>Stable strict-decoder failure identities. Values are persisted in diagnostics and must not move.</summary>
public enum ServiceCycleReplayFormatErrorCode
{
    SourceTooShort = 1,
    MagicMismatch = 2,
    ContainerVersionUnsupported = 3,
    HeaderShapeInvalid = 4,
    HeaderFlagsUnsupported = 5,
    ReservedBytesNonzero = 6,
    LengthOverflow = 7,
    LengthMismatch = 8,
    ArtifactLimitExceeded = 9,
    GlobalChecksumMismatch = 10,
    SectionKindUnsupported = 11,
    SectionVersionUnsupported = 12,
    SectionFlagsUnsupported = 13,
    SectionOrderInvalid = 14,
    SectionBoundsInvalid = 15,
    SectionChecksumMismatch = 16,
    ManifestInvalid = 17,
    FenceMismatch = 18,
    SemanticTraceRejected = 19,
    CodecManifestInvalid = 20,
    CodecManifestOrderInvalid = 21,
    CodecManifestCoverageInvalid = 22,
    RecordIndexInvalid = 23,
    RecordSequenceInvalid = 24,
    RecordCycleInvalid = 25,
    RecordIdentityInvalid = 26,
    RecordSchemaMismatch = 27,
    RecordPayloadPartitionInvalid = 28,
    RecordChecksumMismatch = 29,
    CycleFooterInvalid = 30,
    CycleFooterOrderInvalid = 31,
    DuplicateCycleFooter = 32,
    SerializedJoinMismatch = 33,
}

public sealed class ServiceCycleReplayFormatException : FormatException
{
    internal ServiceCycleReplayFormatException(ServiceCycleReplayFormatErrorCode code, int index = -1)
        : base(index < 0
            ? $"Invalid service-cycle replay artifact ({(int)code})."
            : $"Invalid service-cycle replay artifact ({(int)code}) at index {index}.")
    {
        Code = code;
        Index = index;
    }

    public ServiceCycleReplayFormatErrorCode Code { get; }
    public int Index { get; }
}

/// <summary>Stable reason an otherwise structural artifact cannot be executed exactly.</summary>
public enum ServiceCycleReplayArtifactEligibilityCode
{
    Complete = 1,
    RecordingDisabled = 2,
    RecordingIncomplete = 3,
    SemanticTraceIncomplete = 4,
    FooterIncomplete = 5,
    EvaluationAborted = 6,
    ProjectionAborted = 7,
    RecordCoverageIncomplete = 8,
    SemanticJoinIncomplete = 9,
    PreviousReceiptIncomplete = 10,
    NativeEvidenceIncomplete = 11,
}

/// <summary>Stable exact-join result for a worker footer and authoritative semantic evidence.</summary>
public enum ServiceCycleReplaySemanticJoinCode
{
    Complete = 1,
    SemanticTraceIncomplete = 2,
    FooterNotProvisional = 3,
    RecordBoundsMismatch = 4,
    UnjoinedRecord = 5,
    RequiredRecordMissing = 6,
    RequiredRecordDuplicate = 7,
    ActionRecordGap = 8,
    EvaluationTerminalMissing = 9,
    EvaluationTerminalDuplicate = 10,
    EvaluationFaulted = 11,
    EvaluationActionCountMismatch = 12,
    StatePublicationMissing = 13,
    StatePublicationDuplicate = 14,
    ProjectionFingerprintMismatch = 15,
    BatchPublicationMissing = 16,
    BatchPublicationDuplicate = 17,
    BatchTerminalMissing = 18,
    BatchTerminalDuplicate = 19,
    CycleTerminalMissing = 20,
    CycleTerminalDuplicate = 21,
    PreviousReceiptMissing = 22,
    PreviousReceiptMismatch = 23,
    ActionEvidenceMissing = 24,
    ActionEvidenceDuplicate = 25,
    NativeEvidenceMismatch = 26,
    CaptureStartedMissing = 27,
    CaptureStartedDuplicate = 28,
    CaptureCompletedMissing = 29,
    CaptureCompletedDuplicate = 30,
    CycleQueuedMissing = 31,
    CycleQueuedDuplicate = 32,
    CycleStartedMissing = 33,
    CycleStartedDuplicate = 34,
    EvaluationStartedMissing = 35,
    EvaluationStartedDuplicate = 36,
    CausalParentMismatch = 37,
    AbortedFooterEvidenceInvalid = 38,
    WakeMismatch = 39,
    ConfigurationPublicationMissing = 40,
    ConfigurationPublicationDuplicate = 41,
    StrategyPublicationMissing = 42,
    StrategyPublicationDuplicate = 43,
    PublicationOrderMismatch = 44,
    ActionAttemptMissing = 45,
    ActionAttemptDuplicate = 46,
    ActionAttemptOrderMismatch = 47,
    ActionAttemptCausalityMismatch = 48,
    BatchTerminalCausalityMismatch = 49,
}
