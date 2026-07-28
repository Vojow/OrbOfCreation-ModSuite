using System;
using OrbMentor;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.Mentor.Runtime.World;

public sealed class MentorMasteryEventJournalTests
{
    [Fact]
    public void ObservationsPublishInSequenceAndRepeatAcrossWorldGenerations()
    {
        var journal = new MentorMasteryEventJournal();
        var source = Guid.NewGuid();
        journal.Publish(
            7,
            MasteryExperienceDomain.Spell,
            source,
            4,
            true,
            new MentorAmount(2.5, 3));

        var first = Collect(journal, 7);
        var second = Collect(journal, 7);

        Assert.Equal(1, first.MasteryExperience.Count);
        Assert.Equal(1, second.MasteryExperience.Count);
        Assert.Equal(1, first.MasteryExperience[0].Sequence);
        Assert.Equal(source, first.MasteryExperience[0].SourceId);
        Assert.Equal(4, first.MasteryExperience[0].SourceMastery);
        Assert.True(first.MasteryExperience[0].SourceEligible);
        Assert.Equal(2500d, first.MasteryExperience[0].Amount.ToDouble());
        Assert.Equal(first.MasteryExperience[0].Sequence, second.MasteryExperience[0].Sequence);
    }

    [Fact]
    public void ANewLifecycleDropsOldInputsAndRestartsItsSequence()
    {
        var journal = new MentorMasteryEventJournal();
        journal.Publish(
            3,
            MasteryExperienceDomain.Alchemy,
            Guid.NewGuid(),
            1,
            true,
            new MentorAmount(1, 0));
        journal.Publish(
            4,
            MasteryExperienceDomain.Artifact,
            Guid.NewGuid(),
            2,
            true,
            new MentorAmount(2, 0));

        Assert.Equal(0, Collect(journal, 3).MasteryExperience.Count);
        var current = Collect(journal, 4);
        Assert.Equal(1, current.MasteryExperience.Count);
        Assert.Equal(1, current.MasteryExperience[0].Sequence);
        Assert.Equal(MasteryExperienceDomain.Artifact, current.MasteryExperience[0].Domain);
    }

    [Fact]
    public void CapacityKeepsTheNewestBoundedHistory()
    {
        var journal = new MentorMasteryEventJournal(capacity: 2);
        for (var index = 0; index < 3; index++)
        {
            journal.Publish(
                9,
                MasteryExperienceDomain.Spell,
                Guid.NewGuid(),
                index,
                true,
                new MentorAmount(1, 0));
        }

        var world = Collect(journal, 9);

        Assert.Equal(2, world.MasteryExperience.Count);
        Assert.Equal(2, world.MasteryExperience[0].Sequence);
        Assert.Equal(3, world.MasteryExperience[1].Sequence);
        Assert.Equal(1, journal.Overwritten);
    }

    private static GameWorldState Collect(
        MentorMasteryEventJournal journal,
        long lifecycleEpoch)
    {
        var collector = new GameWorldCollector(
            _ => null,
            () => 0.02d,
            journal);
        var frame = new GameWorldCycleFrame
        {
            CollectedAtEpoch = lifecycleEpoch,
        };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }
}
