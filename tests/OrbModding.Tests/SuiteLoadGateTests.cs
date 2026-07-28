using System;
using System.IO;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// The load gate is the guard that makes owning the game's economy math safe: the ported arithmetic
/// is only valid for one audited assembly pair, so any other build must not run the suite at all.
/// These tests pin refusal, and pin that a refusal is actionable rather than merely silent.
/// </summary>
public sealed class SuiteLoadGateTests
{
    [Fact]
    public void AnUnknownBuildIsRefused()
    {
        using var install = new FakeInstall(
            assemblyCSharp: "not the audited assembly",
            firstPass: "nor is this");

        var decision = SuiteLoadGate.Evaluate(install.Root);

        Assert.False(decision.ShouldLoad);
        Assert.Empty(decision.BaselineId);
    }

    [Fact]
    public void ARefusalNamesTheObservedHashesAndTheAuditedBaselines()
    {
        // "The mod did not load" without evidence is indistinguishable from a broken install. The
        // message has to carry enough to act on, because the gate deliberately offers no way to
        // continue.
        using var install = new FakeInstall("unknown", "unknown");

        var decision = SuiteLoadGate.Evaluate(install.Root);

        Assert.Contains("Refusing to load", decision.Message, StringComparison.Ordinal);
        Assert.Contains(GameAssemblyAudit.WindowsBaselineId, decision.Message, StringComparison.Ordinal);
        Assert.Contains(GameAssemblyAudit.MacBaselineId, decision.Message, StringComparison.Ordinal);
        Assert.Contains(GameAssemblyAudit.MacV1052BaselineId, decision.Message, StringComparison.Ordinal);
        Assert.Contains("Observed Assembly-CSharp=", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingInstallIsRefusedRatherThanTreatedAsUnverifiable()
    {
        // An audit that cannot run is not an audit that passed.
        var decision = SuiteLoadGate.Evaluate(Path.Combine(Path.GetTempPath(), "orb-suite-absent-" + Guid.NewGuid().ToString("N")));

        Assert.False(decision.ShouldLoad);
        Assert.Contains("Refusing to load", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyGameRootIsRefused()
    {
        Assert.False(SuiteLoadGate.Evaluate(string.Empty).ShouldLoad);
    }

    [Fact]
    public void PartialAssemblyPairsAreRefused()
    {
        // One matching assembly is not a matching build; admission requires a complete pair.
        using var install = new FakeInstall(
            assemblyCSharp: "unknown",
            firstPass: null);

        Assert.False(SuiteLoadGate.Evaluate(install.Root).ShouldLoad);
    }

    /// <summary>A throwaway managed-assembly layout the audit will discover and hash.</summary>
    private sealed class FakeInstall : IDisposable
    {
        internal FakeInstall(string? assemblyCSharp, string? firstPass)
        {
            Root = Path.Combine(Path.GetTempPath(), "orb-suite-gate-" + Guid.NewGuid().ToString("N"));
            var managed = Path.Combine(Root, "Orb Of Creation_Data", "Managed");
            Directory.CreateDirectory(managed);

            if (assemblyCSharp is not null)
                File.WriteAllText(Path.Combine(managed, "Assembly-CSharp.dll"), assemblyCSharp);
            if (firstPass is not null)
                File.WriteAllText(Path.Combine(managed, "Assembly-CSharp-firstpass.dll"), firstPass);
        }

        internal string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory must never fail a test run.
            }
        }
    }
}
