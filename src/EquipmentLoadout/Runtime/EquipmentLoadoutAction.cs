using System;

namespace OrbAutomata;

internal enum EquipmentLoadoutActionKind { Equip = 1, Unequip = 2 }

internal readonly struct EquipmentLoadoutAction
{
    internal EquipmentLoadoutAction(EquipmentLoadoutActionKind kind, Guid targetId, long lifecycleEpoch)
    {
        if (targetId == Guid.Empty) throw new ArgumentException("An equipment identity is required.", nameof(targetId));
        Kind = kind;
        TargetId = targetId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal EquipmentLoadoutActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal long LifecycleEpoch { get; }
}
