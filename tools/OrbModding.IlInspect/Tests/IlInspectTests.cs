using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace OrbModding.IlInspect.Tests;

public sealed class IlInspectTests
{
    private static readonly string FixtureAssembly = typeof(IlInspectTests).Assembly.Location;

    [Fact]
    public void HeaderPinsResolvedPathSha256AndMvid()
    {
        using var inspector = AssemblyInspector.Open(FixtureAssembly);
        using var output = new StringWriter();

        inspector.WriteHeader(output);

        var text = output.ToString();
        Assert.Contains($"assembly: {Path.GetFullPath(FixtureAssembly)}", text);
        Assert.Contains($"sha256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FixtureAssembly))).ToLowerInvariant()}", text);
        Assert.Contains($"mvid: {typeof(IlInspectTests).Module.ModuleVersionId:D}", text);
    }

    [Fact]
    public void TypeShowsBaseInterfacesAndMemberModifiers()
    {
        var text = Inspect("type", typeof(DeclaredImplementation).FullName!);

        Assert.Contains($"type {NameOf<DeclaredImplementation>()}", text);
        Assert.Contains(NameOf<FixtureBase>(), text);
        Assert.Contains($"{NameOf<IFixtureContract>()} [declared]", text);
        Assert.Contains("FixtureVirtual(System.Int32)", text);
        Assert.Contains("public static field", text);
        Assert.Contains("public virtual final method", text);
    }

    [Fact]
    public void MethodPrintsReadableIlAndFullMemberNames()
    {
        var text = Inspect("method", $"{typeof(DeclaredImplementation).FullName}::CallTarget");

        Assert.Contains($"method {NameOf<DeclaredImplementation>()}.CallTarget()", text);
        Assert.Contains($"{NameOf<DeclaredImplementation>()}.TargetMethod(System.Int32)", text);
        Assert.Contains($"{NameOf<DeclaredImplementation>()}.TargetField", text);
        Assert.Contains("fixture literal", text);
        Assert.Contains("IL_", text);
    }

    [Fact]
    public void CallersFindsMethodAndFieldReferenceSites()
    {
        var methodText = Inspect("callers", $"{typeof(DeclaredImplementation).FullName}::TargetMethod");
        var fieldText = Inspect("callers", $"{typeof(DeclaredImplementation).FullName}::TargetField");

        Assert.Contains($"{NameOf<DeclaredImplementation>()}.CallTarget()", methodText);
        Assert.Contains(" call ", methodText);
        Assert.Contains($"{NameOf<DeclaredImplementation>()}.CallTarget()", fieldText);
        Assert.Contains("ldsfld", fieldText);
    }

    [Fact]
    public void ImplementersDistinguishesDeclarationFromInheritance()
    {
        var text = Inspect("implementers", typeof(IFixtureContract).FullName!);

        Assert.Contains($"{NameOf<DeclaredImplementation>()} [declared]", text);
        Assert.Contains($"{NameOf<InheritedImplementation>()} [inherited]", text);
    }

    [Fact]
    public void StringsListsEachContainingMethod()
    {
        var text = Inspect("strings", "fixture literal");

        Assert.Contains($"{NameOf<DeclaredImplementation>()}.CallTarget()", text);
        Assert.Contains("\"fixture literal\"", text);
    }

    [Fact]
    public void CommandLineUsesEnvironmentAndRejectsAssemblyTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"il-inspect-{Guid.NewGuid():N}");
        var managed = Path.Combine(root, "Orb Of Creation_Data", "Managed");
        Directory.CreateDirectory(managed);
        var target = Path.Combine(managed, "Assembly-CSharp.dll");
        File.Copy(FixtureAssembly, target);
        var alternateTarget = Path.Combine(managed, "Fixture.dll");
        File.Copy(FixtureAssembly, alternateTarget);
        try
        {
            var command = CommandLine.Parse(
                new[] { "strings", "fixture" },
                () => root);
            Assert.Equal(target, command.AssemblyPath);

            var alternate = CommandLine.Parse(
                new[] { "--game-dir", root, "--assembly", "Fixture.dll", "strings", "fixture" },
                () => null);
            Assert.Equal(alternateTarget, alternate.AssemblyPath);

            Assert.Throws<CommandLineException>(() => CommandLine.Parse(
                new[] { "--game-dir", root, "--assembly", "../other.dll", "strings", "fixture" },
                () => null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Inspect(string verb, string query)
    {
        using var inspector = AssemblyInspector.Open(FixtureAssembly);
        using var output = new StringWriter();
        inspector.Execute(verb, query, output);
        return output.ToString();
    }

    private static string NameOf<T>() => typeof(T).FullName!.Replace('+', '.');

    private interface IFixtureContract
    {
    }

    private abstract class FixtureBase
    {
        public virtual int FixtureVirtual(int value) => value;
    }

    private class DeclaredImplementation : FixtureBase, IFixtureContract
    {
        public static readonly int TargetField = 3;

        public sealed override int FixtureVirtual(int value) => value;

        public int TargetMethod(int value) => value + 1;

        public int CallTarget()
        {
            Consume("fixture literal");
            return TargetMethod(TargetField);
        }

        private static void Consume(string value) => _ = value.Length;
    }

    private sealed class InheritedImplementation : DeclaredImplementation
    {
    }
}
