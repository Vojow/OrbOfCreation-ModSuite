using System;
using System.Reflection;

namespace OrbAutomata;

/// <summary>
/// Defines bounded reflection failures that native Automata adapters contain as rejected or
/// faulted operations. Process-fatal failures deliberately remain outside containment.
/// </summary>
internal static class AutomataReflectionAccess
{
    internal static bool IsExpectedFailure(Exception exception) =>
        exception is TargetInvocationException or
            AmbiguousMatchException or
            ArgumentException or
            InvalidOperationException or
            TargetException or
            MemberAccessException or
            MissingMemberException or
            FormatException or
            OverflowException or
            TypeInitializationException;
}
