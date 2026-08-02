using OrbModding.Common;

namespace OrbAutomata;

internal enum EquipmentLoadoutPreflight
{
    Proceeded = 0, ContractUnavailable = 1, WrongThread = 3,
    LifecycleReplaced = 4, IdentityUnavailable = 5, NotCreated = 6,
    AlreadyInRequestedState = 7, LoadoutFull = 8, EquipmentTypeFull = 9,
    UsageUnaffordable = 10, MultiBuyUnavailable = 11, MutationPermitUnavailable = 12,
    PostCommitFault = 13, VerificationFailed = 14,
}

internal enum EquipmentLoadoutNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

internal readonly struct EquipmentLoadoutState
{
    internal EquipmentLoadoutState(int equippedStacks, int maximumStacks, int multiBuy,
        int usedSlots, int maximumSlots, int typeUsedSlots, int typeMaximumSlots,
        bool usageAffordable, int maximumAffordableStacks)
    {
        EquippedStacks = equippedStacks;
        MaximumStacks = maximumStacks;
        MultiBuy = multiBuy;
        UsedSlots = usedSlots;
        MaximumSlots = maximumSlots;
        TypeUsedSlots = typeUsedSlots;
        TypeMaximumSlots = typeMaximumSlots;
        UsageAffordable = usageAffordable;
        MaximumAffordableStacks = maximumAffordableStacks;
    }
    internal int EquippedStacks { get; }
    internal int MaximumStacks { get; }
    internal int MultiBuy { get; }
    internal int UsedSlots { get; }
    internal int MaximumSlots { get; }
    internal int TypeUsedSlots { get; }
    internal int TypeMaximumSlots { get; }
    internal bool UsageAffordable { get; }
    internal int MaximumAffordableStacks { get; }
}

internal readonly struct EquipmentLoadoutReceipt
{
    internal EquipmentLoadoutReceipt(bool evidenceAvailable, EquipmentLoadoutActionKind kind,
        int requestedAmount, in EquipmentLoadoutState before, in EquipmentLoadoutState after)
    {
        EvidenceAvailable = evidenceAvailable;
        Kind = kind;
        RequestedAmount = requestedAmount;
        Before = before;
        After = after;
    }
    internal bool EvidenceAvailable { get; }
    internal EquipmentLoadoutActionKind Kind { get; }
    internal int RequestedAmount { get; }
    internal EquipmentLoadoutState Before { get; }
    internal EquipmentLoadoutState After { get; }
}

internal readonly struct EquipmentLoadoutSubmission
{
    internal EquipmentLoadoutSubmission(EquipmentLoadoutPreflight preflight,
        EquipmentLoadoutNativeStage stage, NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome, in EquipmentLoadoutReceipt receipt, string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Receipt = receipt;
        Reason = reason ?? string.Empty;
    }
    internal EquipmentLoadoutPreflight Preflight { get; }
    internal EquipmentLoadoutNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal EquipmentLoadoutReceipt Receipt { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == EquipmentLoadoutPreflight.Proceeded && Outcome == NativeMutationOutcome.Verified;
    internal static EquipmentLoadoutSubmission Reject(EquipmentLoadoutPreflight preflight, string reason) =>
        new(preflight, EquipmentLoadoutNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, default, reason);
}
