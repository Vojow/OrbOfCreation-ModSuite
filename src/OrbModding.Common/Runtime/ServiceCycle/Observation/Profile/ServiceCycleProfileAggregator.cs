#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal enum ServiceCycleProfileAggregationFault
{
    None = 0,
    GroupCapacityExhausted = 1,
    ArithmeticExhausted = 2,
    AllocationUnavailable = 3,
}

internal enum ServiceCycleProfileAggregationResult
{
    Accepted = 0,
    Faulted = 1,
    Sealed = 2,
}

internal sealed class ServiceCycleProfileAggregator
{
    private const int MaximumGroups = 1_024;
    private const int MaximumSamplesPerGroup = 64;

    private readonly ServiceCycleProfileAggregateBucket[] _buckets;
    private readonly int[] _groupSlots;
    private readonly ServiceCycleProfileMeasurement[] _samples;
    private readonly int _samplesPerGroup;
    private readonly int _mask;
    private readonly int _ownerThreadId;
    private readonly bool _allocationAvailable;
    private int _groupCount;
    private bool _sealed;
    private ServiceCycleProfileAggregationFault _fault;

    internal ServiceCycleProfileAggregator(
        int maximumGroups,
        int samplesPerGroup,
        bool allocationAvailable)
    {
        if (maximumGroups is <= 0 or > MaximumGroups)
            throw new ArgumentOutOfRangeException(nameof(maximumGroups));
        if (samplesPerGroup is <= 0 or > MaximumSamplesPerGroup)
            throw new ArgumentOutOfRangeException(nameof(samplesPerGroup));
        var tableCapacity = TableCapacity(maximumGroups);
        _buckets = new ServiceCycleProfileAggregateBucket[tableCapacity];
        _groupSlots = new int[maximumGroups];
        _samples = new ServiceCycleProfileMeasurement[checked(maximumGroups * samplesPerGroup)];
        _samplesPerGroup = samplesPerGroup;
        _mask = tableCapacity - 1;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _allocationAvailable = allocationAvailable;
    }

    internal int GroupCount => _groupCount;
    internal int MaximumOutputRecords => checked(_groupSlots.Length * (1 + _samplesPerGroup));
    internal ServiceCycleProfileAggregationFault Fault => _fault;

    internal ServiceCycleProfileAggregationResult Record(in ServiceCycleProfileMeasurement measurement)
    {
        EnsureOwner();
        if (_sealed) return ServiceCycleProfileAggregationResult.Sealed;
        if (_fault != ServiceCycleProfileAggregationFault.None)
            return ServiceCycleProfileAggregationResult.Faulted;
        if (!_allocationAvailable && measurement.AllocatedBytes != 0)
            return Fail(ServiceCycleProfileAggregationFault.AllocationUnavailable);

        var key = new ServiceCycleProfileAggregateKey(in measurement);
        var stableHash = key.StableHash();
        var slot = FindSlot(in key, stableHash, out var occupied);
        if (!occupied && _groupCount == _groupSlots.Length)
            return Fail(ServiceCycleProfileAggregationFault.GroupCapacityExhausted);

        ref var bucket = ref _buckets[slot];
        int groupOrdinal;
        if (!occupied)
        {
            groupOrdinal = _groupCount++;
            _groupSlots[groupOrdinal] = slot;
            bucket.Initialize(groupOrdinal, in key, in measurement);
        }
        else
        {
            groupOrdinal = bucket.GroupOrdinal;
            if (!bucket.TryAdd(in measurement))
                return Fail(ServiceCycleProfileAggregationFault.ArithmeticExhausted);
        }
        RetainSample(groupOrdinal, stableHash, ref bucket, in measurement);
        return ServiceCycleProfileAggregationResult.Accepted;
    }

    internal void Seal()
    {
        EnsureOwner();
        if (_sealed) throw new InvalidOperationException("The profile aggregator is already sealed.");
        _sealed = true;
    }

    internal ServiceCycleProfileRecord GetAggregate(int groupOrdinal) => ReadBucket(groupOrdinal).ToRecord();

    internal int GetSampleCount(int groupOrdinal) => ReadBucket(groupOrdinal).SampleCount;

    internal ServiceCycleProfileRecord GetSample(int groupOrdinal, int sampleOrdinal)
    {
        ref readonly var bucket = ref ReadBucket(groupOrdinal);
        if ((uint)sampleOrdinal >= (uint)bucket.SampleCount)
            throw new ArgumentOutOfRangeException(nameof(sampleOrdinal));
        var measurement = _samples[checked(groupOrdinal * _samplesPerGroup + sampleOrdinal)];
        var context = measurement.Context;
        var operations = measurement.Operations;
        return ServiceCycleProfileRecord.Sample(
            context.StageCode,
            context.ServiceOrdinal,
            context.Lifecycle,
            context.Cycle,
            context.Frame,
            measurement.StartedAtRawTicks,
            measurement.ElapsedRawTicks,
            measurement.AllocatedBytes,
            context.Temperature,
            in operations);
    }

    private ref readonly ServiceCycleProfileAggregateBucket ReadBucket(int groupOrdinal)
    {
        EnsureOwner();
        if (!_sealed) throw new InvalidOperationException("Profile aggregates are readable only after sealing.");
        if (_fault != ServiceCycleProfileAggregationFault.None)
            throw new InvalidOperationException("Faulted profile aggregates cannot be published.");
        if ((uint)groupOrdinal >= (uint)_groupCount) throw new ArgumentOutOfRangeException(nameof(groupOrdinal));
        return ref _buckets[_groupSlots[groupOrdinal]];
    }

    private int FindSlot(in ServiceCycleProfileAggregateKey key, ulong stableHash, out bool occupied)
    {
        var start = unchecked((int)stableHash) & _mask;
        for (var offset = 0; offset < _buckets.Length; offset++)
        {
            var index = (start + offset) & _mask;
            ref readonly var bucket = ref _buckets[index];
            if (!bucket.Occupied)
            {
                occupied = false;
                return index;
            }
            if (bucket.Key.Equals(key))
            {
                occupied = true;
                return index;
            }
        }
        throw new InvalidOperationException("The profile aggregate table exceeded its configured load factor.");
    }

    private void RetainSample(
        int groupOrdinal,
        ulong stableHash,
        ref ServiceCycleProfileAggregateBucket bucket,
        in ServiceCycleProfileMeasurement measurement)
    {
        int sampleOrdinal;
        if (bucket.SampleCount < _samplesPerGroup)
        {
            sampleOrdinal = bucket.SampleCount++;
        }
        else
        {
            var draw = SampleDraw(stableHash, bucket.OccurrenceCount) % bucket.OccurrenceCount;
            if (draw >= checked((ulong)_samplesPerGroup)) return;
            sampleOrdinal = checked((int)draw);
        }
        _samples[checked(groupOrdinal * _samplesPerGroup + sampleOrdinal)] = measurement;
    }

    private ServiceCycleProfileAggregationResult Fail(ServiceCycleProfileAggregationFault fault)
    {
        _fault = fault;
        return ServiceCycleProfileAggregationResult.Faulted;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Profile aggregation is owner-thread affine.");
    }

    private static int TableCapacity(int maximumGroups)
    {
        var required = checked(maximumGroups * 2);
        var capacity = 2;
        while (capacity < required) capacity <<= 1;
        return capacity;
    }

    private static ulong SampleDraw(ulong groupHash, ulong occurrence)
    {
        var value = groupHash ^ unchecked(occurrence * 0x9e3779b97f4a7c15ul);
        value ^= value >> 30;
        value = unchecked(value * 0xbf58476d1ce4e5b9ul);
        value ^= value >> 27;
        value = unchecked(value * 0x94d049bb133111ebul);
        return value ^ value >> 31;
    }
}
#endif
