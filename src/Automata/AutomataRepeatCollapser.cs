using System;
using System.Globalization;

namespace OrbAutomata;

internal enum AutomataLogSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Preserves each severity's state transitions while replacing byte-identical repeat runs with a
/// counted, timestamped account of the occurrences that were held.
/// </summary>
internal sealed class AutomataRepeatCollapser
{
    private readonly object _gate = new();
    private readonly TimeSpan _heartbeat;
    private readonly Action<AutomataLogSeverity, string, DateTimeOffset> _emit;
    private readonly RepeatState _info = new();
    private readonly RepeatState _warning = new();
    private readonly RepeatState _error = new();

    internal AutomataRepeatCollapser(
        TimeSpan heartbeat,
        Action<AutomataLogSeverity, string, DateTimeOffset> emit)
    {
        if (heartbeat <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(heartbeat), heartbeat, "Heartbeat must be positive.");

        _heartbeat = heartbeat;
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
    }

    internal void Write(AutomataLogSeverity severity, string message, DateTimeOffset occurredAt)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        lock (_gate)
        {
            var state = StateFor(severity);
            if (state.Message is null)
            {
                Start(state, message, occurredAt);
                _emit(severity, message, occurredAt);
                return;
            }

            if (!string.Equals(state.Message, message, StringComparison.Ordinal))
            {
                Flush(severity, state);
                Start(state, message, occurredAt);
                _emit(severity, message, occurredAt);
                return;
            }

            state.AdditionalOccurrences++;
            state.LastOccurrenceAt = occurredAt;
            if (Elapsed(state.SpanStartedAt, occurredAt) >= _heartbeat)
                Flush(severity, state);
        }
    }

    internal void FlushAll()
    {
        lock (_gate)
        {
            Flush(AutomataLogSeverity.Info, _info);
            Flush(AutomataLogSeverity.Warning, _warning);
            Flush(AutomataLogSeverity.Error, _error);
        }
    }

    private RepeatState StateFor(AutomataLogSeverity severity) => severity switch
    {
        AutomataLogSeverity.Info => _info,
        AutomataLogSeverity.Warning => _warning,
        AutomataLogSeverity.Error => _error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
    };

    private void Flush(AutomataLogSeverity severity, RepeatState state)
    {
        if (state.AdditionalOccurrences == 0 || state.Message is null) return;

        var span = Elapsed(state.SpanStartedAt, state.LastOccurrenceAt);
        _emit(
            severity,
            Summary(severity, state.Message, state.AdditionalOccurrences, span),
            state.LastOccurrenceAt);
        state.SpanStartedAt = state.LastOccurrenceAt;
        state.AdditionalOccurrences = 0;
    }

    private static void Start(RepeatState state, string message, DateTimeOffset occurredAt)
    {
        state.Message = message;
        state.SpanStartedAt = occurredAt;
        state.LastOccurrenceAt = occurredAt;
        state.AdditionalOccurrences = 0;
    }

    private static TimeSpan Elapsed(DateTimeOffset startedAt, DateTimeOffset endedAt) =>
        endedAt >= startedAt ? endedAt - startedAt : TimeSpan.Zero;

    private static string Summary(
        AutomataLogSeverity severity,
        string message,
        int additionalOccurrences,
        TimeSpan span)
    {
        var severityName = severity.ToString().ToLowerInvariant();
        var repeatNoun = additionalOccurrences == 1 ? "time" : "times";
        var spanSeconds = span.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"Previous {severityName} line repeated {additionalOccurrences} more {repeatNoun} " +
               $"over {spanSeconds}s: {message}";
    }

    private sealed class RepeatState
    {
        internal string? Message { get; set; }

        internal DateTimeOffset SpanStartedAt { get; set; }

        internal DateTimeOffset LastOccurrenceAt { get; set; }

        internal int AdditionalOccurrences { get; set; }
    }
}
