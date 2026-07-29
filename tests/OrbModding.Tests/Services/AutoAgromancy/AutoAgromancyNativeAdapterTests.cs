using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoAgromancy;

public sealed class AutoAgromancyNativeAdapterTests
{
    [Fact]
    public void NewSelectionUsesExactHighestSustainableNativeLevel()
    {
        var fixture = new Fixture(baseRate: 3.0, maximumLevel: 5);

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Applied, result.Disposition);
        Assert.Equal(3, result.TargetLevel);
        Assert.Equal(3, fixture.ActiveLevel);
        Assert.Equal(0, fixture.Action.equipSound.PlayCalls);
    }

    [Fact]
    public void AddSideUiRowUsesExactHighestSustainableNativeLevel()
    {
        var fixture = new Fixture(baseRate: 3.0, maximumLevel: 5);
        var row = new global::UIHarvestAction
        {
            actionListVariable = fixture.List,
            item = fixture.Selected,
        };

        var result = fixture.Adapter.Balance(row, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Applied, result.Disposition);
        Assert.Equal(3, result.TargetLevel);
        Assert.Equal(3, fixture.ActiveLevel);
        Assert.Equal(0, row.FlashCalls);
    }

    [Fact]
    public void RebalanceReplacesCurrentContributionInsteadOfDoubleCountingIt()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5);
        fixture.List.AddInstance(fixture.Selected, 2);
        Assert.Equal(3.0, fixture.Resource.trueRate.Mantissa);

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Applied, result.Disposition);
        Assert.Equal(2, result.PreviousLevel);
        Assert.Equal(5, result.TargetLevel);
        Assert.Equal(5, fixture.ActiveLevel);
    }

    [Fact]
    public void NativeQualityConversionParticipatesInAdmission()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5);
        fixture.Resource.qualitySpendMultiplier = 2.0;

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(2, result.TargetLevel);
        Assert.Equal(2, fixture.ActiveLevel);
    }

    [Fact]
    public void UnsustainableLevelOneDoesNotAddTheAction()
    {
        var fixture = new Fixture(baseRate: 0.5, maximumLevel: 5);

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Rejected, result.Disposition);
        Assert.Equal(0, fixture.ActiveLevel);
        Assert.Empty(fixture.List.value);
    }

    [Fact]
    public void InvalidNativeRateRejectsInsteadOfTreatingItAsZero()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5);
        fixture.Resource.trueRate = new global::BigDouble(double.NaN, 0);

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Rejected, result.Disposition);
        Assert.Empty(fixture.List.value);
    }

    [Fact]
    public void FullNativeActionListRejectsWithoutMutation()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5);
        fixture.List.capacity = 0;

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Rejected, result.Disposition);
        Assert.Contains("slot", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.List.value);
    }

    [Fact]
    public void RemoveSideListIsNotIntercepted()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5);
        fixture.Ui.actionListVariable = null;

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.NotApplicable, result.Disposition);
        Assert.Empty(fixture.List.value);
    }

    [Fact]
    public void LostActionFamilyPermitRejectsImmediatelyBeforeMutation()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5, permit: false);

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Rejected, result.Disposition);
        Assert.Empty(fixture.List.value);
    }

    [Fact]
    public void HarmfulEffectFeedbackRollsBackNewSelection()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5);
        fixture.Action.AfterLevelChanged = level =>
        {
            if (level > 0) fixture.Resource.trueRate = new global::BigDouble(-1, 0);
        };

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(
            AutoAgromancyBalanceDisposition.MutationUnverified,
            result.Disposition);
        Assert.Empty(fixture.List.value);
    }

    [Fact]
    public void LifecycleChangeBetweenCaptureAndMutationRejects()
    {
        var lifecycle = 1L;
        var reads = 0;
        var fixture = new Fixture(
            baseRate: 5.0,
            maximumLevel: 5,
            readLifecycle: () => ++reads < 2 ? lifecycle : lifecycle + 1);

        var result = fixture.Adapter.Balance(fixture.Ui, fixture.Selected);

        Assert.Equal(AutoAgromancyBalanceDisposition.Rejected, result.Disposition);
        Assert.Empty(fixture.List.value);
    }

    [Fact]
    public void AutoHarvestRebalanceCoversEveryActiveActionAndElementPair()
    {
        var fixture = new Fixture(baseRate: 10.0, maximumLevel: 5);
        fixture.List.AddInstance(fixture.Selected, 1);
        var secondAction = new global::HarvestActionSO();
        var secondElement = new global::HarvestElementSO { masteryLevel = 4 };
        secondElement.actionReference.actionCost.costs.Add(
            new global::ResourceTuple(
                fixture.Resource,
                new global::BigDouble(1, 0)));
        var second = new global::HarvestActionInstance(secondElement, secondAction);
        fixture.List.AddInstance(second, 1);

        var results = fixture.Adapter.BalanceActive();

        Assert.Equal(2, results.Count);
        Assert.All(
            results,
            result => Assert.Equal(
                AutoAgromancyBalanceDisposition.Applied,
                result.Disposition));
        Assert.Equal(5, fixture.List.FindInstance(fixture.Selected)!.instances);
        Assert.Equal(5, fixture.List.FindInstance(second)!.instances);
    }

    [Fact]
    public void ExactZeroTargetPreservesTheAuthoritativePairMembership()
    {
        var fixture = new Fixture(baseRate: 5.0, maximumLevel: 5);
        fixture.List.AddInstance(fixture.Selected, 2);

        var result = fixture.Adapter.ApplyExactTarget(
            fixture.Action.GetGuid(),
            fixture.Element.GetGuid(),
            expectedCurrentLevel: 2,
            targetLevel: 0);

        Assert.Equal(
            AutoAgromancyExactMutationDisposition.Committed,
            result.Disposition);
        var retained = Assert.Single(fixture.List.value);
        Assert.Same(fixture.Action, retained.GetAction());
        Assert.Same(fixture.Element, retained.GetElement());
        Assert.Equal(0, retained.instances);
    }

    private sealed class Fixture
    {
        internal Fixture(
            double baseRate,
            int maximumLevel,
            bool permit = true,
            Func<long>? readLifecycle = null)
        {
            Resource = new global::ResourceSO
            {
                name = "Mana",
                baseRate = new global::BigDouble(baseRate, 0),
                trueRate = new global::BigDouble(baseRate, 0),
            };
            Action = new global::HarvestActionSO();
            Element = new global::HarvestElementSO
            {
                masteryLevel = maximumLevel - 1,
            };
            Element.actionReference.actionCost.costs.Add(
                new global::ResourceTuple(Resource, new global::BigDouble(1, 0)));
            Selected = new global::HarvestActionInstance(Element, Action);
            List = new global::HarvestActionInstanceListVariable();
            Ui = new global::UIHarvestActionList { actionListVariable = List };
            var registry = new Hashtable
            {
                [Guid.Parse(AutoAgromancyNativeAdapter.ActiveHarvestActionsId)] = List,
            };
            var resolver = new TypedRegistryResolver(
                () => 1,
                () => TypedRegistrySourceSnapshot.Ready(registry),
                value => value is global::IdScriptableObject identified
                    ? identified.GetGuid()
                    : null);
            Adapter = new AutoAgromancyNativeAdapter(
                readLifecycle ?? (() => 1),
                () => permit,
                resolver);
            Assert.True(Adapter.ContractAvailable, Adapter.ContractFailure);
        }

        internal global::ResourceSO Resource { get; }
        internal global::HarvestActionSO Action { get; }
        internal global::HarvestElementSO Element { get; }
        internal global::HarvestActionInstance Selected { get; }
        internal global::HarvestActionInstanceListVariable List { get; }
        internal global::UIHarvestActionList Ui { get; }
        internal AutoAgromancyNativeAdapter Adapter { get; }
        internal int ActiveLevel => List.FindInstance(Selected)?.instances ?? 0;
    }
}
