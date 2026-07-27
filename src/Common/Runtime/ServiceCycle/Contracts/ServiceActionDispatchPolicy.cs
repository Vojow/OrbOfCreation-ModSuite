using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>
/// When a service's actions are dispatched relative to other services' within one frame.
/// </summary>
/// <remarks>
/// Dispatch is otherwise a fair rotation, and this is the one exception to it. A service that
/// publishes data the others consume goes first, so a snapshot acquired this frame is live before any
/// consumer decides what to do this frame rather than a frame later. It is a freshness rule, not a
/// correctness one — generations are stamped when the game was read, so a consumer reaches the right
/// conclusion whichever order the two run in.
/// </remarks>
public enum ServiceActionDispatchClass
{
    /// <summary>Acts on the game. Dispatched after everything that feeds it.</summary>
    GameMutation = 1,

    /// <summary>Hands the pump data other services read. Dispatched first.</summary>
    Publication = 2,
}

/// <summary>
/// What a service is for. There is no third shape.
/// </summary>
public enum ServiceShape
{
    /// <summary>
    /// Reads the game and publishes the result. It changes no game state, so the world gate can
    /// never close on it: only a committed native mutation arms that gate, and a Source commits none.
    /// </summary>
    Source = 1,

    /// <summary>
    /// Consumes the publications and acts on the game, and must therefore not decide twice against a
    /// snapshot its own action already invalidated.
    /// </summary>
    Ordinary = 2,
}

/// <summary>
/// Bounds how many actions one service may dispatch during its turn in a Unity frame, and where that
/// turn falls. Every action remains an independent service callback with fresh native validation.
/// </summary>
public readonly struct ServiceActionDispatchPolicy : IEquatable<ServiceActionDispatchPolicy>
{
    private ServiceActionDispatchPolicy(
        int maximumActionsPerFrame,
        ServiceActionDispatchClass dispatchClass)
    {
        MaximumActionsPerFrame = maximumActionsPerFrame;
        DispatchClass = dispatchClass;
    }

    public int MaximumActionsPerFrame { get; }
    public ServiceActionDispatchClass DispatchClass { get; }
    /// <summary>
    /// The service's shape, read off where its turn falls rather than declared beside it: a service
    /// that hands the pump data other services read is a Source, and everything else is Ordinary.
    /// Declaring it separately would only create a way for the two answers to disagree.
    /// </summary>
    public ServiceShape Shape => DispatchClass == ServiceActionDispatchClass.Publication
        ? ServiceShape.Source
        : ServiceShape.Ordinary;

    public bool IsValid =>
        MaximumActionsPerFrame > 0 &&
        DispatchClass is ServiceActionDispatchClass.GameMutation or ServiceActionDispatchClass.Publication;

    public static ServiceActionDispatchPolicy Single { get; } =
        new(1, ServiceActionDispatchClass.GameMutation);

    public static ServiceActionDispatchPolicy Bounded(int maximumActionsPerFrame) =>
        Bounded(maximumActionsPerFrame, ServiceActionDispatchClass.GameMutation);

    public static ServiceActionDispatchPolicy Bounded(
        int maximumActionsPerFrame,
        ServiceActionDispatchClass dispatchClass)
    {
        if (maximumActionsPerFrame <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumActionsPerFrame),
                "At least one action per frame is required.");
        if (dispatchClass is not (ServiceActionDispatchClass.GameMutation or
            ServiceActionDispatchClass.Publication))
            throw new ArgumentOutOfRangeException(nameof(dispatchClass));
        return new ServiceActionDispatchPolicy(maximumActionsPerFrame, dispatchClass);
    }

    public bool Equals(ServiceActionDispatchPolicy other) =>
        MaximumActionsPerFrame == other.MaximumActionsPerFrame &&
        DispatchClass == other.DispatchClass;

    public override bool Equals(object? obj) =>
        obj is ServiceActionDispatchPolicy other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(MaximumActionsPerFrame, DispatchClass);

    public static bool operator ==(
        ServiceActionDispatchPolicy left,
        ServiceActionDispatchPolicy right) =>
        left.Equals(right);

    public static bool operator !=(
        ServiceActionDispatchPolicy left,
        ServiceActionDispatchPolicy right) =>
        !left.Equals(right);
}
