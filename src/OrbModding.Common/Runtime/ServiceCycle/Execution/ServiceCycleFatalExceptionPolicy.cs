using System;
using System.Runtime.CompilerServices;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// Opt-in marker for callback surfaces whose fatal exceptions must escape ordinary service fault containment.
/// The live gameplay runtime does not implement this marker; replay adapters do so that deterministic tooling
/// can preserve its stricter process-fatal boundary without changing ordinary OOM fault recovery.
/// </summary>
internal interface IServiceCycleFatalExceptionPolicy
{
}

internal static class ServiceCycleFatalExceptionPolicy
{
    private static readonly ConditionalWeakTable<object, Registration> Registrations = new();

    internal static void Register(object callbackOwner)
    {
        if (callbackOwner is null) throw new ArgumentNullException(nameof(callbackOwner));
        Registrations.GetOrCreateValue(callbackOwner);
    }

    internal static bool AppliesTo(object callbackOwner) =>
        callbackOwner is IServiceCycleFatalExceptionPolicy ||
        Registrations.TryGetValue(callbackOwner, out _);

    internal static bool MustEscape(object callbackOwner, Exception exception) =>
        AppliesTo(callbackOwner) &&
        exception is StackOverflowException or OutOfMemoryException or AccessViolationException;

    private sealed class Registration
    {
        public Registration() { }
    }
}
