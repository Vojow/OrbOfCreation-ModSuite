using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing;

public sealed class ServiceCycleTracingShapeTests
{
    [Fact]
    public void EventAndExportedFactsAreNumericValueOnlyShapes()
    {
        var roots = new[]
        {
            typeof(ServiceCycleTraceSessionId),
            typeof(ServiceCycleTraceServiceId),
            typeof(ServiceCycleTraceEventId),
            typeof(ServiceCycleTraceCursor),
            typeof(ServiceCycleTraceCaptureIdentity),
            typeof(ServiceCycleTraceCycleIdentity),
            typeof(ServiceCycleSemanticPayload),
            typeof(ServiceCycleSemanticEvent),
            typeof(ServiceCycleTraceDropRange),
            typeof(ServiceCycleEventDrain),
            typeof(ServiceCycleTraceGraphValidation),
        };

        foreach (var root in roots) AuditValueGraph(root, root.Name);
    }

    [Fact]
    public void PublicTracingSurfaceContainsNoNativePathIdentityOrExceptionPayloads()
    {
        var tracingTypes = typeof(ServiceCycleSemanticEvent).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                typeof(ServiceCycleSemanticEvent).Namespace!,
                StringComparison.Ordinal) == true)
            .ToArray();
        foreach (var type in tracingTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                AssertSafeSurface(field.FieldType, type.FullName + "." + field.Name);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                AssertSafeSurface(property.PropertyType, type.FullName + "." + property.Name);
        }
    }

    [Fact]
    public void RingHasNoLockOrOpenEndedStorage()
    {
        var fields = typeof(ServiceCycleEventRing).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(object));
        Assert.DoesNotContain(fields, field => typeof(Delegate).IsAssignableFrom(field.FieldType));
        Assert.DoesNotContain(fields, field => field.FieldType.Namespace?.StartsWith("System.Threading", StringComparison.Ordinal) == true);
    }

    private static void AuditValueGraph(Type type, string path)
    {
        if (type.IsEnum || type.IsPrimitive) return;
        Assert.True(type.IsValueType, path + " must be a value type.");
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Assert.False(field.FieldType == typeof(string), path + "." + field.Name);
            Assert.False(field.FieldType.IsArray, path + "." + field.Name);
            Assert.False(typeof(IEnumerable).IsAssignableFrom(field.FieldType), path + "." + field.Name);
            Assert.False(typeof(Delegate).IsAssignableFrom(field.FieldType), path + "." + field.Name);
            Assert.False(typeof(Exception).IsAssignableFrom(field.FieldType), path + "." + field.Name);
            Assert.DoesNotContain("UnityEngine", field.FieldType.FullName ?? string.Empty);
            Assert.DoesNotContain("BepInEx", field.FieldType.FullName ?? string.Empty);
            AuditValueGraph(field.FieldType, path + "." + field.Name);
        }
    }

    private static void AssertSafeSurface(Type type, string path)
    {
        var candidate = type.IsByRef ? type.GetElementType()! : type;
        Assert.False(candidate == typeof(string), path);
        Assert.False(candidate == typeof(object), path);
        Assert.False(typeof(Exception).IsAssignableFrom(candidate), path);
        var name = candidate.FullName ?? string.Empty;
        Assert.DoesNotContain("UnityEngine", name);
        Assert.DoesNotContain("BepInEx", name);
        Assert.DoesNotContain("HarmonyLib", name);
        Assert.DoesNotContain("System.IO.File", name);
        Assert.DoesNotContain("System.IO.Directory", name);
    }
}
