using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class EvidenceStrengthTests
{
    [Fact]
    public void MinimumRequiresBothStrengthAndEveryNamedSource()
    {
        var assessment = new EvidenceAssessment(
            EvidenceLevel.SerializedAssetVerified,
            EvidenceSource.SerializedAsset |
            EvidenceSource.RuntimeNativeType |
            EvidenceSource.StableIdentity);

        Assert.True(assessment.Meets(
            EvidenceLevel.RuntimeObserved,
            EvidenceSource.RuntimeNativeType | EvidenceSource.StableIdentity));
        Assert.False(assessment.Meets(EvidenceLevel.StaticallyVerified));
        Assert.False(assessment.Meets(
            EvidenceLevel.RuntimeObserved,
            EvidenceSource.RuntimeRegistry));
    }

    [Fact]
    public void ContradictionAlwaysDegradesToUnresolved()
    {
        var assessment = EvidenceAssessment.Contradictory(
            EvidenceSource.SerializedAsset | EvidenceSource.RuntimeRegistry);

        Assert.Equal(EvidenceLevel.Unresolved, assessment.Level);
        Assert.True(assessment.IsContradictory);
        Assert.False(assessment.IsResolved);
        Assert.False(assessment.Meets(EvidenceLevel.Inferred));
    }
}
