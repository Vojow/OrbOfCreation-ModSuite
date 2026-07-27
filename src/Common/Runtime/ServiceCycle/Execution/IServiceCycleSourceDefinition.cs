using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// A service that reads the game itself and publishes what it read.
/// </summary>
/// <remarks>
/// <para>
/// The only shape with a main-thread capture, and the only one that needs one: the game's own state
/// can be read nowhere else, so this service fills a buffer on the main thread that its worker then
/// derives from. Every other service consumes the publication that comes out the far end, which is
/// why the ordinary contract has no capture at all.
/// </para>
/// <para>
/// The buffer is <see cref="GameWorldCycleFrame"/>, named rather than generic. There is one game and
/// therefore one shape of raw reading; a type parameter here would only be a promise that a second
/// one could exist, and it cannot. The runtime constructs one per lifecycle and hands the same
/// instance to the capture and to the evaluation — passed by value, because it is a class the
/// runtime owns for the whole lifecycle and there is no instance to swap.
/// </para>
/// </remarks>
internal interface IServiceCycleSourceDefinition<TState, TAction> :
    IServiceCycleMainThreadDefinition<TAction>
{
    /// <summary>
    /// Creates the worker resource under the runtime's serialized reference-factory admission, on the
    /// same terms as the ordinary shape's.
    /// </summary>
    /// <remarks>
    /// The source worker contract is its own rather than a narrowing of the ordinary one, which is
    /// why this shape is a sibling of <see cref="IServiceCycleDefinition{TState, TAction}"/> and not
    /// an extension of it. The two workers read different things — this one derives from the buffer
    /// the capture filled, an ordinary one from the published world — and nothing but the type stops
    /// a definition from handing over the wrong one.
    /// </remarks>
    IServiceCycleSourceWorkerDefinition<TState, TAction> CreateWorkerDefinition();

    /// <summary>
    /// Reads the game into the runtime's buffer, on the main thread, before the cycle is queued.
    /// </summary>
    /// <remarks>
    /// May report the reading unavailable, in which case no cycle starts and the runtime sleeps on
    /// the returned wake policy. That is a decision, not a failure: a game that is not ready to be
    /// read has nothing wrong with it.
    /// </remarks>
    ServiceCaptureResult Capture(
        GameWorldCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in ServiceCaptureContext context);
}

/// <summary>
/// The worker half of the source shape.
/// </summary>
/// <remarks>
/// It derives from the buffer the main-thread capture filled rather than from the published world,
/// because it is the service that produces that publication and cannot consume it.
/// </remarks>
internal interface IServiceCycleSourceWorkerDefinition<TState, TAction> :
    IServiceCycleWorkerStateDefinition<TState>
{
    /// <summary>
    /// Derives this cycle's decision from the buffer the capture filled, on the worker thread.
    /// </summary>
    /// <remarks>
    /// The buffer is the same instance the capture wrote, handed straight across. It crosses threads
    /// once per cycle and only in one direction: the capture completes before the cycle is queued,
    /// and the worker is finished with it before the next capture can run.
    /// </remarks>
    WakePolicy Evaluate(
        GameWorldCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref TState state,
        ServiceActionWriter<TAction> actions);
}
