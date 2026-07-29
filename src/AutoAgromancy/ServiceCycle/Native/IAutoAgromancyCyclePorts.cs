using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal interface IAutoAgromancyCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoAgromancyCycleAction action,
        in SuiteRuntimeConfiguration configuration,
        in ServiceActionContext context);
}

internal interface IAutoAgromancyExactNativeMutator
{
    AutoAgromancyExactMutationResult ApplyExactTarget(
        Guid actionId,
        Guid elementId,
        int expectedCurrentLevel,
        int targetLevel);
}

internal interface IAutoAgromancyLiveWorldReader
{
    bool TryRead(long lifecycleEpoch, out GameWorldState world);
}
