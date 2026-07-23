using System;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestServiceAdapterComposition
{
    private AutoHarvestServiceAdapterComposition(
        AutoHarvestBindingResolver bindings,
        AutoHarvestNativeGateSet gates,
        IAutomataReplayServiceDefinition<
            AutoHarvestCycleFrame,
            AutoHarvestCycleState,
            AutoHarvestCycleAction,
            AutoHarvestCycleInputRecord,
            AutoHarvestStateRecord,
            AutoHarvestActionRecord> definition
#if SERVICE_CYCLE_PROFILE
        , AutoHarvestProfileOperations profileOperations
#endif
        )
    {
        Bindings = bindings;
        Gates = gates;
        Definition = definition;
#if SERVICE_CYCLE_PROFILE
        ProfileOperations = profileOperations;
#endif
    }

    internal AutoHarvestBindingResolver Bindings { get; }
    internal AutoHarvestNativeGateSet Gates { get; }
    internal IAutomataReplayServiceDefinition<
        AutoHarvestCycleFrame,
        AutoHarvestCycleState,
        AutoHarvestCycleAction,
        AutoHarvestCycleInputRecord,
        AutoHarvestStateRecord,
        AutoHarvestActionRecord> Definition { get; }
#if SERVICE_CYCLE_PROFILE
    internal AutoHarvestProfileOperations ProfileOperations { get; }
#endif

    internal static AutoHarvestServiceAdapterComposition Create(
        AutoHarvestServiceCycleDependencies dependencies
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var contractCircuit = new AutoHarvestContractCircuit();
#if SERVICE_CYCLE_PROFILE
        var profileOperations = new AutoHarvestProfileOperations(
            profileProbe ?? throw new ArgumentNullException(nameof(profileProbe)));
        var auditor = new AutoHarvestStaticContractAuditor(profileOperations);
        var bindings = new AutoHarvestBindingResolver(
            dependencies.RegistryResolver,
            auditor,
            contractCircuit,
            profileOperations);
        var reader = new AutoHarvestNativeStateReader(profileOperations);
#else
        var auditor = new AutoHarvestStaticContractAuditor();
        var bindings = new AutoHarvestBindingResolver(
            dependencies.RegistryResolver,
            auditor,
            contractCircuit);
        var reader = new AutoHarvestNativeStateReader();
#endif
        var gates = new AutoHarvestNativeGateSet();
        var capture = new AutoHarvestCycleCaptureAdapter(
            bindings,
            reader,
            gates,
            contractCircuit,
            dependencies.OwnsActionFamily
#if SERVICE_CYCLE_PROFILE
            , profileOperations,
            bindings
#endif
            );
        var actions = new AutoHarvestCycleActionAdapter(
            bindings,
            new AutoHarvestMutationAdapter(
                reader
#if SERVICE_CYCLE_PROFILE
                , profileOperations
#endif
                ),
            gates,
            dependencies.OwnsActionFamily,
            dependencies.TryCaptureMutationPermit);
        return new AutoHarvestServiceAdapterComposition(
            bindings,
            gates,
            AutoHarvestService.Define(capture, actions)
#if SERVICE_CYCLE_PROFILE
            , profileOperations
#endif
            );
    }
}
