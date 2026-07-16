using OrbMentor;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorPerformanceTests
{
    [Fact]
    public void SourceEventsCoalesceByQualifiedSourceWithoutRetroactiveQualification()
    {
        var events = new MentorSourceAccumulator();

        events.Capture("lower-source", new MentorAmount(9, 2), qualifiesAtEvent: false);
        events.Capture("lower-source", new MentorAmount(1, 3), qualifiesAtEvent: true);
        events.Capture("mentor-a", new MentorAmount(1, 3), qualifiesAtEvent: true);
        events.Capture("mentor-a", new MentorAmount(2, 3), qualifiesAtEvent: true);
        events.Capture("mentor-b", new MentorAmount(4, 3), qualifiesAtEvent: true);

        Assert.Equal(3, events.SourceCount);
        var total = events.Drain();
        Assert.Equal(8, total.Mantissa, 12);
        Assert.Equal(3, total.Exponent);
        Assert.False(events.HasPending);
    }

    [Fact]
    public void RecipientPlanResumesWithoutDroppingUnexpandedWork()
    {
        var recipients = new[]
        {
            new MentorRecipe("a", 0, true),
            new MentorRecipe("b", 1, true),
            new MentorRecipe("c", 2, true),
        };
        var engine = new MentorEngine();
        var plan = Assert.IsType<MentorPlan>(engine.CreatePlan(
            new MentorAmount(3, 3),
            30,
            MentorEconomyMode.SharedPool,
            recipients));

        Assert.True(plan.TryTake(out var first));
        Assert.Equal("a", first.Uuid);
        Assert.Equal(2, plan.RemainingCount);

        Assert.True(plan.TryTake(out var second));
        Assert.True(plan.TryTake(out var third));
        Assert.Equal(new[] { "b", "c" }, new[] { second.Uuid, third.Uuid });
        Assert.Equal(0, plan.RemainingCount);
        Assert.False(plan.TryTake(out _));

        Assert.Equal(first.Amount, second.Amount);
        Assert.Equal(second.Amount, third.Amount);
        Assert.Equal(3, first.Amount.Mantissa, 12);
        Assert.Equal(2, first.Amount.Exponent);
    }

    [Fact]
    public void CancellationClearsCapturedEventsAndExpandedRecipientWork()
    {
        var events = new MentorSourceAccumulator();
        var engine = new MentorEngine();
        events.Capture("mentor", new MentorAmount(1, 3), qualifiesAtEvent: true);
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(1, 2)));

        events.Cancel();
        engine.Cancel();

        Assert.False(events.HasPending);
        Assert.Equal(0, engine.PendingCount);
    }
}
