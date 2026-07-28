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
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

public sealed class AutoConceptServiceCompositionTests
{
    private static readonly Guid Recipe = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Core = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void PublishedWorldReachesTheWorkerAndTheActionReturnsToTheMainThread()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = AutoConceptService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            ServiceActionDispatchPolicy.Bounded(1));
        registry.WorldPublication.Publish(World(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        pump.PumpFrame(1);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromMilliseconds(250)));
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.Equal(1, actions.ExecutionCount);
        Assert.Equal(AutoConceptActionKind.Add, actions.LastKind);
        Assert.Equal(Recipe, actions.LastRecipe);
    }

    private static GameWorldState World()
    {
        var concepts = new WorldConceptRecipeBuffer();
        var concept = new WorldConceptRecipe(Recipe, Core);
        concepts.Append(in concept);
        var recipes = new[]
        {
            new WorldAlchemyRecipe(
                Recipe, true, 0, 0, 0, default, 0, default,
                false, false, false, 0, false,
                default, default, default, default, default, default, default, default,
                default, default, default, default, default, new BigDouble(2), default,
                new BigDouble(1)),
        };
        return new GameWorldState
        {
            AlchemyRecipes = WorldTable.Create(recipes),
            ConceptRecipes = WorldAlchemyRowDeriver.Build(concepts),
            CollectedAtEpoch = 1,
        };
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = AutoConceptOperationMode.Active,
                FallbackEvaluationIntervalSeconds = 30,
                TrainingPeriodSeconds = 60,
            },
        };

    private sealed class ActionPort : IAutoConceptCycleActionPort
    {
        private readonly int _ownerThread;

        internal ActionPort(int ownerThread) => _ownerThread = ownerThread;
        internal int ExecutionCount { get; private set; }
        internal AutoConceptActionKind LastKind { get; private set; }
        internal Guid LastRecipe { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoConceptCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            LastKind = action.Kind;
            LastRecipe = action.RecipeId;
            var call = new NativeMutationCallOutcome(1, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call));
        }
    }
}
