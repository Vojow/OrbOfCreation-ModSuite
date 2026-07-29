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
}
