using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayExportSlotPool
{
    internal static ServiceCycleReplayExportSlot? TryClaim(
        ServiceCycleReplayExportSlot first,
        ServiceCycleReplayExportSlot second)
    {
        if (Interlocked.CompareExchange(ref first.State, ServiceCycleReplayExportSlot.OwnerClaimed,
                ServiceCycleReplayExportSlot.Free) == ServiceCycleReplayExportSlot.Free) return first;
        return Interlocked.CompareExchange(ref second.State, ServiceCycleReplayExportSlot.OwnerClaimed,
            ServiceCycleReplayExportSlot.Free) == ServiceCycleReplayExportSlot.Free ? second : null;
    }

    internal static ServiceCycleReplayExportSlot? TryTakeNextReady(
        ServiceCycleReplayExportSlot first,
        ServiceCycleReplayExportSlot second)
    {
        var firstReady = Volatile.Read(ref first.State) == ServiceCycleReplayExportSlot.Ready;
        var secondReady = Volatile.Read(ref second.State) == ServiceCycleReplayExportSlot.Ready;
        if (!firstReady && !secondReady) return null;
        var candidate = firstReady && secondReady
            ? (first.Ordinal <= second.Ordinal ? first : second)
            : (firstReady ? first : second);
        return Interlocked.CompareExchange(ref candidate.State, ServiceCycleReplayExportSlot.WorkerOwned,
            ServiceCycleReplayExportSlot.Ready) == ServiceCycleReplayExportSlot.Ready ? candidate : null;
    }

    internal static bool TryDiscardReady(ServiceCycleReplayExportSlot slot)
    {
        if (Interlocked.CompareExchange(ref slot.State, ServiceCycleReplayExportSlot.Free,
                ServiceCycleReplayExportSlot.Ready) != ServiceCycleReplayExportSlot.Ready) return false;
        Clear(slot);
        return true;
    }

    internal static void Release(ServiceCycleReplayExportSlot slot)
    {
        Clear(slot);
        Volatile.Write(ref slot.State, ServiceCycleReplayExportSlot.Free);
    }

    private static void Clear(ServiceCycleReplayExportSlot slot)
    {
        slot.EventCount = 0;
        slot.SemanticSession = default;
        slot.Dropped = default;
        slot.Recording = default;
    }
}
