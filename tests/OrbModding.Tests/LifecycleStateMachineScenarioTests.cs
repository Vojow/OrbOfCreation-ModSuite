using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Scenarios;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

public sealed class LifecycleStateMachineScenarioTests
{
    private const string JourneyUpgrade = "11111111-1111-4111-8111-111111111111";
    private const string ResetCandidate = "22222222-2222-4222-8222-222222222222";
    private const string ReenableCandidate = "33333333-3333-4333-8333-333333333333";
    private const string SceneCandidate = "44444444-4444-4444-8444-444444444444";
    private const string MixedStructure = "55555555-5555-4555-8555-555555555555";
    private const string MentorOnlyRecipient = "66666666-6666-4666-8666-666666666666";
    private const string MixedRecipientA = "77777777-7777-4777-8777-777777777777";
    private const string MixedRecipientB = "88888888-8888-4888-8888-888888888888";

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompleteJourney_NoSaveThroughReset_IsGenerationIsolated()
    {
        using var kernel = new LifecycleScenarioKernel();
        var autoBuy = kernel.AddFeature(new ScenarioAutoBuyFeature(
            kernel,
            new[]
            {
                new SimulatedCandidateSpec(
                    JourneyUpgrade,
                    AutoBuyCandidateKind.Upgrade,
                    baseCost: 10.0,
                    available: false,
                    maximumLevel: 1),
            },
            queueCapacity: 3,
            initialResourceQuantity: 100.0));

        Assert.Equal(GameLifecycleState.NoGame, kernel.Lifecycle.Current.State);
        Assert.Equal(0, kernel.Lifecycle.Current.Generation);

        EnterLoadedGameplay(kernel);
        kernel.Step(2, secondsPerFrame: 1.0);
        Assert.Equal(0, autoBuy.World.TotalSubmitted);

        autoBuy.SetAvailable(JourneyUpgrade, available: true);
        RunUntil(kernel, () => autoBuy.World.TotalSubmitted == 1);

        var purchased = autoBuy.Candidate(JourneyUpgrade);
        Assert.Equal(1, purchased.QueuedLevels);
        Assert.Equal(1, autoBuy.World.QueueCount);
        Assert.True(Assert.Single(kernel.Mutations).Frame > 0);

        kernel.Schedule(
            "current-generation native completion",
            1,
            () => Assert.Equal(1, autoBuy.CompleteOne()));
        kernel.Step();
        ScenarioOracles.ExecutedCallback(kernel, "current-generation native completion");
        Assert.Equal(1, purchased.CurrentLevel);
        Assert.Equal(0, purchased.QueuedLevels);
        Assert.Equal(0, autoBuy.World.QueueCount);

        var generationBeforeReset = kernel.Lifecycle.Current.Generation;
        var staleInvalidationAccepted = true;
        kernel.Schedule("late old-generation native completion", 2, autoBuy.NotifyNativeCompletion);
        kernel.ScheduleUnfiltered(
            "late old-generation invalidation delivery",
            2,
            () => staleInvalidationAccepted = kernel.TryPublishForGeneration(
                generationBeforeReset,
                GameplayInvalidationKind.Queue,
                GameplayInvalidationDomains.AutomataUpgrades,
                JourneyUpgrade,
                "UpgradeSO",
                out _));
        kernel.Observe(GameLifecycleTransitionKind.ResetStarted);
        kernel.Observe(GameLifecycleTransitionKind.ResetCompleted);
        kernel.Step();

        var ignored = ScenarioOracles.IgnoredCallback(kernel, "late old-generation native completion");
        var delivered = ScenarioOracles.DeliveredCallback(
            kernel,
            "late old-generation invalidation delivery");
        Assert.NotEqual(delivered.ScheduledGeneration, delivered.CurrentGeneration);
        Assert.False(staleInvalidationAccepted);
        Assert.Equal(generationBeforeReset, ignored.ScheduledGeneration);
        Assert.False(kernel.TryPublishForGeneration(
            generationBeforeReset,
            GameplayInvalidationKind.Queue,
            GameplayInvalidationDomains.AutomataStructures,
            JourneyUpgrade,
            "UpgradeSO",
            out var staleReason));
        Assert.Contains("stale", staleReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(kernel.Invalidations.GetSnapshot().StaleDiscarded > 0);

        ScenarioOracles.LifecycleKindsInOrder(
            kernel,
            GameLifecycleTransitionKind.SceneEntered,
            GameLifecycleTransitionKind.SaveLoadStarted,
            GameLifecycleTransitionKind.SaveLoaded,
            GameLifecycleTransitionKind.RegistryRebuilt,
            GameLifecycleTransitionKind.RuntimeReady,
            GameLifecycleTransitionKind.ResetStarted,
            GameLifecycleTransitionKind.ResetCompleted);
        ScenarioOracles.LifecycleStatesInOrder(
            kernel,
            GameLifecycleState.Initializing,
            GameLifecycleState.Resetting,
            GameLifecycleState.Initializing,
            GameLifecycleState.Initializing,
            GameLifecycleState.Playing,
            GameLifecycleState.Resetting,
            GameLifecycleState.Playing);
        ScenarioOracles.OneMutationOwnerPerFrame(kernel);
        ScenarioOracles.MutationRequestsAreUnique(kernel);
        ScenarioOracles.NoLifecycleDispatchFailures(kernel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void PreparedAutoBuyWork_IsDiscardedAtResetBoundary()
    {
        using var kernel = new LifecycleScenarioKernel();
        var autoBuy = kernel.AddFeature(AvailableAutoBuy(kernel, ResetCandidate));
        EnterLoadedGameplay(kernel);

        kernel.BlockNativeMutationOnNextStep();
        kernel.Step(secondsPerFrame: 1.0);

        var staleCandidate = autoBuy.Candidate(ResetCandidate);
        Assert.Equal(0, autoBuy.World.TotalSubmitted);
        Assert.True(autoBuy.Catalog.CompletedCandidateEvaluations > 0);

        kernel.Observe(GameLifecycleTransitionKind.ResetStarted);
        kernel.Observe(GameLifecycleTransitionKind.ResetCompleted);
        var currentCandidate = autoBuy.Candidate(ResetCandidate);
        Assert.NotSame(staleCandidate, currentCandidate);

        RunUntil(kernel, () => autoBuy.World.TotalSubmitted == 1);

        Assert.Equal(0, staleCandidate.PurchaseCalls);
        Assert.Equal(1, currentCandidate.PurchaseCalls);
        Assert.Equal(ResetCandidate, Assert.Single(autoBuy.World.SubmissionOrder));
        Assert.Equal(1, autoBuy.World.QueueCount);

        kernel.Observe(GameLifecycleTransitionKind.ResetStarted);

        Assert.Equal(0, autoBuy.World.QueueCount);
        Assert.Equal(0, currentCandidate.QueuedLevels);
        ScenarioOracles.NoLifecycleDispatchFailures(kernel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void DisableAndReenable_ExecutesFreshWorkOnly()
    {
        using var kernel = new LifecycleScenarioKernel();
        var autoBuy = kernel.AddFeature(AvailableAutoBuy(kernel, ReenableCandidate));
        EnterLoadedGameplay(kernel);

        kernel.BlockNativeMutationOnNextStep();
        kernel.Step(secondsPerFrame: 1.0);
        var preparedCandidate = autoBuy.Candidate(ReenableCandidate);
        var nativeAdmissionChecksBeforeDisable = preparedCandidate.CanPurchaseCalls;
        Assert.Equal(0, preparedCandidate.PurchaseCalls);
        Assert.True(autoBuy.Catalog.CompletedCandidateEvaluations > 0);

        autoBuy.SetEnabled(false);
        Assert.Equal(0, autoBuy.World.TotalSubmitted);
        autoBuy.SetEnabled(true);
        RunUntil(kernel, () => autoBuy.World.TotalSubmitted == 1);

        Assert.True(preparedCandidate.CanPurchaseCalls > nativeAdmissionChecksBeforeDisable);
        Assert.Equal(1, preparedCandidate.PurchaseCalls);
        Assert.Equal(1, kernel.Mutations.Count(mutation => mutation.Feature == autoBuy.Name));
        ScenarioOracles.NoLifecycleDispatchFailures(kernel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void SameNameSceneRecreation_RejectsWorkPreparedForOldSceneIdentity()
    {
        using var kernel = new LifecycleScenarioKernel();
        var autoBuy = kernel.AddFeature(AvailableAutoBuy(kernel, SceneCandidate));
        EnterLoadedGameplay(kernel);

        kernel.BlockNativeMutationOnNextStep();
        kernel.Step(secondsPerFrame: 1.0);
        var oldSceneIdentity = kernel.SceneIdentity;
        var oldGeneration = kernel.Lifecycle.Current.Generation;
        var oldCandidate = autoBuy.Candidate(SceneCandidate);
        Assert.Equal(0, oldCandidate.PurchaseCalls);

        var oldSceneInvalidationAccepted = true;
        kernel.ScheduleUnfiltered(
            "old-scene queue delivery",
            2,
            () => oldSceneInvalidationAccepted = kernel.TryPublishForGeneration(
                oldGeneration,
                GameplayInvalidationKind.Queue,
                GameplayInvalidationDomains.AutomataStructures,
                SceneCandidate,
                "StructureSO",
                out _));

        var newSceneIdentity = kernel.RecreateSceneWithSameName("Main");
        kernel.Observe(GameLifecycleTransitionKind.RuntimeReady, "Main");
        kernel.Step();
        var newCandidate = autoBuy.Candidate(SceneCandidate);
        Assert.NotSame(oldSceneIdentity, newSceneIdentity);
        Assert.NotSame(oldCandidate, newCandidate);
        var delivered = ScenarioOracles.DeliveredCallback(kernel, "old-scene queue delivery");
        Assert.NotEqual(delivered.ScheduledGeneration, delivered.CurrentGeneration);
        Assert.False(oldSceneInvalidationAccepted);

        RunUntil(kernel, () => autoBuy.World.TotalSubmitted == 1);

        Assert.Equal(0, oldCandidate.PurchaseCalls);
        Assert.Equal(1, newCandidate.PurchaseCalls);
        Assert.Equal(GameLifecycleState.Playing, kernel.Lifecycle.Current.State);
        Assert.Equal("Main", kernel.Lifecycle.Current.SceneName);
        ScenarioOracles.NoLifecycleDispatchFailures(kernel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void MixedSuite_FeaturesRemainIsolatedAndMutationsStayUnique()
    {
        using var kernel = new LifecycleScenarioKernel();
        var autoBuy = kernel.AddFeature(AvailableAutoBuy(kernel, MixedStructure, queueCapacity: 8));
        var mentor = kernel.AddFeature(new ScenarioMentorFeature(kernel));
        EnterLoadedGameplay(kernel);
        mentor.SetUnlocked(true);

        autoBuy.SetEnabled(false);
        var disabledAutoBuySubmissions = autoBuy.World.TotalSubmitted;
        mentor.QueueGrant(MentorOnlyRecipient);
        RunUntil(kernel, () => mentor.NativeGrantCalls == 1);
        Assert.Equal(disabledAutoBuySubmissions, autoBuy.World.TotalSubmitted);
        Assert.Equal(1, mentor.NativeGrantCalls);

        mentor.SetUnlocked(false);
        autoBuy.SetEnabled(true);
        RunUntil(kernel, () => autoBuy.World.TotalSubmitted > disabledAutoBuySubmissions);
        var submissionsWhileMentorLocked = autoBuy.World.TotalSubmitted;
        Assert.Equal(1, mentor.NativeGrantCalls);

        mentor.SetUnlocked(true);
        mentor.QueueGrant(MixedRecipientA);
        mentor.QueueGrant(MixedRecipientB);
        RunUntil(kernel, () => mentor.NativeGrantCalls == 3, maximumFrames: 40);

        Assert.True(autoBuy.World.TotalSubmitted >= submissionsWhileMentorLocked);
        Assert.Equal(3, mentor.NativeGrantCalls);
        Assert.Contains(kernel.Mutations, mutation => mutation.Feature == autoBuy.Name);
        Assert.Contains(kernel.Mutations, mutation => mutation.Feature == mentor.Name);
        Assert.Contains(
            kernel.InvalidationTrace,
            invalidation => invalidation.Domain == GameplayInvalidationDomains.MentorSpells);
        ScenarioOracles.OnlyFeaturesMutated(kernel, autoBuy.Name, mentor.Name);
        ScenarioOracles.OneMutationOwnerPerFrame(kernel);
        ScenarioOracles.MutationRequestsAreUnique(kernel);
        ScenarioOracles.NoLifecycleDispatchFailures(kernel);
    }

    private static ScenarioAutoBuyFeature AvailableAutoBuy(
        LifecycleScenarioKernel kernel,
        string uuid,
        int queueCapacity = 2) =>
        new(
            kernel,
            new[]
            {
                new SimulatedCandidateSpec(
                    uuid,
                    AutoBuyCandidateKind.Structure,
                    baseCost: 1.0,
                    available: true),
            },
            queueCapacity,
            initialResourceQuantity: 100.0);

    private static void EnterLoadedGameplay(LifecycleScenarioKernel kernel)
    {
        kernel.EnterScene("Main");
        kernel.Observe(GameLifecycleTransitionKind.SaveLoadStarted, "Main");
        kernel.Observe(GameLifecycleTransitionKind.SaveLoaded, "Main");
        kernel.Observe(GameLifecycleTransitionKind.RegistryRebuilt, "Main", new object());
        kernel.Observe(GameLifecycleTransitionKind.RuntimeReady, "Main");
    }

    private static void RunUntil(
        LifecycleScenarioKernel kernel,
        Func<bool> condition,
        int maximumFrames = 20)
    {
        for (var frame = 0; frame < maximumFrames && !condition(); frame++)
            kernel.Step(secondsPerFrame: 1.0);
        Assert.True(condition(), $"Scenario condition was not reached after {maximumFrames} frames.");
    }
}
