using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace OrbModding.Tests;

public sealed class TestArchitectureTests
{
    [Fact]
    public void PortableCiPartitionMarkers_AreMutuallyExclusive()
    {
        var conflicts = new List<string>();
        var assembly = typeof(TestArchitectureTests).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            var classCategories = Categories(type);
            foreach (var method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.Instance |
                         BindingFlags.Static))
            {
                if (!method.CustomAttributes.Any(attribute =>
                        typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType)))
                {
                    continue;
                }

                var categories = new HashSet<string>(classCategories, StringComparer.Ordinal);
                categories.UnionWith(Categories(method));
                if (categories.Contains("PerformanceSimulation") &&
                    categories.Contains("ExternalProcess"))
                {
                    conflicts.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        Assert.True(conflicts.Count == 0,
            "Portable tests cannot belong to both exclusive CI partitions: " +
            string.Join(", ", conflicts));
    }

    private static IEnumerable<string> Categories(MemberInfo member) =>
        member.CustomAttributes
            .Where(attribute =>
                attribute.AttributeType == typeof(TraitAttribute) &&
                attribute.ConstructorArguments.Count == 2 &&
                string.Equals(
                    attribute.ConstructorArguments[0].Value as string,
                    "Category",
                    StringComparison.Ordinal))
            .Select(attribute => attribute.ConstructorArguments[1].Value as string)
            .Where(value => value != null)
            .Select(value => value!);
}
