using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

public readonly struct ServiceCycleReplayRecordDecodeResult<TRecord>
    where TRecord : struct, IServiceCycleReplayRecord
{
    internal ServiceCycleReplayRecordDecodeResult(TRecord record)
    {
        Record = record;
        Fault = default;
        Succeeded = true;
    }

    internal ServiceCycleReplayRecordDecodeResult(ServiceCycleReplayFault fault)
    {
        Record = default;
        Fault = fault;
        Succeeded = false;
    }

    public bool Succeeded { get; }
    public TRecord Record { get; }
    public ServiceCycleReplayFault Fault { get; }
}

/// <summary>
/// Strict post-container feature decoder. It validates type, descriptor, schema and byte bounds, contains
/// ordinary feature decode/encode faults as DecodeRejected(record), and requires canonical byte-for-byte
/// re-encoding. Process-fatal exceptions remain outside containment.
/// </summary>
public static class ServiceCycleReplayRecordDecoder
{
    public static ServiceCycleReplayRecordDecodeResult<TRecord> Decode<TRecord>(
        in ServiceCycleReplayEncodedRecord encoded,
        ServiceCycleReplayRecordIdentity expectedIdentity,
        IServiceCycleReplayCodec<TRecord> codec)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        var scratch = new ServiceCycleReplayRecordDecodeScratch();
        return Decode(in encoded, expectedIdentity, codec, scratch);
    }

    internal static ServiceCycleReplayRecordDecodeResult<TRecord> Decode<TRecord>(
        in ServiceCycleReplayEncodedRecord encoded,
        ServiceCycleReplayRecordIdentity expectedIdentity,
        IServiceCycleReplayCodec<TRecord> codec,
        ServiceCycleReplayRecordDecodeScratch scratch)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        if (codec is null) throw new ArgumentNullException(nameof(codec));
        if (!expectedIdentity.IsValid)
            throw new ArgumentException("A valid expected record identity is required.", nameof(expectedIdentity));
        var location = ServiceCycleReplayFailureLocation.AtRecord(expectedIdentity);
        if (encoded.Identity != expectedIdentity)
            return Rejected<TRecord>(location, ServiceCycleReplayExecutionDetailCode.RecordIdentityRejected);

        var shape = ServiceCycleReplayRecordValidator.Validate<TRecord>();
        if (!shape.IsValid)
        {
            return new ServiceCycleReplayRecordDecodeResult<TRecord>(new ServiceCycleReplayFault(
                ServiceCycleReplayFaultCode.RecordTypeRejected,
                location,
                (int)shape.Code));
        }

        ServiceCycleReplayCodecDescriptor descriptor;
        try { descriptor = codec.Descriptor; }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            return Rejected<TRecord>(location, ServiceCycleReplayExecutionDetailCode.RecordSchemaRejected);
        }
        if (ServiceCycleReplayCodecContract.ValidateDescriptor(in descriptor) !=
            ServiceCycleReplayCodecContractCode.Valid || encoded.SchemaVersion != descriptor.SchemaVersion)
        {
            return Rejected<TRecord>(location, ServiceCycleReplayExecutionDetailCode.RecordSchemaRejected);
        }
        if (ServiceCycleReplayCodecContract.ValidateDecodeSource(in descriptor, encoded.Payload.Length) !=
            ServiceCycleReplayCodecContractCode.Valid)
        {
            return Rejected<TRecord>(location, ServiceCycleReplayExecutionDetailCode.RecordSizeRejected);
        }

        try
        {
            var decoded = codec.Decode(encoded.Payload.Span);
            var canonical = scratch.Get(descriptor.MaximumEncodedBytes);
            var length = codec.Encode(in decoded, canonical);
            if (ServiceCycleReplayCodecContract.ValidateEncodeResult(
                    in descriptor,
                    canonical.Length,
                    length) != ServiceCycleReplayCodecContractCode.Valid ||
                length != encoded.Payload.Length ||
                !canonical.AsSpan(0, length).SequenceEqual(encoded.Payload.Span))
            {
                return Rejected<TRecord>(
                    location,
                    ServiceCycleReplayExecutionDetailCode.RecordCanonicalEncodingRejected);
            }
            return new ServiceCycleReplayRecordDecodeResult<TRecord>(decoded);
        }
        catch (Exception exception) when (ServiceCycleReplayContainedRunner.IsContainable(exception))
        {
            return Rejected<TRecord>(location, ServiceCycleReplayExecutionDetailCode.RecordCanonicalEncodingRejected);
        }
    }

    private static ServiceCycleReplayRecordDecodeResult<TRecord> Rejected<TRecord>(
        ServiceCycleReplayFailureLocation location,
        ServiceCycleReplayExecutionDetailCode detail)
        where TRecord : struct, IServiceCycleReplayRecord =>
        new(new ServiceCycleReplayFault(
            ServiceCycleReplayFaultCode.DecodeRejected,
            location,
            (int)detail));
}

internal sealed class ServiceCycleReplayRecordDecodeScratch
{
    private byte[] _buffer = Array.Empty<byte>();
    internal int AllocationCount { get; private set; }

    internal byte[] Get(int minimumLength)
    {
        if (_buffer.Length < minimumLength)
        {
            _buffer = new byte[minimumLength];
            AllocationCount++;
        }
        return _buffer;
    }
}
