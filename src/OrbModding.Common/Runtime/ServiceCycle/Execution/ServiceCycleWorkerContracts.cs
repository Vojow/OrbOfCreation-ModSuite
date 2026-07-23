using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal interface IServiceCycleWorkerStarter
{
    void Start(Thread thread);
}

internal interface IServiceCycleWorkerExitObserver
{
    void OnWorkerExitPrepared();
}

internal sealed class ServiceFrameStorage<TFrame>
{
    internal ServiceFrameStorage(
        TFrame frame,
        LifecycleGeneration lifecycle)
    {
        Value = frame;
        Lifecycle = lifecycle;
    }

    internal TFrame Value;
    internal LifecycleGeneration Lifecycle { get; }
}
