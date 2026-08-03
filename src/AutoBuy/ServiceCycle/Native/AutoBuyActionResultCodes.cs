using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Why an Auto Buy action ended the way it did. Codes are append-only.</summary>
internal static class AutoBuyActionResultCodes
{
    /// <summary>
    /// Another plugin holds the action-family lease for this candidate's kind.
    /// </summary>
    /// <remarks>
    /// A rejection rather than a fault: standing down for a plugin that owns the family is the
    /// arbitration working, not a failure, and the lease can come back at any time.
    /// </remarks>
    public static ServiceActionResultCode ActionFamilyUnavailable => new(2048);

    public static ServiceActionResultCode OwningViewUnavailable => new(2049);
    public static ServiceActionResultCode OwningViewRelationMissing => new(2050);
    public static ServiceActionResultCode OwningViewRelationUnreadable => new(2051);
    public static ServiceActionResultCode OwningViewRelationContradictory => new(2053);
    public static ServiceActionResultCode StructureUnavailable => new(2054);
    public static ServiceActionResultCode DestinationCapacityFull => new(2055);
    public static ServiceActionResultCode DestinationCapacityContractUnavailable => new(2056);
    public static ServiceActionResultCode DestinationCapacityIdentityMismatch => new(2057);

    /// <summary>
    /// Verified spend earlier in this Auto Buy batch invalidated the remaining planned resource
    /// margin. The action is skipped before native submission and the next publication replans it.
    /// </summary>
    public static ServiceActionResultCode BatchSpendDrift => new(2058);
}
