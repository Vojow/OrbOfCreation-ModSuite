using System;
using System.Reflection;
using HarmonyLib;
using OrbModding.Common;

namespace OrbAutomata;

internal enum ActionQueueCompletionKind
{
    Structure = 1,
    Upgrade = 2,
}

internal enum ActionQueueCompletionFaultOutcome
{
    FatalExceptionUncontained = 1,
    LifecycleChanged = 2,
    NativeStateUnreadable = 3,
    IdentityChanged = 4,
    InitialStateContradictory = 5,
    PendingCountDidNotDecrease = 6,
    OmittedUnstackNotProven = 7,
    NativeUnloadThrew = 8,
    RepairPostconditionFailed = 9,
    RepairedOmittedUnstack = 10,
}

/// <summary>
/// Detached evidence from a native completion exception. It carries no Unity/native references and
/// no exception message or stack trace, so a coordinator may retain or publish it safely.
/// </summary>
internal readonly struct ActionQueueCompletionFaultEvent
{
    internal ActionQueueCompletionFaultEvent(
        ActionQueueCompletionKind kind,
        Guid actionableId,
        long lifecycleBefore,
        long lifecycleAfter,
        int stacksBefore,
        int pendingBefore,
        int stacksAfterFault,
        int pendingAfterFault,
        int stacksAfterRepair,
        int pendingAfterRepair,
        ActionQueueCompletionFaultOutcome outcome,
        string exceptionType)
    {
        Kind = kind;
        ActionableId = actionableId;
        LifecycleBefore = lifecycleBefore;
        LifecycleAfter = lifecycleAfter;
        StacksBefore = stacksBefore;
        PendingBefore = pendingBefore;
        StacksAfterFault = stacksAfterFault;
        PendingAfterFault = pendingAfterFault;
        StacksAfterRepair = stacksAfterRepair;
        PendingAfterRepair = pendingAfterRepair;
        Outcome = outcome;
        ExceptionType = exceptionType ?? string.Empty;
    }

    internal ActionQueueCompletionKind Kind { get; }
    internal Guid ActionableId { get; }
    internal long LifecycleBefore { get; }
    internal long LifecycleAfter { get; }
    internal int StacksBefore { get; }
    internal int PendingBefore { get; }
    internal int StacksAfterFault { get; }
    internal int PendingAfterFault { get; }
    internal int StacksAfterRepair { get; }
    internal int PendingAfterRepair { get; }
    internal ActionQueueCompletionFaultOutcome Outcome { get; }
    internal string ExceptionType { get; }
}

internal interface IActionQueueIntegrityEventSink
{
    void Observe(in ActionQueueCompletionFaultEvent evidence);
}

/// <summary>
/// Contains the exact native failure window in which CompleteAction has reduced the actionable's
/// pending count but ActionableListVariable.Process cannot execute its following Unstack call.
/// </summary>
/// <remarks>
/// Harmony invokes this bridge on Unity's main thread. A successful containment removes only the
/// one outer stack Process demonstrably omitted. It never retries completion, changes progression,
/// suppresses the exception, or edits save data.
/// </remarks>
internal static class ActionQueueCompletionFaultBridge
{
    private static IActionQueueIntegrityEventSink? _sink;
    private static Func<long> _readLifecycle = ReadSharedLifecycle;

    internal static void Install(
        IActionQueueIntegrityEventSink sink,
        Func<long>? readLifecycle = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _readLifecycle = readLifecycle ?? ReadSharedLifecycle;
    }

    internal static void Reset()
    {
        _sink = null;
        _readLifecycle = ReadSharedLifecycle;
    }

    internal static void CaptureStructure(
        StructureSO actionable,
        out ActionQueueCompletionFaultState state) =>
        state = Capture(
            actionable,
            ActionQueueCompletionKind.Structure,
            static item => item.GetQueuedQuantity());

    internal static void CaptureUpgrade(
        UpgradeSO actionable,
        out ActionQueueCompletionFaultState state) =>
        state = Capture(
            actionable,
            ActionQueueCompletionKind.Upgrade,
            static item => checked(item.GetQueuedPurchaseLevel() - item.GetPurchaseLevel()));

    internal static Exception? FinishStructure(
        StructureSO actionable,
        in ActionQueueCompletionFaultState state,
        Exception? exception) =>
        Finish(
            actionable,
            state,
            exception,
            static item => item.GetQueuedQuantity());

    internal static Exception? FinishUpgrade(
        UpgradeSO actionable,
        in ActionQueueCompletionFaultState state,
        Exception? exception) =>
        Finish(
            actionable,
            state,
            exception,
            static item => checked(item.GetQueuedPurchaseLevel() - item.GetPurchaseLevel()));

    private static ActionQueueCompletionFaultState Capture<TActionable>(
        TActionable actionable,
        ActionQueueCompletionKind kind,
        Func<TActionable, int> readPending)
        where TActionable : class, IActionable
    {
        try
        {
            if (actionable.GetType() != typeof(TActionable) ||
                !TryReadQueue(out var queue))
                return default;

            var actionableId = actionable.GetGuid();
            var pending = readPending(actionable);
            var stacks = queue.GetStacks(actionable);
            if (actionableId == Guid.Empty || pending < 0 || stacks < 0)
                return default;

            return new ActionQueueCompletionFaultState(
                kind,
                actionableId,
                ReadLifecycle(),
                queue,
                actionable,
                stacks,
                pending);
        }
        catch
        {
            // A diagnostic/containment prefix must never become the exception that aborts native
            // completion. Without a complete before-state the finalizer has no mutation authority.
            return default;
        }
    }

    private static Exception? Finish<TActionable>(
        TActionable actionable,
        in ActionQueueCompletionFaultState state,
        Exception? exception,
        Func<TActionable, int> readPending)
        where TActionable : class, IActionable
    {
        if (exception is null || !state.IsCaptured)
            return exception;

        var lifecycleAfter = ReadLifecycle();
        var stacksAfterFault = -1;
        var pendingAfterFault = -1;
        var stacksAfterRepair = -1;
        var pendingAfterRepair = -1;
        var outcome = ActionQueueCompletionFaultOutcome.NativeStateUnreadable;

        try
        {
            if (IsFatal(exception))
            {
                outcome = ActionQueueCompletionFaultOutcome.FatalExceptionUncontained;
                return exception;
            }

            if (lifecycleAfter != state.Lifecycle)
            {
                outcome = ActionQueueCompletionFaultOutcome.LifecycleChanged;
                return exception;
            }

            if (actionable.GetType() != ExpectedType(state.Kind) ||
                actionable.GetGuid() != state.ActionableId ||
                !ReferenceEquals(actionable, state.Actionable) ||
                !TryReadQueue(out var queue) ||
                !ReferenceEquals(queue, state.Queue))
            {
                outcome = ActionQueueCompletionFaultOutcome.IdentityChanged;
                return exception;
            }

            pendingAfterFault = readPending(actionable);
            stacksAfterFault = queue.GetStacks(actionable);
            if (pendingAfterFault < 0 || stacksAfterFault < 0)
            {
                outcome = ActionQueueCompletionFaultOutcome.NativeStateUnreadable;
                return exception;
            }

            if (state.StacksBefore != state.PendingBefore)
            {
                outcome = ActionQueueCompletionFaultOutcome.InitialStateContradictory;
                return exception;
            }

            if (pendingAfterFault >= state.PendingBefore)
            {
                outcome = ActionQueueCompletionFaultOutcome.PendingCountDidNotDecrease;
                return exception;
            }

            // Structure completion may finish a native bulk and internally unload all but the one
            // outer Process stack. The invariant that proves that exact missing outer unstack is
            // therefore the post-fault differential, not a hard-coded pending delta of one.
            if ((long)stacksAfterFault - pendingAfterFault != 1L)
            {
                outcome = ActionQueueCompletionFaultOutcome.OmittedUnstackNotProven;
                return exception;
            }

            try
            {
                ActionManager.UnloadAction(actionable, 1);
            }
            catch
            {
                outcome = ActionQueueCompletionFaultOutcome.NativeUnloadThrew;
                TryReadAfterRepair(
                    actionable,
                    state,
                    readPending,
                    ref stacksAfterRepair,
                    ref pendingAfterRepair);
                return exception;
            }

            if (ReadLifecycle() != state.Lifecycle ||
                !TryReadQueue(out var queueAfter) ||
                !ReferenceEquals(queueAfter, state.Queue) ||
                actionable.GetType() != ExpectedType(state.Kind) ||
                actionable.GetGuid() != state.ActionableId)
            {
                outcome = ActionQueueCompletionFaultOutcome.RepairPostconditionFailed;
                return exception;
            }

            pendingAfterRepair = readPending(actionable);
            stacksAfterRepair = queueAfter.GetStacks(actionable);
            outcome = pendingAfterRepair == pendingAfterFault &&
                      stacksAfterRepair == pendingAfterRepair &&
                      stacksAfterRepair == stacksAfterFault - 1
                ? ActionQueueCompletionFaultOutcome.RepairedOmittedUnstack
                : ActionQueueCompletionFaultOutcome.RepairPostconditionFailed;
            return exception;
        }
        catch
        {
            // Preserve the native exception even if diagnostic post-reading itself fails.
            outcome = ActionQueueCompletionFaultOutcome.NativeStateUnreadable;
            return exception;
        }
        finally
        {
            Observe(new ActionQueueCompletionFaultEvent(
                state.Kind,
                state.ActionableId,
                state.Lifecycle,
                lifecycleAfter,
                state.StacksBefore,
                state.PendingBefore,
                stacksAfterFault,
                pendingAfterFault,
                stacksAfterRepair,
                pendingAfterRepair,
                outcome,
                ExceptionType(exception)));
        }
    }

    private static void TryReadAfterRepair<TActionable>(
        TActionable actionable,
        in ActionQueueCompletionFaultState state,
        Func<TActionable, int> readPending,
        ref int stacks,
        ref int pending)
        where TActionable : class, IActionable
    {
        try
        {
            if (!TryReadQueue(out var queue) || !ReferenceEquals(queue, state.Queue)) return;
            stacks = queue.GetStacks(actionable);
            pending = readPending(actionable);
        }
        catch
        {
            stacks = -1;
            pending = -1;
        }
    }

    private static bool TryReadQueue(out ActionableListVariable queue)
    {
        queue = null!;
        var manager = ActionManager.instance;
        if (manager is null || manager.actionableItems is null) return false;
        queue = manager.actionableItems;
        return true;
    }

    private static long ReadLifecycle()
    {
        try { return _readLifecycle(); }
        catch { return -1; }
    }

    private static long ReadSharedLifecycle() =>
        GameLifecycleMonitor.Shared.Current.Generation;

    private static Type ExpectedType(ActionQueueCompletionKind kind) => kind switch
    {
        ActionQueueCompletionKind.Structure => typeof(StructureSO),
        ActionQueueCompletionKind.Upgrade => typeof(UpgradeSO),
        _ => typeof(void),
    };

    private static bool IsFatal(Exception exception) =>
        exception is StackOverflowException or OutOfMemoryException or AccessViolationException;

    private static string ExceptionType(Exception exception)
    {
        var name = exception.GetType().FullName ?? exception.GetType().Name;
        return name.Length <= 160 ? name : name.Substring(0, 160);
    }

    private static void Observe(in ActionQueueCompletionFaultEvent evidence)
    {
        try { _sink?.Observe(evidence); }
        catch
        {
            // Observer failure is detached from both native completion and containment.
        }
    }
}

internal readonly struct ActionQueueCompletionFaultState
{
    internal ActionQueueCompletionFaultState(
        ActionQueueCompletionKind kind,
        Guid actionableId,
        long lifecycle,
        ActionableListVariable queue,
        IActionable actionable,
        int stacksBefore,
        int pendingBefore)
    {
        Kind = kind;
        ActionableId = actionableId;
        Lifecycle = lifecycle;
        Queue = queue;
        Actionable = actionable;
        StacksBefore = stacksBefore;
        PendingBefore = pendingBefore;
        IsCaptured = true;
    }

    internal bool IsCaptured { get; }
    internal ActionQueueCompletionKind Kind { get; }
    internal Guid ActionableId { get; }
    internal long Lifecycle { get; }
    internal ActionableListVariable? Queue { get; }
    internal IActionable? Actionable { get; }
    internal int StacksBefore { get; }
    internal int PendingBefore { get; }
}

[HarmonyPatch]
internal static class StructureCompletionFaultPatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("StructureSO")?.GetMethod(
            "CompleteAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);

    internal static void Prefix(
        StructureSO __instance,
        out ActionQueueCompletionFaultState __state) =>
        ActionQueueCompletionFaultBridge.CaptureStructure(__instance, out __state);

    internal static Exception? Finalizer(
        StructureSO __instance,
        ActionQueueCompletionFaultState __state,
        Exception? __exception) =>
        ActionQueueCompletionFaultBridge.FinishStructure(__instance, __state, __exception);
}

[HarmonyPatch]
internal static class UpgradeCompletionFaultPatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("UpgradeSO")?.GetMethod(
            "CompleteAction",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);

    internal static void Prefix(
        UpgradeSO __instance,
        out ActionQueueCompletionFaultState __state) =>
        ActionQueueCompletionFaultBridge.CaptureUpgrade(__instance, out __state);

    internal static Exception? Finalizer(
        UpgradeSO __instance,
        ActionQueueCompletionFaultState __state,
        Exception? __exception) =>
        ActionQueueCompletionFaultBridge.FinishUpgrade(__instance, __state, __exception);
}
