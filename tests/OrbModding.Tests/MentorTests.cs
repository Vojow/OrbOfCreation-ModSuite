using System;
using System.Linq;
using OrbMentor;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorTests
{
    private sealed class FakeArtifactContainer
    {
        public global::BigDouble Received;
        public void GainExperience(global::BigDouble xp) => Received = xp;
        public int GetGainedLevels() => 2;
        public global::BigDouble GetExperience() => new(3.0, 4);
    }

    private sealed class FakeArtifact
    {
        private readonly FakeArtifactContainer container = new();
        public global::BigDouble masteryXp = default;
        public int masteryLevel;
        public object GetExperienceElement() => container;
        private void GainMasteryLevels(int levels) => masteryLevel += levels;
        public FakeArtifactContainer Container => container;
    }

    private static readonly MentorRecipe Mentor = new("mentor", 5, true);
    private static readonly MentorRecipe[] Recipes =
    {
        Mentor, new("z-recipient", 4, true), new("a-recipient", 0, true),
        new("equal", 5, true), new("locked", 0, false), new("higher", 6, false),
    };

    [Fact]
    public void HighestDiscoveredSourceMentorsEveryLowerDiscoveredRecipeInStableOrder()
    {
        var recipients = new MentorEngine().EligibleRecipients("mentor", Recipes);
        Assert.Equal(new[] { "a-recipient", "z-recipient" }, recipients.Select(r => r.Uuid));
    }

    [Fact]
    public void TiedMentorQualifiesButLowerSourceDoesNot()
    {
        var engine = new MentorEngine();
        Assert.Equal(2, engine.EligibleRecipients("equal", Recipes).Count);
        Assert.Empty(engine.EligibleRecipients("a-recipient", Recipes));
    }

    [Theory]
    [InlineData(MentorEconomyMode.SharedPool, 5.0, 1998L)]
    [InlineData(MentorEconomyMode.PerRecipient, 1.0, 1999L)]
    public void EconomyFormulaIsExactForRecipients(MentorEconomyMode mode, double expectedMantissa, long expectedExponent)
    {
        var engine = new MentorEngine();
        var recipients = engine.EligibleRecipients("mentor", Recipes);
        var grants = engine.Plan(new MentorAmount(1.0, 2000), 10.0, mode, recipients);
        Assert.All(grants, grant => { Assert.Equal(expectedMantissa, grant.Amount.Mantissa, 12); Assert.Equal(expectedExponent, grant.Amount.Exponent); });
    }

    [Fact]
    public void InvalidAndZeroConfigurationCreatesNoWork()
    {
        var engine = new MentorEngine();
        Assert.Empty(engine.Plan(default, 10, MentorEconomyMode.SharedPool, new[] { Mentor }));
        Assert.Empty(engine.Plan(new MentorAmount(1, 0), double.NaN, MentorEconomyMode.SharedPool, new[] { Mentor }));
        Assert.Empty(engine.Plan(new MentorAmount(1, 0), 0, MentorEconomyMode.SharedPool, new[] { Mentor }));
    }

    [Fact]
    public void PendingWorkConsolidatesSpansBudgetsAndCancels()
    {
        var engine = new MentorEngine();
        engine.Consolidate(new[] { new MentorGrant("b", new MentorAmount(1, 3)), new MentorGrant("b", new MentorAmount(2, 3)), new MentorGrant("a", new MentorAmount(1, 2)) });
        var first = Assert.Single(engine.Take(1));
        Assert.Equal("b", first.Uuid);
        Assert.Equal(3, first.Amount.Mantissa, 12);
        Assert.Equal(1, engine.PendingCount);
        Assert.Equal("a", Assert.Single(engine.Take(1)).Uuid);
        engine.Consolidate(new[] { new MentorGrant("x", new MentorAmount(1, 1)) });
        engine.Cancel();
        Assert.Equal(0, engine.PendingCount);
    }

    [Fact]
    public void PendingGrantsKeepDifferentSourceMasteryCeilingsSeparate()
    {
        var engine = new MentorEngine();
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(1, 3), 4));
        engine.Consolidate(new MentorGrant("recipient", new MentorAmount(2, 3), 7));

        Assert.Equal(2, engine.PendingCount);
        var grants = engine.Take(2);
        Assert.Equal(new[] { 4, 7 }, grants.Select(grant => grant.MasteryCeilingExclusive));
    }

    [Fact]
    public void EquippedSourceSharesOnlyBelowItsOwnMastery()
    {
        var relationship = new MentorRelationshipSnapshot(
            3,
            9,
            new[]
            {
                new MentorRecipe("source", 4, true),
                new MentorRecipe("lower", 3, true),
                new MentorRecipe("equal", 4, true),
                new MentorRecipe("higher", 8, true),
            },
            new[] { new MentorRecipe("lower", 3, true) });
        var captured = new MentorCaptureKey(new object(), "source", 4, true, relationship: relationship);

        var recipients = relationship.ForEquippedCapture(captured);

        Assert.Equal(4, recipients.HighestMastery);
        Assert.Equal("lower", Assert.Single(recipients.Recipients).Uuid);
        Assert.Same(recipients, relationship.ForEquippedCapture(captured));
    }

    [Fact]
    public void ReplenishingEarlyRecipientsCannotStarveLaterRecipients()
    {
        var engine = new MentorEngine();
        var batch = new[]
        {
            new MentorGrant("a", new MentorAmount(1, 1)),
            new MentorGrant("b", new MentorAmount(1, 1)),
            new MentorGrant("c", new MentorAmount(1, 1)),
        };

        engine.Consolidate(batch);
        Assert.Equal("a", Assert.Single(engine.Take(1)).Uuid);

        engine.Consolidate(batch);
        Assert.Equal("b", Assert.Single(engine.Take(1)).Uuid);
        engine.Consolidate(batch);
        Assert.Equal("c", Assert.Single(engine.Take(1)).Uuid);

        Assert.Equal(new[] { "a", "b" }, engine.Take(2).Select(grant => grant.Uuid));
    }

    [Fact]
    public void ArtifactAdapterCompletesNativeContainerLevelAndSaveSequence()
    {
        var artifact = new FakeArtifact();

        MentorRuntime.GrantArtifact(artifact, new global::BigDouble(7.0, 8));

        Assert.Equal(7.0, artifact.Container.Received.Mantissa);
        Assert.Equal(8, artifact.Container.Received.Exponent);
        Assert.Equal(2, artifact.masteryLevel);
        Assert.Equal(3.0, artifact.masteryXp.Mantissa);
        Assert.Equal(4, artifact.masteryXp.Exponent);
    }

    [Fact]
    public void NewProgressionDomainsStartDisabledWithTenPercentShares()
    {
        var config = MentorConfig.Bind(new BepInEx.Configuration.ConfigFile());

        Assert.False(config.ArtifactsEnabled.Value);
        Assert.Equal(10.0, config.ArtifactSharePercent.Value);
        Assert.False(config.AlchemyEnabled.Value);
        Assert.Equal(10.0, config.AlchemySharePercent.Value);
        Assert.Equal(2, config.OperationsPerFrame.Value);
        Assert.Equal(1.0f, config.CpuBudgetMilliseconds.Value);
        Assert.Equal(MentorSpellSourcePolicy.EquippedSpells, config.SpellSourcePolicy.Value);
    }

    [Fact]
    public void ContinuousDomainsDistributeAtMostOncePerWindow()
    {
        long next = 0;

        Assert.True(MentorRuntime.DistributionDue(100, ref next, 25));
        Assert.Equal(125, next);
        Assert.False(MentorRuntime.DistributionDue(124, ref next, 25));
        Assert.True(MentorRuntime.DistributionDue(125, ref next, 25));
        Assert.Equal(150, next);
    }

    [Fact]
    public void MentorSummaryShowsTiesLevelsAndOverflow()
    {
        Assert.Equal("Analyze Nature, Study Magic, Transmute Magic +2 (Lv 92)",
            MentorRuntime.FormatMentorSummary(new[] { "Analyze Nature", "Study Magic", "Transmute Magic", "Fourth", "Fifth" }, 92, 5));
        Assert.Equal("None", MentorRuntime.FormatMentorSummary(Array.Empty<string>(), 0, 0));
    }

    [Fact]
    public void DomainsUseTheirNativeUnlockPredicates()
    {
        Assert.Equal("IsDiscovered", MentorRuntime.AvailabilityMethod(MentorDomain.Spells));
        Assert.Equal("IsCreated", MentorRuntime.AvailabilityMethod(MentorDomain.Artifacts));
        Assert.Equal("IsAvailable", MentorRuntime.AvailabilityMethod(MentorDomain.Alchemy));
    }
}
