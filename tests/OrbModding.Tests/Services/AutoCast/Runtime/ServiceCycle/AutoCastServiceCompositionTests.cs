using System;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Runtime.ServiceCycle;

/// <summary>
/// End-to-end wiring proof for Auto Cast's ServiceCycle composition: the shared pump drives the
/// typed definition, and everything that touches the game stays on the main thread.
/// </summary>
/// <remarks>
/// This is also the only place the runtime's structural audits run against this service. The
/// worker-storage rule and the worker-definition validator fire when a definition is registered, not
/// when it is compiled, so without a test that registers one they would first be checked inside the
/// game.
/// </remarks>
public sealed class AutoCastServiceCompositionTests
{
    private static readonly Guid Ember = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void CommonPumpExecutesThePlainServiceThroughMainThreadPorts()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = AutoCastService.Define(actions, new AutoCastManualPauseState());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        pump.PumpFrame(1);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        // An empty loadout is nothing to cast, so the boundary is never reached.
        Assert.Equal(0, actions.ExecutionCount);
    }

    /// <summary>
    /// A world published into the registry reaches the worker and becomes a cast, without the service
    /// ever being handed a publisher.
    /// </summary>
    /// <remarks>
    /// The composition pumps to completion against an empty world, so the test above cannot tell a
    /// wired world from an unwired one. This one can: nothing but the registry's publication could
    /// have put a slot in front of the action port.
    /// </remarks>
    [Fact]
    public void AWorldPublishedIntoTheRegistryReachesTheWorker()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = AutoCastService.Define(actions, new AutoCastManualPauseState());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        registry.WorldPublication.Publish(ReadyLoadoutWorld(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        pump.PumpFrame(1);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.True(actions.ExecutionCount > 0);
        Assert.Equal(AutoCastActionKind.Fire, actions.LastKind);
        Assert.Equal(Ember, actions.LastSpellRecipeId);
    }

    /// <summary>One equipped slot the game calls ready, costing nothing.</summary>
    private static GameWorldState ReadyLoadoutWorld()
    {
        var slots = new WorldSpellSlotBuffer();
        var slot = new WorldSpellSlot(
            0, Ember,
            occupied: true,
            casting: false,
            readyingCast: false,
            attuning: false,
            channeled: false,
            toggled: false,
            chargeable: false,
            castReady: true,
            chargeAvailable: true,
            resourcesCovered: true,
            currentCharges: 1,
            maximumCharges: 1,
            cooldownRemaining: default);
        slots.Append(in slot);

        return new GameWorldState
        {
            SpellSlots = WorldSpellSlotDeriver.Build(slots),
            SpellCosts = PublicationTable<WorldSpellCost>.Empty,
        };
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoCast = new AutoCastConfiguration
            {
                Mode = AutoCastOperationMode.Active,
            },
        };

    private sealed class ActionPort : IAutoCastCycleActionPort
    {
        private readonly int _ownerThread;

        public ActionPort(int ownerThread) => _ownerThread = ownerThread;

        public int ExecutionCount { get; private set; }
        public AutoCastActionKind LastKind { get; private set; }
        public Guid LastSpellRecipeId { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoCastCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            LastKind = action.Kind;
            LastSpellRecipeId = action.SpellRecipeId;
            var call = new NativeMutationCallOutcome(1, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call));
        }
    }
}
