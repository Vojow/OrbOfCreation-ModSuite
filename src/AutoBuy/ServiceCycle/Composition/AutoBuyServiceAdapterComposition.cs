using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// Builds the Auto Buy action adapter and the plain typed service definition from the feature's
/// dependencies. The action adapter is self-contained, so — unlike Auto Harvest — there is no shared
/// binding resolver, gate set, or contract circuit to thread through. There is no capture adapter at
/// all: the runtime pins the world and the worker projects it, so nothing is left for capture to do.
/// </summary>
internal sealed class AutoBuyServiceAdapterComposition
{
    private AutoBuyServiceAdapterComposition(
        IAutomataServiceDefinition<
            AutoBuyCycleState,
            AutoBuyCycleAction> definition,
        AutoBuyCycleActionAdapter actions)
    {
        Definition = definition;
        Actions = actions;
    }

    internal IAutomataServiceDefinition<
        AutoBuyCycleState,
        AutoBuyCycleAction> Definition { get; }
    internal AutoBuyCycleActionAdapter Actions { get; }

    internal static AutoBuyServiceAdapterComposition Create(
        AutoBuyFeatureDependencies dependencies,
        IServiceWorldGenerationSource worldGenerations
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

#if SERVICE_CYCLE_PROFILE
        var profileOperations = new AutomataProfileOperations(
            profileProbe ?? throw new ArgumentNullException(nameof(profileProbe)));
        var actions = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(profileOperations),
            new AutoBuyNativeQueueRoomAdapter(),
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnershipMask,
            profileOperations,
            dependencies.RefusalResponse,
            worldGenerations,
            dependencies.GameMcpOwnership);
#else
        var actions = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(),
            new AutoBuyNativeQueueRoomAdapter(),
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnershipMask,
            dependencies.RefusalResponse,
            worldGenerations);
#endif
        return new AutoBuyServiceAdapterComposition(AutoBuyService.Define(actions), actions);
    }
}
