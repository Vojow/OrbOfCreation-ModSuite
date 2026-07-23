using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services;

/// <summary>
/// Mechanically enforces the runtime dependency direction so that namespaces and folders
/// are not the only boundary. Common must depend only on neutral contracts and value types;
/// it must not reference a feature plugin assembly, contain a feature domain identity type,
/// or reintroduce the superseded runtime substrate.
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    private static readonly Assembly CommonAssembly = typeof(SuitePerformanceCoordinator).Assembly;
    private static readonly Assembly AutomataAssembly = typeof(OrbAutomata.BepInExAutomataConfiguration).Assembly;

    private static readonly string[] SupersededRuntimeNamespaces =
    {
        "OrbModding.Common.Runtime.Contracts",
        "OrbModding.Common.Runtime.Host",
        "OrbModding.Common.Runtime.Kernel",
        "OrbModding.Common.Runtime.Lanes",
        "OrbModding.Common.Runtime.Process",
        "OrbModding.Common.Runtime.Telemetry",
        "OrbModding.Common.Runtime.Views",
    };

    private static readonly string[] SupersededRuntimeTypeNames =
    {
        "BoundedTraceWriter",
        "CausalTraceCodec",
        "CausalTraceDocument",
        "CausalTraceReplayOracle",
        "CausalTraceRecord",
        "CommandGeneration",
        "CommandId",
        "DemandPulledLiveView",
        "LiveViewBufferPool",
        "PlanGeneration",
        "PolicyGeneration",
        "RuntimeGenerationSet",
        "RuntimeCycleDiagnostics",
        "RuntimeServiceId",
        "RuntimeWorkKey",
        "SnapshotGeneration",
        "StrategyScope",
        "TraceCapture",
        "TraceCaptureDrainReport",
        "TraceWriterMetrics",
        "WorkRequestId",
        "WorkSessionId",
    };

    private static readonly string[] FeatureIdentityFragments =
    {
        "AutoHarvest", "AutoBuy", "AutoCast", "AutoConcept",
        "AutoSpell", "SpellLevel", "Mentor", "Agrimancy", "Agromancy",
    };

    [Fact]
    public void CommonDoesNotReferenceAnyFeaturePluginAssembly()
    {
        var referenced = CommonAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.DoesNotContain("OrbAutomata", referenced);
        Assert.DoesNotContain("OrbMentor", referenced);
        Assert.DoesNotContain("OrbModConfig", referenced);
    }

    [Fact]
    public void AutomataReferencesCommon_ProvingTheDirectionIsOneWay()
    {
        var referenced = AutomataAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.Contains("OrbModding.Common", referenced);
    }

    [Fact]
    public void CommonContainsNoFeatureDomainIdentityType()
    {
        var offenders = CommonAssembly.GetTypes()
            .Where(t => FeatureIdentityFragments.Any(fragment =>
                t.Name.IndexOf(fragment, StringComparison.Ordinal) >= 0))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CommonRuntimeContainsNoFeatureDomainIdentityType()
    {
        var offenders = CommonAssembly.GetTypes()
            .Where(t => t.Namespace is not null &&
                        t.Namespace.StartsWith("OrbModding.Common.Runtime", StringComparison.Ordinal))
            .Where(t => FeatureIdentityFragments.Any(fragment =>
                t.Name.IndexOf(fragment, StringComparison.Ordinal) >= 0))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CommonContainsNoSupersededRuntimeSubstrate()
    {
        var types = CommonAssembly.GetTypes();
        var namespaceOffenders = types
            .Where(type => SupersededRuntimeNamespaces.Any(prefix =>
                type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true))
            .Select(type => type.FullName)
            .ToArray();
        var typeOffenders = types
            .Where(type => SupersededRuntimeTypeNames.Contains(
                type.Name.Split('`')[0],
                StringComparer.Ordinal))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(namespaceOffenders);
        Assert.Empty(typeOffenders);
    }

    [Fact]
    public void AutoHarvestServiceCycleDoesNotReferenceTheLegacyPerformanceCoordinator()
    {
        var forbidden = new[]
        {
            typeof(SuitePerformanceCoordinator),
            typeof(SuiteWorkRegistration),
            typeof(SuiteWorkLease),
            typeof(SuiteWorkAdmission),
            typeof(SuitePerformanceWorkIdentity),
        };
        var offenders = AutomataAssembly.GetTypes()
            .Where(type =>
                type.Name.IndexOf("AutoHarvest", StringComparison.Ordinal) >= 0 ||
                type.Name == "AutomataReplayExportPort")
            .SelectMany(type => ReferencedMemberTypes(type)
                .Where(reference => ContainsAny(reference, forbidden))
                .Select(reference => $"{type.FullName} -> {reference.FullName}"))
            .Distinct()
            .ToArray();

        Assert.Empty(offenders);
    }

    private static Type[] ReferencedMemberTypes(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Static |
                       BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Concat(type.GetConstructors(BindingFlags.Instance |
                                         BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType))
            .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                    BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType)))
            .ToArray();

    private static bool ContainsAny(Type candidate, Type[] forbidden)
    {
        if (forbidden.Contains(candidate) ||
            candidate.IsGenericType && forbidden.Contains(candidate.GetGenericTypeDefinition()))
            return true;
        if (candidate.HasElementType)
            return ContainsAny(candidate.GetElementType()!, forbidden);
        return candidate.IsGenericType &&
               candidate.GetGenericArguments().Any(argument => ContainsAny(argument, forbidden));
    }
}
