using System;
using System.Reflection;

namespace OrbAutomata;

/// <summary>
/// Defines the bounded reflection failures that make Auto Items reject an operation. Process-fatal
/// failures remain outside containment.
/// </summary>
internal static class AutoItemsReflectionAccess
{
    internal static bool IsExpectedFailure(Exception exception) =>
        exception is TargetInvocationException or
            AmbiguousMatchException or
            ArgumentException or
            InvalidOperationException or
            TargetException or
            MemberAccessException or
            FormatException or
            OverflowException or
            TypeInitializationException;
}
