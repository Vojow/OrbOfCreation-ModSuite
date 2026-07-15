using OrbAutomata;
using OrbMentor;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class StatusControlGroupTests
{
    [Theory]
    [InlineData(0.0f, 0.0f, 50.0f, 62.0f)]
    [InlineData(100.0f, 0.5f, 50.0f, 137.0f)]
    [InlineData(100.0f, 1.0f, 50.0f, 112.0f)]
    public void GroupStartsOneGapBeyondNativeRightEdge(float nativeX, float pivotX, float width, float expectedX)
    {
        var nativePosition = new Vector2(nativeX, 20.0f);
        var nativePivot = new Vector2(pivotX, 0.5f);

        var automata = StatusControlGroup.CalculateGroupPosition(nativePosition, nativePivot, width, 50.0f);
        var mentor = MentorStatusControlGroup.CalculateGroupPosition(nativePosition, nativePivot, width, 50.0f);

        Assert.Equal(expectedX, automata.x);
        Assert.Equal(20.0f, automata.y);
        Assert.Equal(automata.x, mentor.x);
        Assert.Equal(automata.y, mentor.y);
    }

    [Fact]
    public void ModControlsUseConsecutiveSlotsWithUniformGaps()
    {
        const float width = 50.0f;
        var expectedCenters = new[] { 25.0f, 87.0f, 149.0f };

        for (var slot = 0; slot < expectedCenters.Length; slot++)
        {
            Assert.Equal(expectedCenters[slot], StatusControlGroup.CalculateSlotCenterX(width, slot));
            Assert.Equal(expectedCenters[slot], MentorStatusControlGroup.CalculateSlotCenterX(width, slot));
        }

        Assert.Equal(12.0f, expectedCenters[1] - expectedCenters[0] - width);
        Assert.Equal(12.0f, expectedCenters[2] - expectedCenters[1] - width);
    }
}
