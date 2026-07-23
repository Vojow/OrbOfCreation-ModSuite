using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed class ServiceRunnerResourceIdentity
{
    internal ServiceRunnerResourceIdentity(
        object workerDefinition,
        object? frame,
        object actionStore,
        object handoff,
        object worker,
        object mainState,
        object startCoordinator,
        object batchCompletion)
    {
        WorkerDefinition = workerDefinition;
        Frame = frame;
        ActionStore = actionStore;
        Handoff = handoff;
        Worker = worker;
        MainState = mainState;
        StartCoordinator = startCoordinator;
        BatchCompletion = batchCompletion;
    }

    internal object WorkerDefinition { get; }
    internal object? Frame { get; }
    internal object ActionStore { get; }
    internal object Handoff { get; }
    internal object Worker { get; }
    internal object MainState { get; }
    internal object StartCoordinator { get; }
    internal object BatchCompletion { get; }
}

internal abstract class ServiceRunnerResourceClaimException :
    InvalidOperationException
{
    protected ServiceRunnerResourceClaimException(
        string message,
        bool suppressFrameRelease)
        : base(message)
    {
        SuppressFrameRelease = suppressFrameRelease;
    }

    internal bool SuppressFrameRelease { get; }
}

internal sealed class ServiceRunnerResourceAliasingException :
    ServiceRunnerResourceClaimException
{
    internal ServiceRunnerResourceAliasingException(
        string resource,
        bool suppressFrameRelease)
        : base(
            $"A live service runner aliases the candidate {resource} resource.",
            suppressFrameRelease) { }
}

internal sealed class ServiceRunnerResourceContentionException :
    ServiceRunnerResourceClaimException
{
    internal ServiceRunnerResourceContentionException(string resource)
        : base(
            $"The {resource} factory admission token is temporarily busy.",
            suppressFrameRelease: false) { }
}
