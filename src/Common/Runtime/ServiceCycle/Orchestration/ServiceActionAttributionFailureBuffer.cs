using System;
using System.Collections.Generic;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal readonly struct ServiceActionAttributionFailure
{
    internal ServiceActionAttributionFailure(
        int serviceOrdinal,
        ulong cycle,
        ulong action,
        string reason)
    {
        ServiceOrdinal = serviceOrdinal;
        Cycle = cycle;
        Action = action;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    internal int ServiceOrdinal { get; }
    internal ulong Cycle { get; }
    internal ulong Action { get; }
    internal string Reason { get; }
}

internal sealed class ServiceActionAttributionFailureBuffer
{
    private readonly List<ServiceActionAttributionFailure> _failures = new();

    internal int Count => _failures.Count;

    internal void BeginFrame() => _failures.Clear();

    internal void Observe(
        int serviceOrdinal,
        ulong cycle,
        ulong action,
        string reason) =>
        _failures.Add(new ServiceActionAttributionFailure(
            serviceOrdinal,
            cycle,
            action,
            reason));

    internal ServiceActionAttributionFailure At(int index) => _failures[index];

    internal void Clear() => _failures.Clear();
}
