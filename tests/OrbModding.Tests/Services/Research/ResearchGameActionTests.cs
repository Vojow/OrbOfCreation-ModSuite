using System;
using System.Collections;
using System.Threading.Tasks;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.Research;

public sealed class ResearchGameActionTests : IDisposable
{
    private const long Epoch = 91;
    private readonly IDictionary _registry = new Hashtable();

    public ResearchGameActionTests()
    {
        SettingsManager.ResearchQueueMode = false;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
    }

    public void Dispose()
    {
        SettingsManager.ResearchQueueMode = false;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
    }

    [Fact]
    public void Develop_immediate_uses_purchase_level_and_verifies_active_development()
    {
        var target = Research();
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.True(result.Verified, result.Reason);
        Assert.True(target.isDeveloping);
        Assert.True(target.isActive);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void Queue_mode_replays_the_native_cumulative_cost_loop_and_queues_only_affordable_levels()
    {
        SettingsManager.ResearchQueueMode = true;
        GlobalVariables.MultiBuy = new IntVariable { Value = 3 };
        var resource = new ResourceSO { quantity = new BigDouble(15) };
        var target = Research(maxLevel: 5);
        target.researchCost.costs.Add(new ResourceTuple(resource, new BigDouble(10)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, target.queuedLevels);
        Assert.True(target.isDeveloping);
    }

    [Fact]
    public void Pause_resume_and_cancel_verify_only_the_requested_target_state()
    {
        var target = Research();
        target.isDeveloping = true;
        target.isActive = true;
        target.queuedLevels = 2;
        Register(target);
        using var boundary = Boundary();

        var paused = Submit(boundary, target, ResearchActionKind.Pause);
        var resumed = Submit(boundary, target, ResearchActionKind.Resume);
        var cancelled = Submit(boundary, target, ResearchActionKind.Cancel);

        Assert.True(paused.Verified, paused.Reason);
        Assert.True(resumed.Verified, resumed.Reason);
        Assert.True(cancelled.Verified, cancelled.Reason);
        Assert.False(target.isDeveloping);
        Assert.False(target.isActive);
        Assert.Equal(0, target.queuedLevels);
    }

    [Fact]
    public void Bonus_uses_free_research_type_capacity_and_verifies_self_bonus_identity()
    {
        var type = new ResearchTypeSO { FreeBonusLevels = 2 };
        type.SetGuid(Guid.NewGuid());
        var target = Research();
        target.researchTypes.Add(type);
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, ResearchActionKind.Bonus);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, target.selfBonusLevels);
        Assert.Equal(1, type.UsedBonusLevels);
    }

    [Fact]
    public void Native_gates_refuse_before_the_mutation_permit()
    {
        var target = Research();
        target.researchCost.affordable = false;
        Register(target);
        var permits = 0;
        using var boundary = Boundary(permit: () => { permits++; return true; });

        var result = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.Equal(ResearchPreflight.DevelopUnavailable, result.Preflight);
        Assert.Equal(0, permits);
        Assert.False(target.isDeveloping);
    }

    [Fact]
    public void Ui_mode_and_state_gates_are_preserved_for_pause_and_bonus()
    {
        SettingsManager.ResearchQueueMode = true;
        var target = Research();
        target.isDeveloping = true;
        target.isActive = true;
        var type = new ResearchTypeSO { FreeBonusLevels = 1 };
        type.SetGuid(Guid.NewGuid());
        target.researchTypes.Add(type);
        Register(target);
        using var boundary = Boundary();

        var pause = Submit(boundary, target, ResearchActionKind.Pause);
        var bonus = Submit(boundary, target, ResearchActionKind.Bonus);

        Assert.Equal(ResearchPreflight.InvalidMode, pause.Preflight);
        Assert.Equal(ResearchPreflight.InvalidState, bonus.Preflight);
    }

    [Fact]
    public void Missing_outcome_revalidates_but_throw_after_requested_outcome_commits()
    {
        var target = Research();
        Register(target);
        target.SuppressAction = true;
        using var boundary = Boundary();

        var failed = Submit(boundary, target, ResearchActionKind.Develop);
        var retry = Submit(boundary, target, ResearchActionKind.Develop);
        target.SuppressAction = false;
        boundary.InvalidateLifecycle();
        target.ThrowAfterAction = true;
        var committed = Submit(boundary, target, ResearchActionKind.Develop);

        Assert.Equal(ResearchPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(ResearchPreflight.VerificationFailed, retry.Preflight);
        Assert.True(committed.Verified, committed.Reason);
    }

    [Fact]
    public async Task Unity_thread_is_revalidated_before_identity_or_native_state()
    {
        var target = Research();
        Register(target);
        using var boundary = Boundary();

        var result = await Task.Run(() => Submit(boundary, target, ResearchActionKind.Develop));

        Assert.Equal(ResearchPreflight.WrongThread, result.Preflight);
        Assert.False(target.isDeveloping);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in ResearchNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private ResearchGameAction Boundary(Func<bool>? permit = null,
        Func<string, bool>? includeContract = null)
    {
        static long ReadEpoch() => Epoch;
        var resolver = new TypedRegistryResolver(ReadEpoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is ResearchSO research ? research.GetGuid() : null);
        return new ResearchGameAction(ReadEpoch, permit ?? (() => true),
            static () => "ResearchLifecycle ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static ResearchSubmission Submit(ResearchGameAction boundary,
        ResearchSO target, ResearchActionKind kind)
    {
        var action = new ResearchAction(kind, target.GetGuid(), Epoch);
        return boundary.Submit(in action);
    }

    private void Register(ResearchSO target) => _registry.Add(target.GetGuid(), target);

    private static ResearchSO Research(int maxLevel = 4) => new()
    {
        uuid = Guid.NewGuid().ToString("D"),
        maxLevel = maxLevel,
        available = true,
    };
}
