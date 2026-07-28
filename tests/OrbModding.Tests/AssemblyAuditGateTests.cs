using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// The gate every native mutation in the suite sits behind: a game binary the suite does not
/// recognise means no mutation at all, whichever feature was asking.
/// </summary>
public sealed class AssemblyAuditGateTests
{
    [Fact]
    public void AssemblyHashMismatchFailsClosedBeforeNativeMutationSetup()
    {
        var matching = new AssemblyAuditResult(
            new AssemblyHashResult("main", GameAssemblyAudit.WindowsAssemblyCSharpSha256),
            new AssemblyHashResult("first", GameAssemblyAudit.WindowsFirstPassSha256));
        var mismatch = new AssemblyAuditResult(
            new AssemblyHashResult("main", GameAssemblyAudit.MacAssemblyCSharpSha256),
            new AssemblyHashResult("first", GameAssemblyAudit.WindowsFirstPassSha256));

        Assert.True(global::OrbModding.Plugin.AssemblyAuditAllowsMutation(matching));
        Assert.False(global::OrbModding.Plugin.AssemblyAuditAllowsMutation(mismatch));
    }
}
