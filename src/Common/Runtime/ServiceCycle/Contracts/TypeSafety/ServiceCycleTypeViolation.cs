using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal readonly struct ServiceCycleTypeViolation
{
    internal ServiceCycleTypeViolation(string path, Type type, string reason)
    {
        Path = path;
        Type = type;
        Reason = reason;
    }

    internal string Path { get; }
    internal Type Type { get; }
    internal string Reason { get; }
    internal string Message =>
        $"Service-cycle structural safety rejected {Path} ({Type.FullName ?? Type.Name}): {Reason}.";
}
