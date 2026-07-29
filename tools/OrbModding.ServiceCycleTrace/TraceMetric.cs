namespace OrbModding.ServiceCycleTrace;

internal readonly struct TraceMetric
{
    internal TraceMetric(long samples, double totalMilliseconds, double maximumMilliseconds)
    {
        Samples = samples;
        TotalMilliseconds = totalMilliseconds;
        MaximumMilliseconds = maximumMilliseconds;
    }

    internal long Samples { get; }
    internal double TotalMilliseconds { get; }
    internal double AverageMilliseconds => Samples == 0 ? 0 : TotalMilliseconds / Samples;
    internal double MaximumMilliseconds { get; }
    internal static double ToMilliseconds(long timeSpanTicks) => timeSpanTicks / 10_000d;
}

internal sealed class TraceMetricBuilder
{
    private long _samples;
    private double _total;
    private double _maximum;

    internal void AddTicks(long ticks)
        => AddMilliseconds(TraceMetric.ToMilliseconds(ticks));

    internal void AddMilliseconds(double milliseconds)
    {
        _samples++;
        _total += milliseconds;
        _maximum = Math.Max(_maximum, milliseconds);
    }

    internal TraceMetric Freeze() => new(_samples, _total, _maximum);
}
