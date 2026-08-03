using System;
using System.Threading;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.Mentor.Runtime.ServiceCycle;

public sealed class MentorServiceCompositionTests
{
    private static readonly Guid Source =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Recipient =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void WorldInputIsPlannedOffThreadAndExecutedOnThePumpThread()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = MentorService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            ServiceActionDispatchPolicy.Single);
        registry.WorldPublication.Publish(World(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.Equal(1, actions.Calls);
        Assert.Equal(Recipient, actions.Recipient);
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Mentor = new MentorConfiguration
            {
                Mode = MentorOperationMode.Active,
                EconomyMode = MentorEconomyMode.PerRecipient,
                SpellSourcePolicy = MentorSpellSourcePolicy.EquippedSpells,
                SpellSharePercent = 10,
            },
        };

    private static GameWorldState World() =>
        new()
        {
            CollectedAtEpoch = 1,
            Views = Table(
                new WorldView(KnownEntities.MasteriesEnabled.Uuid, false, false, true),
                new WorldView(KnownEntities.MagicSpellbook.Uuid, false, false, true)),
            SpellRecipes = Table(Spell(Source, 5), Spell(Recipient, 1)),
            SpellSlots = Table(new WorldSpellSlot(
                0, Source, true, false, false, false, false, false, false, true, true,
                true, 1, 1, default)),
            MasteryExperience = Table(new WorldMasteryExperience(
                1, MasteryExperienceDomain.Spell, Source, 5, true, new BigDouble(10))),
        };

    private static WorldSpellRecipe Spell(Guid id, int mastery) =>
        new(id, true, 0, default, mastery, false, false, false, false, 0, 0, 0, false,
            default, default, default, default, default, default, false);

    private static PublicationTable<T> Table<T>(params T[] rows) where T : struct =>
        PublicationTable<T>.Create(rows, rows.Length);

    private sealed class ActionPort : IMentorCycleActionPort
    {
        private readonly int _ownerThread;

        internal ActionPort(int ownerThread) => _ownerThread = ownerThread;
        internal int Calls { get; private set; }
        internal Guid Recipient { get; private set; }

        public ServiceActionResult TryExecute(
            in MentorCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            Calls++;
            Recipient = action.RecipientId;
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
        }
    }
}
