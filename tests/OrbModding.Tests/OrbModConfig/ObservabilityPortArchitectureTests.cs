using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ObservabilityPortArchitectureTests
{
    private const string FullTraceNamespace =
        "OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace";
    private const string ControlNamespace = FullTraceNamespace + ".Control";
    private const string DecisionJournalNamespace =
        "OrbModding.Common.Runtime.ServiceCycle.Observation.Journal";
    private const string DecisionJournalStatusNamespace = DecisionJournalNamespace + ".Status";
    private const string HostTraceNamespace =
        "OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace";
    private const string HostTraceControlNamespace = HostTraceNamespace + ".Control";
    private static readonly string[] ForbiddenNamespaces =
        { FullTraceNamespace, DecisionJournalNamespace, HostTraceNamespace };
    private static readonly string[] AllowedNamespaces =
        { ControlNamespace, DecisionJournalStatusNamespace, HostTraceControlNamespace };
    private const BindingFlags DeclaredMembers =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Assembly SuiteAssembly = typeof(global::OrbModConfig.ModConfigUiShell).Assembly;

    [Fact]
    public void ModConfigSeesObservabilityOnlyThroughItsPublicPorts()
    {
        var violations = new List<string>();
        // One DLL now holds the ports and their implementations alike, so the scan is scoped to
        // the namespace this rule was always about: Mod Config may only see observability through
        // the public control and status ports.
        var modConfigTypes = SuiteAssembly.GetTypes()
            .Where(type => (type.Namespace ?? string.Empty)
                .StartsWith("OrbModConfig", StringComparison.Ordinal));
        foreach (var type in modConfigTypes) AuditType(type, violations);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ScannerDetectsLocalOnlyImplementationReference()
    {
        var method = typeof(ObservabilityPortArchitectureTests).GetMethod(
            nameof(LocalOnlyForbiddenJournalReference),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var violations = new List<string>();

        AuditCallable(method, violations);

        Assert.Contains(violations, violation =>
            violation.Contains("ServiceCycleDecisionJournalRuntime", StringComparison.Ordinal));
    }

    [Fact]
    public void ScannerDetectsStaticInitializerReference()
    {
        var violations = new List<string>();

        AuditCallable(typeof(ForbiddenStaticTraceReference).TypeInitializer!, violations);

        Assert.Contains(violations, violation =>
            violation.Contains("FullTraceRuntimeSession", StringComparison.Ordinal));
    }

    private static void AuditType(Type type, ICollection<string> violations)
    {
        var location = type.FullName ?? type.Name;
        if (type.BaseType is not null) AuditReference(type.BaseType, location + " base", violations);
        foreach (var contract in type.GetInterfaces())
            AuditReference(contract, location + " interface", violations);
        foreach (var field in type.GetFields(DeclaredMembers))
            AuditReference(field.FieldType, location + "." + field.Name + " field", violations);
        foreach (var property in type.GetProperties(DeclaredMembers))
            AuditReference(property.PropertyType, location + "." + property.Name + " property", violations);
        foreach (var eventInfo in type.GetEvents(DeclaredMembers))
            AuditReference(eventInfo.EventHandlerType!, location + "." + eventInfo.Name + " event", violations);

        var callables = type.GetMethods(DeclaredMembers).Cast<MethodBase>()
            .Concat(type.GetConstructors(DeclaredMembers));
        if (type.TypeInitializer is not null) callables = callables.Append(type.TypeInitializer);
        foreach (var callable in callables) AuditCallable(callable, violations);
    }

    private static void AuditCallable(MethodBase callable, ICollection<string> violations)
    {
        var location = (callable.DeclaringType?.FullName ?? "unknown") + "." + callable.Name;
        if (callable is MethodInfo method) AuditReference(method.ReturnType, location + " return", violations);
        foreach (var parameter in callable.GetParameters())
            AuditReference(parameter.ParameterType, location + " parameter", violations);
        var body = callable.GetMethodBody();
        if (body is null) return;
        foreach (var local in body.LocalVariables)
            AuditReference(local.LocalType, location + " local", violations);
        ServiceCycleIlDependencyScanner.Audit(
            callable,
            body,
            location,
            violations,
            ForbiddenNamespaces,
            AllowedNamespaces);
    }

    private static void AuditReference(Type type, string location, ICollection<string> violations)
    {
        if (type.HasElementType) type = type.GetElementType()!;
        if (type.IsGenericParameter) return;
        var candidateNamespace = type.Namespace ?? string.Empty;
        foreach (var forbidden in ForbiddenNamespaces)
        {
            if (!IsNamespaceOrChild(candidateNamespace, forbidden) || IsAllowed(candidateNamespace)) continue;
            violations.Add(location + " references forbidden observability type " + type.FullName);
            break;
        }
        if (!type.IsGenericType) return;
        foreach (var argument in type.GetGenericArguments()) AuditReference(argument, location, violations);
    }

    private static bool IsNamespaceOrChild(string candidate, string expected) =>
        string.Equals(candidate, expected, StringComparison.Ordinal) ||
        candidate.StartsWith(expected + ".", StringComparison.Ordinal);

    private static bool IsAllowed(string candidate)
    {
        foreach (var allowed in AllowedNamespaces)
        {
            if (IsNamespaceOrChild(candidate, allowed)) return true;
        }
        return false;
    }

    private static Type LocalOnlyForbiddenJournalReference()
    {
        global::OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.ServiceCycleDecisionJournalRuntime? value = null;
        GC.KeepAlive(value);
        return value?.GetType() ?? typeof(object);
    }

    private static class ForbiddenStaticTraceReference
    {
        internal static readonly Type Value = typeof(
            global::OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.FullTraceRuntimeSession);
    }
}
