using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>
/// Fixed-capacity association between emergency episodes and their exact entry facts.
/// The oldest association may be discarded only after more emergency entries than the
/// semantic event capacity, at which point the corresponding trace fact is already lost.
/// </summary>
internal sealed class ServiceCycleEmergencyCausalMap
{
    private readonly EmergencyStopContext[] _contexts;
    private readonly ServiceCycleTraceEventId[] _entries;
    private int _next;
    private int _count;

    internal ServiceCycleEmergencyCausalMap(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _contexts = new EmergencyStopContext[capacity];
        _entries = new ServiceCycleTraceEventId[capacity];
    }

    internal void Record(in EmergencyStopContext context, ServiceCycleTraceEventId entry)
    {
        if (!context.IsValid) throw new ArgumentException("A valid emergency context is required.", nameof(context));
        if (!entry.IsValid) throw new ArgumentException("A valid trace event identity is required.", nameof(entry));

        _contexts[_next] = context;
        _entries[_next] = entry;
        _next = (_next + 1) % _contexts.Length;
        if (_count < _contexts.Length) _count++;
    }

    internal bool TryResolve(in EmergencyStopContext context, out ServiceCycleTraceEventId entry)
    {
        for (var offset = 0; offset < _count; offset++)
        {
            var index = _next - 1 - offset;
            if (index < 0) index += _contexts.Length;
            if (_contexts[index] != context) continue;
            entry = _entries[index];
            return true;
        }

        entry = default;
        return false;
    }
}
