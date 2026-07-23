using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataServiceDefinitionTests
{
    [Fact]
    public void DefinitionIsReplayFreeAndRegistryStillAuditsWorkerSeparation()
    {
        var metadata = Metadata();
        var definition = AutomataService.Define<Frame, State, Action>(
            in metadata,
            createFrame: () => default,
            createWorker: () => new WorkerDefinition(),
            shouldStart: (
                in AutomataConfiguration _,
                in ServiceCycleStartContext _) =>
                ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready),
            capture: (
                ref Frame _,
                in AutomataConfiguration _,
                in ServiceCaptureContext _) =>
                ServiceCaptureResult.Captured(
                    new StrategyGeneration(1),
                    CommonServiceDecisionCodes.Captured),
            execute: (
                in Action _,
                in AutomataConfiguration _,
                in ServiceActionContext _) =>
                ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected));

        Assert.IsAssignableFrom<IAutomataServiceDefinition<Frame, State, Action>>(definition);
        Assert.DoesNotContain(
            definition.GetType().GetInterfaces(),
            contract => contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(IServiceCycleReplayDefinition<,,,,,,>));

        using var registry = new OrbModding.Common.Runtime.ServiceCycle.Registration.ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1));
        using var registration = registry.Register(definition, new AutomataConfiguration());

        Assert.Equal(0, registration.Ordinal);
    }

    private static AutomataServiceMetadata Metadata() => new(
        new ServiceId("test.composed"),
        WakePolicy.Immediate,
        new ServiceFaultRecoveryPolicy(
            new MonotonicDuration(1),
            new MonotonicDuration(2)));

    private readonly struct Frame { }
    private readonly struct State { }
    private readonly struct Action { }

    private sealed class WorkerDefinition :
        IServiceCycleWorkerDefinition<Frame, AutomataConfiguration, State, Action>
    {
        public State CreateState(LifecycleGeneration lifecycle) => default;
        public void ReleaseState(ref State state) => state = default;
        public void ReleaseFrame(ref Frame frame) => frame = default;

        public WakePolicy Evaluate(
            in Frame frame,
            in AutomataConfiguration config,
            in ServiceCycleContext context,
            ref State state,
            ServiceActionWriter<Action> actions) =>
            WakePolicy.Immediate;

        public void ProjectState(
            in State state,
            in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output)
        {
        }
    }
}
