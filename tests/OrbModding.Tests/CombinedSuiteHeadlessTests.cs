using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class CombinedSuiteHeadlessTests
{
    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void SupportedSuiteProfileThresholdsPreserveBacklogWithoutStarvation()
    {
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock());
        var registrations = Enumerable.Range(0, SuitePerformanceWorkIdentities.SupportedSuiteV1Count)
            .Select(SuitePerformanceWorkIdentities.GetSupportedSuiteV1)
            .Select(identity => coordinator.Register(
                identity.Subsystem,
                identity.WorkName,
                identity.BudgetClass,
                identity.ExecutionKind))
            .ToArray();
        try
        {
            long frameIdentity = 0;
            foreach (var registration in registrations)
            {
                registration.SetPending(true);
                Assert.Equal(
                    SuiteWorkAdmission.Granted,
                    coordinator.RequestWork(registration, ++frameIdentity, out var warmup));
                warmup.Complete(registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation
                    ? SuiteWorkCompletion.NativeMutation(1, 1)
                    : new SuiteWorkCompletion(1));
                registration.SetPending(false);
            }

            var mutations = registrations
                .Where(registration => registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
                .ToArray();
            foreach (var registration in registrations) registration.SetPending(false);

            // Keyed by work name so each line below arms the work it names, whatever position that
            // identity holds in the table.
            var byWorkName = registrations
                .Select((registration, index) =>
                    (registration, SuitePerformanceWorkIdentities.GetSupportedSuiteV1(index).WorkName))
                .ToDictionary(item => item.WorkName, item => item.registration, StringComparer.Ordinal);
            byWorkName[SuitePerformanceWorkIdentities.AutoCastEvaluate.WorkName].SetPending(true);
            byWorkName[SuitePerformanceWorkIdentities.MentorEvaluate.WorkName].SetPending(true);
            byWorkName[SuitePerformanceWorkIdentities.ModConfigWork.WorkName].SetPending(true);
            byWorkName[SuitePerformanceWorkIdentities.GameplayInvalidationDelivery.WorkName].SetPending(true);
            var nextMutation = 0;
            mutations[nextMutation].SetPending(true);

            var completions = 0;
            for (var sample = 1L; sample <= 240; sample++)
            {
                var frame = ++frameIdentity;
                for (var offset = 0; offset < registrations.Length; offset++)
                {
                    var registration = registrations[(int)((frame + offset) % registrations.Length)];
                    if (coordinator.RequestWork(registration, frame, out var lease) != SuiteWorkAdmission.Granted)
                        continue;
                    lease.Complete(registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation
                        ? SuiteWorkCompletion.NativeMutation(1, 1)
                        : new SuiteWorkCompletion(1));
                    if (registration.ExecutionKind == SuiteWorkExecutionKind.NonPreemptibleNativeMutation)
                    {
                        registration.SetPending(false);
                        nextMutation++;
                        if (nextMutation < mutations.Length) mutations[nextMutation].SetPending(true);
                    }
                    completions++;
                }
            }

            Assert.True(completions >= 240);
            foreach (var registration in registrations)
            {
                Assert.True(coordinator.TryGetRegistrationSnapshot(registration, out var snapshot));
                Assert.True(snapshot.CompletedWorkItems > 0);
                Assert.Equal(0, snapshot.FailedWorkItems);
                Assert.Equal(0, snapshot.AbandonedWorkItems);
                Assert.True(
                    snapshot.StarvationEvents == 0,
                    $"{snapshot.Subsystem}/{snapshot.WorkName} reported {snapshot.StarvationEvents} starvation event(s); max wait {snapshot.MaximumPendingWaitFrames}, threshold {snapshot.StarvationThresholdFrames}.");
                Assert.True(snapshot.MaximumPendingWaitFrames <= snapshot.StarvationThresholdFrames);
            }
        }
        finally
        {
            foreach (var registration in registrations) registration.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CombinedSuite_AllMutationProducersAllowOnlyOneMutationOwnerLeasePerFrame()
    {
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 128);
        var producers = CreateMutationProducers(coordinator);
        try
        {
            foreach (var producer in producers)
            {
                producer.Registration.SetPending(true);
            }

            for (var frame = 1L; frame <= 21; frame++)
            {
                var grantsThisFrame = 0;
                foreach (var producer in producers)
                {
                    var admission = coordinator.RequestWork(producer.Registration, frame, out var lease);
                    if (admission == SuiteWorkAdmission.Granted)
                    {
                        grantsThisFrame++;
                        producer.Grants++;
                        lease.Complete(SuiteWorkCompletion.NativeMutation(1, 1));
                    }
                    else
                    {
                        Assert.Contains(
                            admission,
                            new[]
                            {
                                SuiteWorkAdmission.WaitingForTurn,
                                SuiteWorkAdmission.NativeMutationAlreadyAdmitted,
                            });
                    }
                }

                Assert.Equal(1, grantsThisFrame);
                Assert.True(coordinator.NativeMutationAdmittedThisFrame);
            }

            Assert.All(producers, producer => Assert.Equal(3, producer.Grants));
        }
        finally
        {
            foreach (var producer in producers)
            {
                producer.Registration.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void CombinedSuite_SustainedMutationBacklogRemainsBoundedAndFair()
    {
        var coordinator = new SuitePerformanceCoordinator(new ZeroClock(), 10.0, 10.0, 256);
        var producers = CreateMutationProducers(coordinator);
        var grantOrder = new List<string>();
        try
        {
            foreach (var producer in producers)
            {
                producer.Registration.SetPending(true);
            }

            for (var frame = 1L; frame <= 700; frame++)
            {
                foreach (var producer in producers)
                {
                    if (coordinator.RequestWork(producer.Registration, frame, out var lease) !=
                        SuiteWorkAdmission.Granted)
                    {
                        continue;
                    }

                    producer.Grants++;
                    grantOrder.Add(producer.Name);
                    lease.Complete(SuiteWorkCompletion.NativeMutation(1, 1));
                }
            }

            Assert.Equal(700, grantOrder.Count);
            Assert.All(producers, producer => Assert.Equal(100, producer.Grants));
            for (var index = 0; index < grantOrder.Count; index++)
            {
                Assert.Equal(producers[index % producers.Length].Name, grantOrder[index]);
            }

            Assert.All(producers, producer =>
            {
                Assert.True(coordinator.TryGetSubsystemSnapshot(producer.Name, out var snapshot));
                Assert.Equal(100, snapshot.NativeMutationsStarted);
                Assert.Equal(100, snapshot.NativeMutationAttempts);
                Assert.Equal(100, snapshot.NativeMutationsCommitted);
                Assert.Equal(100, snapshot.CompletedWorkItems);
            });
        }
        finally
        {
            foreach (var producer in producers)
            {
                producer.Registration.Dispose();
            }
        }
    }

    private static MutationProducer[] CreateMutationProducers(SuitePerformanceCoordinator coordinator)
    {
        return new[]
        {
            Create(coordinator, "OrbAutomata.AutoBuy", "Submit purchase"),
            Create(coordinator, "OrbAutomata.AutoCast", "Fire spell"),
            Create(coordinator, "OrbAutomata.AutoConcept", "Change concept quantity"),
            Create(coordinator, "OrbAutomata.SpellLevel", "Purchase spell level"),
            Create(coordinator, "OrbMentor.Spells", "Grant spell XP"),
            Create(coordinator, "OrbMentor.Artifacts", "Grant artifact XP"),
            Create(coordinator, "OrbMentor.Alchemy", "Grant alchemy XP"),
        };
    }

    private static MutationProducer Create(
        SuitePerformanceCoordinator coordinator,
        string subsystem,
        string work)
    {
        return new MutationProducer(
            subsystem,
            coordinator.Register(
                subsystem,
                work,
                SuiteBudgetClass.HardLimited,
                SuiteWorkExecutionKind.NonPreemptibleNativeMutation));
    }

    private sealed class MutationProducer
    {
        public MutationProducer(string name, SuiteWorkRegistration registration)
        {
            Name = name;
            Registration = registration;
        }

        public string Name { get; }

        public SuiteWorkRegistration Registration { get; }

        public int Grants { get; set; }
    }

    private sealed class ZeroClock : IPerformanceClock
    {
        public long GetTimestamp() => 0;

        public double GetElapsedMilliseconds(long startTimestamp, long endTimestamp) => 0.0;
    }
}
