using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoScribeCycleAction
{
    internal AutoScribeCycleAction(
        Guid recipeId,
        Guid scrollId,
        int level,
        long collectedAtFrame,
        long collectedAtEpoch)
    {
        RecipeId = recipeId;
        ScrollId = scrollId;
        Level = level;
        CollectedAtFrame = collectedAtFrame;
        CollectedAtEpoch = collectedAtEpoch;
    }

    internal Guid RecipeId { get; }
    internal Guid ScrollId { get; }
    internal int Level { get; }
    internal long CollectedAtFrame { get; }
    internal long CollectedAtEpoch { get; }
}

internal struct AutoScribeCycleState
{
    private AutoScribeCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        _roleConfiguration = default;
        _enabledRoles = null;
        DeficientRoles = 0;
        EvidenceUnknownRoles = 0;
        ExternallyProducingRoles = 0;
        CoveredRoles = 0;
        PlannedActions = 0;
        TargetLevel = 0;
    }

    private ConfigGeneration _roleConfiguration;
    private PublicationTable<ScrollRoleKey>? _enabledRoles;

    internal LifecycleGeneration Lifecycle { get; }
    internal PublicationTable<ScrollRoleKey>? EnabledRoles => _enabledRoles;
    internal int DeficientRoles;
    internal int EvidenceUnknownRoles;
    internal int ExternallyProducingRoles;
    internal int CoveredRoles;
    internal int PlannedActions;
    internal int TargetLevel;

    internal static AutoScribeCycleState Create(LifecycleGeneration lifecycle) =>
        new(lifecycle);

    internal void ObserveConfiguration(
        ConfigGeneration generation,
        AutoScribeConfiguration configuration,
        PublicationTable<AutoScribeRoleDescriptor> roles)
    {
        if (_roleConfiguration == generation) return;
        _enabledRoles = AutoScribeRoleSelection.ParsePublication(
            configuration.Roles,
            roles);
        _roleConfiguration = generation;
    }
}
