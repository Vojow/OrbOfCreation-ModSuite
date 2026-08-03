using System;

namespace OrbAutomata;

internal enum GenericLevelActionKind
{
    Purchase = 1,
    Bonus = 2,
}

internal readonly struct GenericLevelAction
{
    internal GenericLevelAction(
        GenericLevelActionKind kind,
        Guid targetId,
        string nativeType,
        long lifecycleEpoch)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A level target identity is required.", nameof(targetId));
        if (string.IsNullOrWhiteSpace(nativeType))
            throw new ArgumentException("An exact native type is required.", nameof(nativeType));
        Kind = kind;
        TargetId = targetId;
        NativeType = nativeType;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal GenericLevelActionKind Kind { get; }
    internal Guid TargetId { get; }
    internal string NativeType { get; }
    internal long LifecycleEpoch { get; }
}
