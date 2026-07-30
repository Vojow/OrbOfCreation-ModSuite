using System;
using System.Reflection;

namespace OrbAutomata;

internal static class AutoItemsReflectionAccess
{
    internal static bool IsExpectedFailure(Exception ex) => ex is
        TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        InvalidCastException or
        FormatException or
        OverflowException or
        NullReferenceException or
        TargetException or
        TargetParameterCountException or
        MemberAccessException or
        AmbiguousMatchException or
        TypeLoadException or
        MissingMemberException;
}
