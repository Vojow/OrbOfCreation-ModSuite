using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class UnverifiedBuildCompatibilityPolicyTests
{
    private const string Current =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:" +
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Previous =
        "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC:" +
        "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";

    [Fact]
    public void UnknownBuildStartsEmergencyStoppedWithoutConsent()
    {
        var decision = UnverifiedBuildCompatibilityPolicy.AtStartup(
            audited: false,
            Current,
            overrideRequested: false,
            acceptedFingerprint: string.Empty);

        Assert.False(decision.RuntimeAllowed);
        Assert.True(decision.EngageEmergencyStop);
        Assert.False(decision.ResetOverride);
    }

    [Fact]
    public void ConsentSurvivesOnlyForTheExactAssemblyPair()
    {
        var exact = UnverifiedBuildCompatibilityPolicy.AtStartup(
            audited: false,
            Current,
            overrideRequested: true,
            acceptedFingerprint: Current.ToLowerInvariant());
        var changed = UnverifiedBuildCompatibilityPolicy.AtStartup(
            audited: false,
            Current,
            overrideRequested: true,
            acceptedFingerprint: Previous);

        Assert.True(exact.RuntimeAllowed);
        Assert.False(exact.EngageEmergencyStop);
        Assert.False(changed.RuntimeAllowed);
        Assert.True(changed.ResetOverride);
        Assert.True(changed.EngageEmergencyStop);
    }

    [Fact]
    public void ExplicitOptInAcceptsThePairAlreadyObservedThisSession()
    {
        var decision = UnverifiedBuildCompatibilityPolicy.AfterExplicitChange(
            audited: false,
            Current,
            overrideRequested: true,
            acceptedFingerprint: Previous);

        Assert.True(decision.RuntimeAllowed);
        Assert.True(decision.AcceptObserved);
        Assert.False(decision.EngageEmergencyStop);
    }

    [Fact]
    public void RemovingOptInReengagesEmergencyStop()
    {
        var decision = UnverifiedBuildCompatibilityPolicy.AfterExplicitChange(
            audited: false,
            Current,
            overrideRequested: false,
            acceptedFingerprint: Current);

        Assert.False(decision.RuntimeAllowed);
        Assert.True(decision.EngageEmergencyStop);
    }

    [Fact]
    public void AuditedBuildNeverNeedsAnOverride()
    {
        var decision = UnverifiedBuildCompatibilityPolicy.AtStartup(
            audited: true,
            observedFingerprint: string.Empty,
            overrideRequested: false,
            acceptedFingerprint: string.Empty);

        Assert.True(decision.RuntimeAllowed);
        Assert.False(decision.EngageEmergencyStop);
    }
}
