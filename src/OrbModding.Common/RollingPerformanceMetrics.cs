using System;

namespace OrbModding.Common;

/// <summary>
/// Allocation-conscious rolling timing metrics. Recording is allocation-free;
/// percentile calculation reuses a scratch buffer allocated with the window.
/// Snapshot creation sorts that scratch buffer, so it is intended for low-frequency
/// diagnostics rather than per-frame use. Instances are intended to be used from
/// the Unity main thread.
/// </summary>
public sealed class RollingPerformanceMetrics
{
    private readonly double[] _milliseconds;
    private readonly int[] _operations;
    private readonly double[] _percentileScratch;
    private int _count;
    private int _nextIndex;
    private double _windowMilliseconds;
    private long _windowOperations;
    private long _totalSamples;
    private long _totalOperations;

    public RollingPerformanceMetrics(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "The rolling window capacity must be positive.");
        }

        _milliseconds = new double[capacity];
        _operations = new int[capacity];
        _percentileScratch = new double[capacity];
    }

    public int Capacity => _milliseconds.Length;

    public int Count => _count;

    public long TotalSamples => _totalSamples;

    public long TotalOperations => _totalOperations;

    public void Record(double elapsedMilliseconds, int operations = 1)
    {
        if (double.IsNaN(elapsedMilliseconds) || double.IsInfinity(elapsedMilliseconds) || elapsedMilliseconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedMilliseconds),
                "Elapsed time must be a finite, non-negative value.");
        }

        if (operations < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operations), "The operation count cannot be negative.");
        }

        if (_count == Capacity)
        {
            _windowMilliseconds -= _milliseconds[_nextIndex];
            _windowOperations -= _operations[_nextIndex];
        }
        else
        {
            _count++;
        }

        _milliseconds[_nextIndex] = elapsedMilliseconds;
        _operations[_nextIndex] = operations;
        _windowMilliseconds += elapsedMilliseconds;
        _windowOperations += operations;
        _nextIndex = (_nextIndex + 1) % Capacity;
        _totalSamples++;
        _totalOperations += operations;
    }

    public RollingPerformanceSnapshot GetSnapshot(double percentile = 0.95)
    {
        if (double.IsNaN(percentile) || percentile < 0.0 || percentile > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be between zero and one.");
        }

        if (_count == 0)
        {
            return new RollingPerformanceSnapshot(
                Capacity,
                0,
                _totalSamples,
                0,
                _totalOperations,
                0.0,
                0.0,
                percentile,
                0.0);
        }

        var maximum = 0.0;
        for (var i = 0; i < _count; i++)
        {
            var sample = _milliseconds[i];
            _percentileScratch[i] = sample;
            if (sample > maximum)
            {
                maximum = sample;
            }
        }

        Array.Sort(_percentileScratch, 0, _count);
        var percentileIndex = percentile <= 0.0
            ? 0
            : (int)Math.Ceiling(percentile * _count) - 1;

        return new RollingPerformanceSnapshot(
            Capacity,
            _count,
            _totalSamples,
            _windowOperations,
            _totalOperations,
            _windowMilliseconds / _count,
            maximum,
            percentile,
            _percentileScratch[percentileIndex]);
    }
}

public readonly struct RollingPerformanceSnapshot
{
    public RollingPerformanceSnapshot(
        int capacity,
        int sampleCount,
        long totalSamples,
        long operations,
        long totalOperations,
        double averageMilliseconds,
        double maximumMilliseconds,
        double percentile,
        double percentileMilliseconds)
    {
        Capacity = capacity;
        SampleCount = sampleCount;
        TotalSamples = totalSamples;
        Operations = operations;
        TotalOperations = totalOperations;
        AverageMilliseconds = averageMilliseconds;
        MaximumMilliseconds = maximumMilliseconds;
        Percentile = percentile;
        PercentileMilliseconds = percentileMilliseconds;
    }

    public int Capacity { get; }

    public int SampleCount { get; }

    public long TotalSamples { get; }

    public long Operations { get; }

    public long TotalOperations { get; }

    public double AverageMilliseconds { get; }

    public double MaximumMilliseconds { get; }

    public double Percentile { get; }

    public double PercentileMilliseconds { get; }
}
