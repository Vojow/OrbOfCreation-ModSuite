using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>Canonical non-generic `.oscr` v1 encoder and strict decoder.</summary>
public static class ServiceCycleReplayArtifactCodec
{
    public static int GetMaximumEncodedLength(
        int semanticEventCapacity,
        ServiceCycleReplaySession session)
    {
        if (semanticEventCapacity < 0) throw new ArgumentOutOfRangeException(nameof(semanticEventCapacity));
        if (session is null) throw new ArgumentNullException(nameof(session));
        var length = checked(
            ServiceCycleReplayArtifactFormat.HeaderBytes +
            ServiceCycleReplayArtifactFormat.RequiredSectionCount *
                ServiceCycleReplayArtifactFormat.DirectoryEntryBytes +
            ServiceCycleReplayArtifactFormat.ManifestBytes +
            ServiceCycleTraceCodec.GetEncodedLength(semanticEventCapacity) +
            checked(session.ServiceCapacity * 3 * ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes) +
            checked(session.RecordCapacity * ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes) +
            session.ByteCapacity +
            checked(session.CycleFooterCapacity * ServiceCycleReplayArtifactFormat.CycleFooterBytes));
        if (length > ServiceCycleReplayArtifactFormat.MaximumArtifactBytes)
            throw new ArgumentOutOfRangeException(nameof(session), "Configured replay export exceeds the hard artifact cap.");
        return length;
    }

    public static int Encode(
        ReadOnlySpan<byte> exactSemanticTrace,
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot,
        Span<byte> destination)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (exactSemanticTrace.Length > ServiceCycleReplayArtifactFormat.MaximumArtifactBytes)
            throw new ArgumentOutOfRangeException(nameof(exactSemanticTrace));
        var encodedLength = GetValidatedEncodedLength(exactSemanticTrace.Length, in snapshot);
        EnsureDestination(destination, encodedLength);
        var semantic = exactSemanticTrace.ToArray();
        var artifact = ServiceCycleReplayArtifactBuilder.Prepare(semantic, session, in snapshot);
        return ServiceCycleReplayArtifactEncoder.Encode(artifact, destination);
    }

    public static byte[] Encode(
        ReadOnlySpan<byte> exactSemanticTrace,
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (exactSemanticTrace.Length > ServiceCycleReplayArtifactFormat.MaximumArtifactBytes)
            throw new ArgumentOutOfRangeException(nameof(exactSemanticTrace));
        GetValidatedEncodedLength(exactSemanticTrace.Length, in snapshot);
        var semantic = exactSemanticTrace.ToArray();
        var artifact = ServiceCycleReplayArtifactBuilder.Prepare(semantic, session, in snapshot);
        var output = new byte[ServiceCycleReplayArtifactEncoder.GetEncodedLength(artifact)];
        ServiceCycleReplayArtifactEncoder.Encode(artifact, output);
        return output;
    }

    public static int Encode(
        ServiceCycleTraceDropRange dropped,
        ReadOnlySpan<ServiceCycleSemanticEvent> events,
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot,
        Span<byte> destination)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        var semanticLength = ServiceCycleTraceCodec.GetEncodedLength(events.Length);
        var encodedLength = GetValidatedEncodedLength(semanticLength, in snapshot);
        EnsureDestination(destination, encodedLength);
        var semantic = EncodeSemantic(events, dropped, in snapshot);
        var artifact = ServiceCycleReplayArtifactBuilder.Prepare(semantic, session, in snapshot);
        return ServiceCycleReplayArtifactEncoder.Encode(artifact, destination);
    }

    internal static byte[] Encode(
        ServiceCycleTraceDropRange dropped,
        ReadOnlySpan<ServiceCycleSemanticEvent> events,
        ServiceCycleReplaySession session,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        var semanticLength = ServiceCycleTraceCodec.GetEncodedLength(events.Length);
        GetValidatedEncodedLength(semanticLength, in snapshot);
        var semantic = EncodeSemantic(events, dropped, in snapshot);
        var artifact = ServiceCycleReplayArtifactBuilder.Prepare(semantic, session, in snapshot);
        var output = new byte[ServiceCycleReplayArtifactEncoder.GetEncodedLength(artifact)];
        ServiceCycleReplayArtifactEncoder.Encode(artifact, output);
        return output;
    }

    public static ServiceCycleReplayArtifactDocument Decode(ReadOnlySpan<byte> source) =>
        Decode(source, ServiceCycleReplayArtifactLimits.Default);

    public static ServiceCycleReplayArtifactDocument Decode(
        ReadOnlySpan<byte> source,
        ServiceCycleReplayArtifactLimits limits) =>
        ServiceCycleReplayArtifactDecoder.Decode(source, in limits);

    internal static ServiceCycleReplayArtifactDocument Decode(
        ReadOnlySpan<byte> source,
        ServiceCycleReplayArtifactLimits limits,
        ServiceCycleReplayFormatWorkCounter work) =>
        ServiceCycleReplayArtifactDecoder.Decode(source, in limits, work);

    public static int Reencode(ServiceCycleReplayArtifactDocument document, Span<byte> destination)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        return ServiceCycleReplayArtifactEncoder.Encode(document.Prepared, destination);
    }

    internal static int Reencode(
        ServiceCycleReplayArtifactDocument document,
        Span<byte> destination,
        ServiceCycleReplayFormatWorkCounter work)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        return ServiceCycleReplayArtifactEncoder.Encode(document.Prepared, destination, work);
    }

    private static byte[] EncodeSemantic(
        ReadOnlySpan<ServiceCycleSemanticEvent> events,
        ServiceCycleTraceDropRange dropped,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        if (!snapshot.TraceSession.IsValid)
            throw new ArgumentException("The replay snapshot has no semantic session.", nameof(snapshot));
        var semantic = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(snapshot.TraceSession, dropped, events, semantic);
        return semantic;
    }

    /// <summary>
    /// Rejects impossible exports before cloning or allocating a semantic buffer. This is a wire
    /// upper-bound check only; the builder remains authoritative for snapshot and semantic validity.
    /// </summary>
    private static int GetValidatedEncodedLength(
        int semanticLength,
        in ServiceCycleReplayRecordingSnapshot snapshot)
    {
        if (semanticLength < 0) throw new ArgumentOutOfRangeException(nameof(semanticLength));
        try
        {
            var codecCount = checked(snapshot.CodecManifests.Count * 3);
            var length = checked(
                ServiceCycleReplayArtifactFormat.HeaderBytes +
                ServiceCycleReplayArtifactFormat.RequiredSectionCount *
                    ServiceCycleReplayArtifactFormat.DirectoryEntryBytes +
                ServiceCycleReplayArtifactFormat.ManifestBytes +
                semanticLength +
                checked(codecCount * ServiceCycleReplayArtifactFormat.CodecManifestEntryBytes) +
                checked(snapshot.HighWater.RecordCount *
                    ServiceCycleReplayArtifactFormat.RecordIndexEntryBytes) +
                snapshot.HighWater.ByteCount +
                checked(snapshot.HighWater.FooterCount *
                    ServiceCycleReplayArtifactFormat.CycleFooterBytes));
            if (length > ServiceCycleReplayArtifactFormat.MaximumArtifactBytes)
                throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.ArtifactLimitExceeded);
            return length;
        }
        catch (OverflowException)
        {
            throw ServiceCycleReplayBinary.Error(ServiceCycleReplayFormatErrorCode.ArtifactLimitExceeded);
        }
    }

    private static void EnsureDestination(Span<byte> destination, int encodedLength)
    {
        if (destination.Length < encodedLength)
            throw new ArgumentException("The destination is too small.", nameof(destination));
    }
}
