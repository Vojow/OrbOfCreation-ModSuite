using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal interface IServiceCycleWorkerStarter
{
    void Start(Thread thread);
}

internal interface IServiceCycleWorkerExitObserver
{
    void OnWorkerExitPrepared();
}

