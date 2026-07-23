using System;
using System.Diagnostics;

namespace OrbModding.Common.Runtime;

/// <summary>
/// A process-local monotonic clock whose zero is the raw Stopwatch timestamp
/// captured when the clock is constructed. It has no wall-clock meaning.
/// </summary>
public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    private readonly Func<long>? _readRawTimestamp;
    private readonly long _rawOrigin;
    private readonly long _frequency;

    public StopwatchMonotonicClock()
    {
        _frequency = Stopwatch.Frequency;
        _rawOrigin = Stopwatch.GetTimestamp();
    }

    internal StopwatchMonotonicClock(Func<long> readRawTimestamp, long frequency)
    {
        _readRawTimestamp = readRawTimestamp ?? throw new ArgumentNullException(nameof(readRawTimestamp));
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _frequency = frequency;
        _rawOrigin = readRawTimestamp();
    }

    public MonotonicTimestamp Now => ConvertTimestamp(ReadRawTimestamp(), _rawOrigin, _frequency);

    /// <summary>
    /// Converts non-negative elapsed Stopwatch ticks to the fixed 100-nanosecond
    /// unit used by <see cref="MonotonicDuration"/>. Fractional destination ticks
    /// are truncated, matching Stopwatch elapsed-time semantics.
    /// </summary>
    public static MonotonicDuration ConvertElapsedTicks(long elapsedStopwatchTicks, long frequency)
    {
        if (elapsedStopwatchTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedStopwatchTicks));
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));

        var wholeSeconds = elapsedStopwatchTicks / frequency;
        var remainder = elapsedStopwatchTicks % frequency;
        var wholeTicks = checked(wholeSeconds * MonotonicDuration.TicksPerSecond);

        long fractionalTicks;
        if (remainder <= long.MaxValue / MonotonicDuration.TicksPerSecond)
        {
            fractionalTicks = remainder * MonotonicDuration.TicksPerSecond / frequency;
        }
        else
        {
            // Decimal supplies enough integer precision for the product of any
            // Int64 remainder and the fixed 10,000,000 destination frequency.
            fractionalTicks = checked((long)decimal.Floor(
                (decimal)remainder * MonotonicDuration.TicksPerSecond / frequency));
        }

        return new MonotonicDuration(checked(wholeTicks + fractionalTicks));
    }

    internal static MonotonicTimestamp ConvertTimestamp(long rawTimestamp, long rawOrigin, long frequency)
    {
        if (rawTimestamp < rawOrigin)
        {
            throw new InvalidOperationException("A monotonic timestamp source cannot move backwards.");
        }

        var elapsed = checked(rawTimestamp - rawOrigin);
        return new MonotonicTimestamp(ConvertElapsedTicks(elapsed, frequency).Ticks);
    }

    private long ReadRawTimestamp() => _readRawTimestamp is null
        ? Stopwatch.GetTimestamp()
        : _readRawTimestamp();
}
