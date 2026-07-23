using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplayPublicShapeTests
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    {
        "UnityEngine",
        "BepInEx",
        "HarmonyLib",
        "System.IO",
        "System.Reflection",
        "System.Security.Principal",
        "Microsoft.Win32.SafeHandles",
    };

    private static readonly string[] PrivacyBearingMemberTokens =
    {
        "Exception",
        "Host",
        "Path",
        "Save",
        "User",
    };

    [Fact]
    public void PublicReplayArtifactAndExportDtosContainNoPrivacyBearingSurface()
    {
        var formatNamespace = typeof(ServiceCycleReplayArtifactDocument).Namespace!;
        var roots = typeof(ServiceCycleReplayArtifactDocument).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == formatNamespace &&
                (type.Name.StartsWith("ServiceCycleReplayArtifact", StringComparison.Ordinal) ||
                 type.Name.StartsWith("ServiceCycleReplayExport", StringComparison.Ordinal) ||
                 type == typeof(ServiceCycleReplayCodecManifestEntry) ||
                 type == typeof(ServiceCycleReplaySemanticJoin)))
            .OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal)
            .ToArray();
        var visited = new HashSet<Type>();
        var violations = new List<string>();

        foreach (var root in roots)
            AuditPublicDataGraph(root, root.FullName ?? root.Name, visited, violations);

        Assert.NotEmpty(roots);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void PublicReplayObserverContainsOnlyPrimitiveAndEnumEvidence()
    {
        var observer = typeof(IServiceCycleReplayExportObserver);
        var visited = new HashSet<Type>();
        var violations = new List<string>();
        foreach (var method in observer.GetMethods())
        {
            var location = observer.FullName + "." + method.Name;
            AuditMemberName(method.Name, location, violations);
            AuditPublicDataGraph(method.ReturnType, location + " return", visited, violations);
            foreach (var parameter in method.GetParameters())
            {
                AuditPublicDataGraph(
                    parameter.ParameterType,
                    location + " parameter " + parameter.Name,
                    visited,
                    violations);
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ExporterPreservesThePreObserverConstructorSignature()
    {
        Assert.NotNull(typeof(ServiceCycleReplayArtifactExporter).GetConstructor(new[]
        {
            typeof(ServiceCycleSemanticTraceSource),
            typeof(ServiceCycleReplaySession),
            typeof(IRestartAwareTraceSegmentStorage),
            typeof(ServiceCycleReplayExportOptions),
        }));
    }

    private static void AuditPublicDataGraph(
        Type candidate,
        string location,
        ISet<Type> visited,
        ICollection<string> violations)
    {
        while (candidate.IsByRef || candidate.IsPointer)
            candidate = candidate.GetElementType()!;
        if (candidate.IsGenericParameter || candidate == typeof(void)) return;
        if (candidate == typeof(byte[])) return;
        if (candidate.IsArray)
        {
            violations.Add(location + " exposes array payload " + candidate.FullName);
            AuditPublicDataGraph(candidate.GetElementType()!, location + " element", visited, violations);
            return;
        }

        if (candidate == typeof(string) || candidate == typeof(object) ||
            candidate == typeof(IntPtr) || candidate == typeof(UIntPtr) ||
            candidate == typeof(Type))
            violations.Add(location + " exposes privacy-bearing or opaque type " + candidate.FullName);
        if (candidate.IsInterface)
            violations.Add(location + " exposes interface payload " + candidate.FullName);
        if (typeof(Exception).IsAssignableFrom(candidate))
            violations.Add(location + " exposes exception payload " + candidate.FullName);
        if (typeof(Delegate).IsAssignableFrom(candidate))
            violations.Add(location + " exposes delegate payload " + candidate.FullName);
        if (typeof(System.Runtime.InteropServices.SafeHandle).IsAssignableFrom(candidate) ||
            typeof(System.Threading.WaitHandle).IsAssignableFrom(candidate))
            violations.Add(location + " exposes handle payload " + candidate.FullName);
        if (candidate != typeof(string) && typeof(IEnumerable).IsAssignableFrom(candidate))
            violations.Add(location + " exposes collection payload " + candidate.FullName);

        var candidateNamespace = candidate.Namespace ?? string.Empty;
        if (ForbiddenNamespacePrefixes.Any(prefix =>
                candidateNamespace.StartsWith(prefix, StringComparison.Ordinal)))
            violations.Add(location + " exposes forbidden type " + candidate.FullName);
        var candidateAssembly = candidate.Assembly.GetName().Name ?? string.Empty;
        if (candidateAssembly is "Assembly-CSharp" or "Assembly-CSharp-firstpass" ||
            candidateAssembly.StartsWith("UnityEngine", StringComparison.Ordinal))
            violations.Add(location + " exposes game assembly type " + candidate.FullName);
        if (candidate.Name.Contains("NativeObject", StringComparison.Ordinal) ||
            candidate.Name.Contains("Save", StringComparison.Ordinal))
            violations.Add(location + " exposes forbidden DTO " + candidate.FullName);

        if (candidate.IsGenericType)
        {
            foreach (var argument in candidate.GetGenericArguments())
                AuditPublicDataGraph(argument, location + " generic argument", visited, violations);
        }
        if (candidate.Assembly != typeof(ServiceCycleReplayArtifactDocument).Assembly ||
            candidate.IsPrimitive || candidate.IsEnum || !visited.Add(candidate))
            return;

        const BindingFlags publicDeclared = BindingFlags.Public | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var field in candidate.GetFields(publicDeclared))
        {
            AuditMemberName(field.Name, location + "." + field.Name, violations);
            AuditPublicDataGraph(field.FieldType, location + "." + field.Name, visited, violations);
        }
        foreach (var property in candidate.GetProperties(publicDeclared))
        {
            AuditMemberName(property.Name, location + "." + property.Name, violations);
            AuditPublicDataGraph(property.PropertyType, location + "." + property.Name, visited, violations);
        }
        foreach (var method in candidate.GetMethods(publicDeclared))
        {
            if (method.IsSpecialName || IsObjectContract(method)) continue;
            AuditMemberName(method.Name, location + "." + method.Name, violations);
            AuditPublicDataGraph(method.ReturnType, location + "." + method.Name + " return", visited, violations);
            foreach (var parameter in method.GetParameters())
            {
                AuditPublicDataGraph(
                    parameter.ParameterType,
                    location + "." + method.Name + " parameter " + parameter.Name,
                    visited,
                    violations);
            }
        }
    }

    private static void AuditMemberName(
        string name,
        string location,
        ICollection<string> violations)
    {
        foreach (var token in PrivacyBearingMemberTokens)
        {
            if (name.Contains(token, StringComparison.Ordinal))
                violations.Add(location + " has privacy-bearing member name " + token);
        }
    }

    private static bool IsObjectContract(MethodInfo method) =>
        method.Name == nameof(object.Equals) ||
        method.Name == nameof(object.GetHashCode) ||
        method.Name == nameof(object.ToString);
}
