using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact Unity-main-thread implementation of the queue recovery seam.</summary>
internal sealed class ActionQueueNativeRecoveryAdapter : IActionQueueRecoveryNativePort
{
    private readonly Func<bool> _isMainThread;
    private readonly Func<long> _readLifecycle;

    internal ActionQueueNativeRecoveryAdapter(
        Func<bool> isMainThread,
        Func<long> readLifecycle)
    {
        _isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
        _readLifecycle = readLifecycle ?? throw new ArgumentNullException(nameof(readLifecycle));
    }

    public bool IsMainThread => _isMainThread();

    public bool TryCapture(
        Guid queueId,
        Guid memberId,
        string exactNativeType,
        out ActionQueueRecoveryNativeState state,
        out string reason)
    {
        state = default;
        reason = string.Empty;
        if (!IsMainThread)
        {
            reason = "queue recovery capture is not on the Unity main thread";
            return false;
        }
        try
        {
            var queue = ActionManager.instance?.actionableItems;
            if (queue is null || queue.GetGuid() != queueId ||
                queueId != KnownEntities.ActiveActionables.Uuid)
            {
                reason = "the exact active-actionables queue was not resolved";
                return false;
            }

            IActionable? match = null;
            for (var index = 0; index < queue.value.Count; index++)
            {
                var candidate = queue.value[index];
                if (candidate is null || candidate.GetGuid() != memberId) continue;
                if (match is not null)
                {
                    reason = "the exact member UUID appears more than once";
                    return false;
                }
                match = candidate;
            }
            if (match is null ||
                !string.Equals(match.GetType().Name, exactNativeType, StringComparison.Ordinal))
            {
                reason = "the exact UUID/type queue member was not resolved";
                return false;
            }

            var pending = ReadPending(match, exactNativeType);
            var stacks = queue.GetStacks(match);
            var total = queue.GetTotalStacks();
            var remaining = queue.GetRemainingRoom();
            if (pending < 0 || stacks < 0 || total < stacks || remaining < 0)
            {
                reason = "the exact queue member returned invalid native counts";
                return false;
            }

            state = new ActionQueueRecoveryNativeState(
                _readLifecycle(), queueId, memberId, exactNativeType,
                stacks, pending, total, remaining);
            reason = "captured exact native queue member";
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException || ex is ArgumentException ||
            ex is ArithmeticException || ex is NullReferenceException)
        {
            reason = ex.GetBaseException().GetType().Name;
            return false;
        }
    }

    public bool TryUnloadExactExcess(
        Guid queueId,
        Guid memberId,
        string exactNativeType,
        int excessStacks,
        out string reason)
    {
        reason = string.Empty;
        if (excessStacks <= 0 ||
            !TryResolve(queueId, memberId, exactNativeType, out var actionable, out reason))
            return false;
        try
        {
            ActionManager.UnloadAction(actionable!, excessStacks);
            reason = "native UnloadAction returned";
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException || ex is ArgumentException ||
            ex is ArithmeticException || ex is NullReferenceException)
        {
            reason = ex.GetBaseException().GetType().Name;
            return false;
        }
    }

    private bool TryResolve(
        Guid queueId,
        Guid memberId,
        string exactNativeType,
        out IActionable? actionable,
        out string reason)
    {
        actionable = null;
        reason = string.Empty;
        if (!IsMainThread)
        {
            reason = "queue recovery mutation is not on the Unity main thread";
            return false;
        }
        var queue = ActionManager.instance?.actionableItems;
        if (queue is null || queue.GetGuid() != queueId ||
            queueId != KnownEntities.ActiveActionables.Uuid)
        {
            reason = "the exact active-actionables queue was not resolved";
            return false;
        }
        for (var index = 0; index < queue.value.Count; index++)
        {
            var candidate = queue.value[index];
            if (candidate is null || candidate.GetGuid() != memberId) continue;
            if (actionable is not null)
            {
                actionable = null;
                reason = "the exact member UUID appears more than once";
                return false;
            }
            actionable = candidate;
        }
        if (actionable is null ||
            !string.Equals(actionable.GetType().Name, exactNativeType, StringComparison.Ordinal))
        {
            actionable = null;
            reason = "the exact UUID/type queue member was not resolved";
            return false;
        }
        return true;
    }

    private static int ReadPending(IActionable actionable, string exactNativeType) =>
        exactNativeType switch
        {
            ActionQueueIntegrityClassifier.StructureNativeType
                when actionable.GetType() == typeof(StructureSO) =>
                ((StructureSO)actionable).GetQueuedQuantity(),
            ActionQueueIntegrityClassifier.UpgradeNativeType
                when actionable.GetType() == typeof(UpgradeSO) =>
                checked(((UpgradeSO)actionable).GetQueuedPurchaseLevel() -
                    ((UpgradeSO)actionable).GetPurchaseLevel()),
            _ => -1,
        };
}
