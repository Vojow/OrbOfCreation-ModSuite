using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoScribeWorker :
    IServiceCycleWorkerDefinition<AutoScribeCycleState, AutoScribeCycleAction>
{
    private readonly AutoScribeIdentityProfile _profile;

    internal AutoScribeWorker(AutoScribeIdentityProfile profile) =>
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public AutoScribeCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoScribeCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoScribeCycleState state) => state = default;

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoScribeCycleState state,
        ServiceActionWriter<AutoScribeCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException(
                "Auto Scribe state belongs to a different lifecycle.");
        state.ObserveConfiguration(
            context.Identity.Config,
            config.AutoScribe,
            _profile.Roles);
        var interval = AutoScribeServiceCycleFeature.Interval(config);
        var plan = ScrollCoveragePlanner.Build(world, _profile);
        ResetProjection(ref state);
        for (var index = 0; index < plan.Roles.Length; index++)
            ObserveRole(plan.Roles[index], ref state);

        if (!AutoScribeServiceCycleFeature.IsOperational(config) ||
            !plan.TryChooseProduction(state.EnabledRoles, out var selected))
        {
            return WakePolicy.AfterDecision(interval);
        }

        actions.Add(new AutoScribeCycleAction(
            selected.RecipeId,
            selected.ScrollId,
            selected.TargetLevel,
            plan.CollectedAtFrame,
            plan.CollectedAtEpoch));
        state.PlannedActions = 1;
        state.TargetLevel = selected.TargetLevel;
        return WakePolicy.AfterDecision(interval);
    }

    public void ProjectState(
        in AutoScribeCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output)
    {
        output.Add(new ServiceProjectionKey(10),
            ServiceProjectionValue.FromInteger(state.DeficientRoles));
        output.Add(new ServiceProjectionKey(11),
            ServiceProjectionValue.FromInteger(state.PlannedActions));
        output.Add(new ServiceProjectionKey(12),
            ServiceProjectionValue.FromInteger(state.TargetLevel));
        output.Add(new ServiceProjectionKey(13),
            ServiceProjectionValue.FromInteger(state.EvidenceUnknownRoles));
        output.Add(new ServiceProjectionKey(14),
            ServiceProjectionValue.FromInteger(state.ExternallyProducingRoles));
        output.Add(new ServiceProjectionKey(15),
            ServiceProjectionValue.FromInteger(state.CoveredRoles));
    }

    private static void ResetProjection(ref AutoScribeCycleState state)
    {
        state.DeficientRoles = 0;
        state.EvidenceUnknownRoles = 0;
        state.ExternallyProducingRoles = 0;
        state.CoveredRoles = 0;
        state.PlannedActions = 0;
        state.TargetLevel = 0;
    }

    private static void ObserveRole(
        ScrollRoleCoverage role,
        ref AutoScribeCycleState state)
    {
        if (role.Deficit > 0) state.DeficientRoles++;
        if (role.State == ScrollCoverageState.EvidenceUnknown)
            state.EvidenceUnknownRoles++;
        else if (role.State == ScrollCoverageState.ExternallyProducing)
            state.ExternallyProducingRoles++;
        else if (role.State == ScrollCoverageState.Covered)
            state.CoveredRoles++;
    }

}
