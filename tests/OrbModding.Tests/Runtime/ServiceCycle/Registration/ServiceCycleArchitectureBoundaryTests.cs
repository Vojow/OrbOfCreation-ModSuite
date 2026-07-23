using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleArchitectureTests
{
    [Fact]
    public void PublicServiceCycleSurfaceDoesNotLeakNativeLegacyDelegateOrMutableCollectionTypes()
    {
        var serviceCycleTypes = ServiceCycleTypes(publicOnly: true);
        var violations = new List<string>();
        foreach (var type in serviceCycleTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                AuditPublicSurface(property.PropertyType, type.FullName + "." + property.Name, violations);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                AuditPublicSurface(field.FieldType, type.FullName + "." + field.Name, violations);
            foreach (var method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                AuditPublicSurface(method.ReturnType, type.FullName + "." + method.Name + " return", violations);
                foreach (var parameter in method.GetParameters())
                {
                    if (method.Name == nameof(object.Equals) && parameter.ParameterType == typeof(object)) continue;
                    AuditPublicSurface(parameter.ParameterType, type.FullName + "." + method.Name + " parameter", violations);
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RunnerKeepsFourGenericExecutionShapeAndRegistrationOwnsConfigurationPublication()
    {
        Assert.Equal(4, typeof(ServiceRunner<,,,>).GetGenericArguments().Length);
        Assert.DoesNotContain(typeof(ServiceRunner<,,,>).GetGenericArguments(), argument =>
            argument.Name.Contains("Strategy", StringComparison.Ordinal));
        Assert.Null(typeof(ServiceRunner<,,,>).GetProperty(
            "Configuration", BindingFlags.Instance | BindingFlags.Public));
        Assert.NotNull(typeof(ServiceRegistration<,,,>).GetProperty(
            "Configuration", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void OrdinaryServiceCycleLayersDoNotDependOnTheOptInReplayLayer()
    {
        var violations = new List<string>();
        var ordinaryNamespaces = new[]
        {
            "OrbModding.Common.Runtime.ServiceCycle.Contracts",
            "OrbModding.Common.Runtime.ServiceCycle.Configuration",
            "OrbModding.Common.Runtime.ServiceCycle.Execution",
            "OrbModding.Common.Runtime.ServiceCycle.Registration",
            "OrbModding.Common.Runtime.ServiceCycle.Orchestration",
            "OrbModding.Common.Runtime.ServiceCycle.Diagnostics",
            "OrbModding.Common.Runtime.ServiceCycle.Tracing",
        };

        foreach (var sourceNamespace in ordinaryNamespaces)
        {
            AuditLayerDependencies(
                sourceNamespace,
                new[] { "OrbModding.Common.Runtime.ServiceCycle.Replay" },
                violations);
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void LowerLayersDoNotDependOnUpperLayersAcrossStorageSignaturesLocalsOrIl()
    {
        var violations = new List<string>();

        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Contracts",
            new[]
            {
                "OrbModding.Common.Runtime.ServiceCycle.Configuration",
                "OrbModding.Common.Runtime.ServiceCycle.Execution",
                "OrbModding.Common.Runtime.ServiceCycle.Registration",
                "OrbModding.Common.Runtime.ServiceCycle.Orchestration",
            },
            violations);
        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Configuration",
            new[]
            {
                "OrbModding.Common.Runtime.ServiceCycle.Execution",
                "OrbModding.Common.Runtime.ServiceCycle.Registration",
                "OrbModding.Common.Runtime.ServiceCycle.Orchestration",
            },
            violations);
        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Execution",
            new[]
            {
                "OrbModding.Common.Runtime.ServiceCycle.Registration",
                "OrbModding.Common.Runtime.ServiceCycle.Orchestration",
            },
            violations);
        AuditLayerDependencies(
            "OrbModding.Common.Runtime.ServiceCycle.Registration",
            new[] { "OrbModding.Common.Runtime.ServiceCycle.Orchestration" },
            violations);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
