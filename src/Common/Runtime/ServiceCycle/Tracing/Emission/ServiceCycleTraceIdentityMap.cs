using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>
/// Stable numeric trace identities for the fixed, explicitly registered service array.
/// Registration ordinal zero maps to trace identity one; strings never enter trace payloads.
/// </summary>
internal sealed class ServiceCycleTraceIdentityMap
{
    private readonly ServiceId[] _services;
    private int _registeredCount;

    internal ServiceCycleTraceIdentityMap(int serviceCapacity)
    {
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        _services = new ServiceId[serviceCapacity];
    }

    internal int ServiceCapacity => _services.Length;
    internal int RegisteredCount => _registeredCount;

    internal void Register(int ordinal, ServiceId service)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (ordinal != _registeredCount || (uint)ordinal >= (uint)_services.Length)
            throw new ArgumentOutOfRangeException(nameof(ordinal),
                "Trace services must be registered once in stable ordinal order.");
        for (var index = 0; index < _registeredCount; index++)
        {
            if (_services[index] == service)
                throw new ArgumentException("Trace service identities must be unique.", nameof(service));
        }
        _services[ordinal] = service;
        _registeredCount++;
    }

    internal ServiceCycleTraceServiceId ForRegistrationOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)_registeredCount)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return new ServiceCycleTraceServiceId(checked((ulong)ordinal + 1));
    }

    internal void EnsureMatches(int ordinal, ServiceId service)
    {
        ForRegistrationOrdinal(ordinal);
        if (!service.IsValid || _services[ordinal] != service)
            throw new ArgumentException("The service identity does not match the registered ordinal.", nameof(service));
    }
}
