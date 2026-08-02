using System;

namespace OrbAutomata;

/// <summary>Stable intent to discover one exact native <c>IDiscoverable</c>.</summary>
internal readonly struct GenericDiscoveryAction
{
    internal GenericDiscoveryAction(Guid targetId, string expectedNativeType, long lifecycleEpoch)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A discoverable identity is required.", nameof(targetId));
        if (string.IsNullOrWhiteSpace(expectedNativeType))
            throw new ArgumentException("An exact native discoverable type is required.", nameof(expectedNativeType));
        TargetId = targetId;
        ExpectedNativeType = expectedNativeType;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal Guid TargetId { get; }
    internal string ExpectedNativeType { get; }
    internal long LifecycleEpoch { get; }
}
