using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal enum ServiceResourceRole
{
    WorkerDefinition = 1,
    Frame = 2,
    State = 3,
}

internal enum ServiceResourceClaimResult
{
    Claimed = 1,
    Aliased = 2,
    CapacityExhausted = 3,
    Contended = 4,
}

internal sealed class ServiceResourceClaim
{
    private const int Reserved = 1;
    private const int Owned = 2;
    private const int Retired = 3;
    private const int FactoryOpen = 1;
    private const int FactoryClosing = 2;

    private object? _identity;
    private int _state = Reserved;
    private int _factoryState = FactoryOpen;

    internal ServiceResourceClaim(long token, ServiceResourceRole role)
    {
        Token = token;
        Role = role;
    }

    internal int SlotIndex { get; private set; } = -1;
    internal long Token { get; }
    internal ServiceResourceRole Role { get; }
    internal object? Identity => Volatile.Read(ref _identity);
    internal bool IsReserved => Volatile.Read(ref _state) == Reserved;
    internal bool IsRetired => Volatile.Read(ref _state) == Retired;
    internal bool IsFactoryOpen => Volatile.Read(ref _factoryState) == FactoryOpen;
    internal bool IsFactoryClosing => Volatile.Read(ref _factoryState) == FactoryClosing;

    internal void AssignSlot(int slotIndex) => SlotIndex = slotIndex;

    internal void PublishOwned(object identity)
    {
        if (Volatile.Read(ref _state) != Reserved)
            throw new InvalidOperationException(
                "A resource reservation can be finalized only once.");
        Volatile.Write(ref _identity, identity);
        Volatile.Write(ref _state, Owned);
    }

    internal bool MarkRetired() =>
        Interlocked.CompareExchange(ref _state, Retired, Owned) == Owned;

    internal bool MarkFactoryClosing() =>
        Interlocked.CompareExchange(
            ref _factoryState,
            FactoryClosing,
            FactoryOpen) == FactoryOpen;

    internal void ClearIdentity() => Volatile.Write(ref _identity, null);
}
