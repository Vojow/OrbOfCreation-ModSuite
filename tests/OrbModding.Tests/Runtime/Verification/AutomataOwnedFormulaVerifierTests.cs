using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

public sealed class AutomataOwnedFormulaVerifierTests
{
    private static readonly Guid SpellId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [Fact]
    public void OwnedSpellCostAgreementClassifiesAsPass()
    {
        var result = VerifySpell(ownedAmount: 10, nativeAmount: 10);

        Assert.Contains("PASSED", result, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedSpellCostDivergenceClassifiesAsFailure()
    {
        var result = VerifySpell(ownedAmount: 11, nativeAmount: 10);

        Assert.Contains("FAILED", result, StringComparison.Ordinal);
        Assert.Contains("SpellRecipeSO term=cost", result, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableOwnedSpellOracleClassifiesAsInconclusive()
    {
        var verifier = new AutomataSpellLevelVerifier(typeof(object), typeof(object));
        var session = new DifferentialVerificationSession("Owned spell level cost");
        session.Start();

        var verified = verifier.TryVerifyCost(
            new object(), new GameWorldState(), session.Run, session, out var failure);
        if (verified) session.RecordVerified();
        else session.RecordUnverifiable(failure);
        session.EndTick();

        var result = session.Complete();
        Assert.Contains("INCONCLUSIVE", result, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSED", result, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedFormulaOraclesFailClosedWhenNativeShapesMove()
    {
        Assert.False(new AutomataSpellLevelVerifier(typeof(object), typeof(object)).IsAvailable);
        Assert.False(new AutomataConceptDrainVerifier(typeof(object), typeof(object)).IsAvailable);
    }

    private static string VerifySpell(double ownedAmount, double nativeAmount)
    {
        var resource = new ResourceSO();
        resource.SetGuid(ResourceId);
        var spell = new SpellRecipeSO { uuid = SpellId.ToString() };
        spell.levelCost.costs.Add(new ResourceTuple(resource, new BigDouble(nativeAmount)));
        var published = new WorldSpellRecipe(
            SpellId, true, 0, default, 0, true, true, 1, Guid.Empty,
            false, false, 0, 1, 1, false,
            default, default, default, default, default, default, false);
        var world = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[] { published }),
            MasteryCosts = PublicationTable<WorldMasteryCost>.Create(new[]
            {
                new WorldMasteryCost(
                    SpellId, 0, ResourceId, new BigDouble(ownedAmount), affordable: true),
            }),
        };
        var verifier = new AutomataSpellLevelVerifier(typeof(SpellRecipeSO), typeof(ResourceCostList));
        var session = new DifferentialVerificationSession("Owned spell level cost");
        session.Start();

        var verified = verifier.TryVerifyCost(spell, world, session.Run, session, out var failure);
        if (verified) session.RecordVerified();
        else session.RecordUnverifiable(failure);
        session.EndTick();
        return session.Complete();
    }
}
