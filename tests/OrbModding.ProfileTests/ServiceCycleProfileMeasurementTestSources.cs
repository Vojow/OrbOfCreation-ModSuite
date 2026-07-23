using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbModding.ProfileTests;

internal sealed class ScriptedProfileRawClock : IServiceCycleProfileRawClock
{
    private readonly long[] _timestamps;
    private readonly List<char>? _calls;
    private readonly Exception? _terminalFailure;
    private int _index;

    internal ScriptedProfileRawClock(
        long frequency,
        long[] timestamps,
        List<char>? calls = null,
        Exception? terminalFailure = null)
    {
        Frequency = frequency;
        _timestamps = timestamps;
        _calls = calls;
        _terminalFailure = terminalFailure;
    }

    public long Frequency { get; }
    internal int ReadCount => _index;

    public long ReadTimestamp()
    {
        _calls?.Add('R');
        if (_index < _timestamps.Length) return _timestamps[_index++];
        _index++;
        throw _terminalFailure ?? new InvalidOperationException("The raw-clock script is exhausted.");
    }
}

internal sealed class ScriptedProfileAllocationCounter : IServiceCycleProfileAllocationCounter
{
    private readonly long[] _values;
    private readonly List<char>? _calls;
    private readonly Exception? _terminalFailure;
    private int _index;

    internal ScriptedProfileAllocationCounter(
        long[] values,
        List<char>? calls = null,
        Exception? terminalFailure = null)
    {
        _values = values;
        _calls = calls;
        _terminalFailure = terminalFailure;
    }

    internal int ReadCount => _index;

    public long ReadAllocatedBytes()
    {
        _calls?.Add('A');
        if (_index < _values.Length) return _values[_index++];
        _index++;
        throw _terminalFailure ?? new InvalidOperationException("The allocation script is exhausted.");
    }
}

internal sealed class FixedProfileMonotonicClock : IMonotonicClock
{
    internal FixedProfileMonotonicClock(long ticks) => Now = new MonotonicTimestamp(ticks);
    public MonotonicTimestamp Now { get; }
}

internal sealed class IncrementingProfileRawClock : IServiceCycleProfileRawClock
{
    private long _timestamp;
    public long Frequency => 10_000_000;
    public long ReadTimestamp() => _timestamp += 2;
}

internal sealed class ProvenIncrementingProfileAllocationCounter : IServiceCycleProfileAllocationCounter
{
    private long _allocated;
    private int _reads;

    public long ReadAllocatedBytes()
    {
        _reads++;
        if (_reads == 1) return 0;
        if (_reads == 2) return _allocated = 100;
        if (_reads == 3) return _allocated = 400;
        return _allocated += 8;
    }
}
