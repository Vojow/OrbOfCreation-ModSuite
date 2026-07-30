using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeNativeAdapterTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();
    private readonly AutoScribeIdentityProfile _profile;
    private readonly AutomataFeatureStatusReporter _status;
    private readonly CraftingRecipeSO _recipe;
    private readonly ConsumableSO _scroll;
    private readonly CraftingInstanceListVariable _active;
    private readonly CraftingInstanceListVariable _automatic;
    private readonly Targeting.TargetStructure _targets;
    private long _epoch = 2;
    private bool _owns = true;
    private bool _canConsume = true;
    private bool _capturePermit = true;

    public AutoScribeNativeAdapterTests()
    {
        Assert.True(new AutoScribeIdentityCatalog().TryGetProfile(
            GameAssemblyAudit.WindowsV1052BaselineId,
            out _profile));
        Assert.True(_profile.TryFind(
            new ScrollRoleKey("scribe.advancement"),
            out var role));
        _recipe = Register(
            new CraftingRecipeSO
            {
                visible = true,
                useQuantityAsLevel = true,
                MaximumAffordableLevel = 12,
            },
            role.Recipe!.Value.Uuid);
        _scroll = Register(new ConsumableSO { visible = true }, role.Scroll.Uuid);
        _active = Register(
            new CraftingInstanceListVariable(),
            _profile.ActiveInstances.Uuid);
        _automatic = Register(
            new CraftingInstanceListVariable { isAutoList = true },
            _profile.AutomaticInstances.Uuid);
        _targets = new Targeting.TargetStructure();
        _targets.Candidates.Add(new Target());
        var options = new Targeting.TargetSelectOptions { Targeting = _targets };
        var block = new InstantEffectBlock();
        block.effectScripts.Add(
            new RequestTargetEffectScript { targetOptions = options });
        _scroll.onUseEffects.Add(block);
        _status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(
                    PluginIds.SuiteGuid,
                    AutomataFeatureStatuses.AutoScribeFeatureId),
                "Auto Scribe",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "waiting"),
                lifecycleGeneration: 2));
    }

    public void Dispose() => _status.Dispose();

    [Fact]
    public void OneShotQueueSubmissionCommitsExactlyOneLevelledCraft()
    {
        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        var queued = Assert.Single(_active.value);
        Assert.Same(_recipe, queued.Recipe);
        Assert.Equal(12d, queued.Quantity.ToDouble());
        Assert.True(queued.Initiated);
        Assert.Equal(1, _recipe.PurchaseCalls);
        Assert.Equal(3, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(1, result.NativeCallOutcome.MutationsCommitted);
    }

    [Fact]
    public void SubmissionAdvancesToHighestAffordableLevelBeyondUnlockedFrontier()
    {
        _recipe.MainType.maxStartingLevel = 12;
        _recipe.MaximumAffordableLevel = 17;

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(17d, Assert.Single(_active.value).Quantity.ToDouble());
        Assert.Equal(17, _recipe.MainType.maxStartingLevel);
    }

    [Fact]
    public void SubmissionFallsBackBelowFrontierWhenFrontierIsNotAffordable()
    {
        _recipe.MainType.maxStartingLevel = 12;
        _recipe.MaximumAffordableLevel = 7;

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(7d, Assert.Single(_active.value).Quantity.ToDouble());
        Assert.Equal(12, _recipe.MainType.maxStartingLevel);
    }

    [Fact]
    public void InstantCraftCommitsOnlyWhenSameLevelStockIncreases()
    {
        _recipe.InstantCraftEnabled = true;
        _recipe.InstantOutput = _scroll;

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Empty(_active.value);
        var count = Assert.Single(_scroll.consumableCounts);
        Assert.Equal(12, count.Level);
        Assert.Equal(1, count.Quantity);
    }

    [Fact]
    public void LifecycleReplacementRejectsBeforeAnyNativeCall()
    {
        _epoch++;

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(0, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Empty(_active.value);
        Assert.Equal(0, _recipe.PurchaseCalls);
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("consumption")]
    [InlineData("capture")]
    public void LostRuntimeDependencyRejectsBeforeAnyNativeCall(string dependency)
    {
        if (dependency == "ownership") _owns = false;
        if (dependency == "consumption") _canConsume = false;
        if (dependency == "capture") _capturePermit = false;

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.PolicyRejected, result.Code);
        Assert.Equal(0, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Empty(_active.value);
    }

    [Fact]
    public void FullNativeQueueRejectsWithoutChargingOrMutating()
    {
        _active.Maximum = 0;

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(0, _recipe.PurchaseCalls);
        Assert.Empty(_active.value);
    }

    [Fact]
    public void NewSameLevelSupplyAfterPlanningRejectsDuplicateProduction()
    {
        _scroll.consumableCounts.Add(
            new ConsumableCount { Level = 12, Quantity = 1 });

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(0, _recipe.PurchaseCalls);
        Assert.Empty(_active.value);
    }

    [Fact]
    public void NewAutomaticWorkAfterPlanningRejectsCompetingProduction()
    {
        _automatic.value.Add(new CraftingInstance(_recipe, new BigDouble(12d)));

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(0, _recipe.PurchaseCalls);
        Assert.Empty(_active.value);
    }

    [Fact]
    public void ExpiredAutomaticWorkDoesNotBlockNeededProduction()
    {
        _automatic.value.Add(new CraftingInstance(_recipe, new BigDouble(12d))
        {
            Automatic = true,
            Expired = true,
        });

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(1, _recipe.PurchaseCalls);
        Assert.Single(_active.value);
    }

    [Fact]
    public void TargetDisappearingAfterPlanningRejectsDuplicateProduction()
    {
        _targets.Candidates.Clear();

        var result = Execute();

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(0, _recipe.PurchaseCalls);
        Assert.Empty(_active.value);
    }

    [Fact]
    public void AmbiguousQueuePostconditionQuarantinesUntilLifecycleInvalidation()
    {
        _active.SuppressAdd = true;
        var adapter = Adapter();

        var ambiguous = Execute(adapter);
        var blocked = Execute(adapter);

        Assert.Equal(ServiceActionDisposition.Faulted, ambiguous.Disposition);
        Assert.Equal(
            NativeMutationOutcome.PostconditionFailed,
            ambiguous.NativeEvidence.Outcome);
        Assert.Equal(1, ambiguous.NativeCallOutcome.MutationAttempts);
        Assert.Equal(0, ambiguous.NativeCallOutcome.MutationsCommitted);
        Assert.Equal(ServiceActionDisposition.Rejected, blocked.Disposition);
        Assert.True(adapter.IsQuarantined);

        _active.SuppressAdd = false;
        adapter.InvalidateLifecycle();
        var recovered = Execute(adapter);

        Assert.Equal(ServiceActionDisposition.Committed, recovered.Disposition);
        Assert.False(adapter.IsQuarantined);
        Assert.Single(_active.value);
    }

    private AutoScribeNativeAdapter Adapter()
    {
        IDictionary dictionary = _registry;
        var resolver = new TypedRegistryResolver(
            () => _epoch,
            () => TypedRegistrySourceSnapshot.Ready(dictionary),
            value => value is IdScriptableObject entity
                ? entity.GetGuid()
                : null);
        return new AutoScribeNativeAdapter(
            new AutoScribeFeatureDependencies(
                resolver,
                _profile,
                () => _epoch,
                () => _owns,
                () => _canConsume,
                () => _capturePermit,
                _status));
    }

    private ServiceActionResult Execute()
    {
        var adapter = Adapter();
        return Execute(adapter);
    }

    private ServiceActionResult Execute(AutoScribeNativeAdapter adapter)
    {
        Assert.True(_profile.TryFind(
            new ScrollRoleKey("scribe.advancement"),
            out var role));
        var action = new AutoScribeCycleAction(
            role.Recipe!.Value.Uuid,
            role.Scroll.Uuid,
            level: 12,
            collectedAtFrame: 8,
            collectedAtEpoch: 2);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
            },
        };
        var context = new ServiceActionContext(
            new ServiceCycleIdentity(
                AutoScribeServiceCycleFeature.ServiceId,
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                StrategyGeneration.Initial,
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(1),
            actionIndex: 0,
            new MonotonicTimestamp(1));
        return adapter.TryExecute(in action, in config, in context);
    }

    private T Register<T>(T value, Guid id) where T : IdScriptableObject
    {
        value.SetGuid(id);
        _registry.Add(id, value);
        return value;
    }

    private sealed class Target : Targeting.ITargetable
    {
    }
}
