using System;
using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Coordination;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ActionQueueCompletionFaultCollection
{
    public const string Name = "Action queue completion fault containment";
}

[Collection(ActionQueueCompletionFaultCollection.Name)]
public sealed class ActionQueueCompletionFaultBridgeTests : IDisposable
{
    private readonly RecordingSink _sink = new();
    private long _lifecycle = 7;

    public ActionQueueCompletionFaultBridgeTests()
    {
        ActionManager.ResetTestState();
        ActionQueueCompletionFaultBridge.Install(_sink, () => _lifecycle);
    }

    public void Dispose()
    {
        ActionQueueCompletionFaultBridge.Reset();
        ActionManager.ResetTestState();
    }

    [Fact]
    public void StructureFaultAfterPendingMutationRepairsExactlyTheOmittedOuterStack()
    {
        var structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        structure.CompleteAction();
        var exception = new NullReferenceException("native audio failure");

        var returned = ActionQueueCompletionFaultBridge.FinishStructure(
            structure, state, exception);

        Assert.Same(exception, returned);
        Assert.Equal(0, structure.GetQueuedQuantity());
        Assert.Equal(0, ActionManager.instance.actionableItems.GetStacks(structure));
        Assert.Equal(1, ActionManager.UnloadCalls);
        var evidence = Assert.Single(_sink.Events);
        Assert.Equal(ActionQueueCompletionKind.Structure, evidence.Kind);
        Assert.Equal(structure.GetGuid(), evidence.ActionableId);
        Assert.Equal(7, evidence.LifecycleBefore);
        Assert.Equal(7, evidence.LifecycleAfter);
        Assert.Equal(1, evidence.StacksBefore);
        Assert.Equal(1, evidence.PendingBefore);
        Assert.Equal(1, evidence.StacksAfterFault);
        Assert.Equal(0, evidence.PendingAfterFault);
        Assert.Equal(0, evidence.StacksAfterRepair);
        Assert.Equal(0, evidence.PendingAfterRepair);
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.RepairedOmittedUnstack,
            evidence.Outcome);
        Assert.Equal(typeof(NullReferenceException).FullName, evidence.ExceptionType);
    }

    [Fact]
    public void UpgradeFaultUsesQueuedMinusOwnedAsItsPendingCount()
    {
        var upgrade = Upgrade(level: 12, queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureUpgrade(upgrade, out var state);
        upgrade.CompleteAction();
        var exception = new InvalidOperationException("post-level native failure");

        var returned = ActionQueueCompletionFaultBridge.FinishUpgrade(
            upgrade, state, exception);

        Assert.Same(exception, returned);
        Assert.Equal(13, upgrade.GetPurchaseLevel());
        Assert.Equal(13, upgrade.GetQueuedPurchaseLevel());
        Assert.Equal(0, ActionManager.instance.actionableItems.GetStacks(upgrade));
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.RepairedOmittedUnstack,
            Assert.Single(_sink.Events).Outcome);
    }

    [Fact]
    public void StructureBulkCompletionAcceptsNativeInternalUnloadsAndRepairsOnlyOne()
    {
        var structure = Structure(queued: 4, stacks: 4);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);

        // Native CompleteQueuedQuantity completed four levels and performed its own `num - 1`
        // unload before the later completion effect threw. Process still owes exactly one unstack.
        structure.queuedQuantity = 0;
        structure.quantity += 4;
        ActionManager.UnloadAction(structure, 3);
        var callsBeforeContainment = ActionManager.UnloadCalls;
        var exception = new NullReferenceException();

        ActionQueueCompletionFaultBridge.FinishStructure(structure, state, exception);

        Assert.Equal(callsBeforeContainment + 1, ActionManager.UnloadCalls);
        Assert.Equal(0, ActionManager.instance.actionableItems.GetStacks(structure));
        var evidence = Assert.Single(_sink.Events);
        Assert.Equal(4, evidence.PendingBefore);
        Assert.Equal(1, evidence.StacksAfterFault);
        Assert.Equal(0, evidence.PendingAfterFault);
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.RepairedOmittedUnstack,
            evidence.Outcome);
    }

    [Fact]
    public void ExceptionBeforePendingMutationDoesNotUnloadAnything()
    {
        var structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        var exception = new InvalidOperationException();

        ActionQueueCompletionFaultBridge.FinishStructure(structure, state, exception);

        Assert.Equal(0, ActionManager.UnloadCalls);
        Assert.Equal(1, ActionManager.instance.actionableItems.GetStacks(structure));
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.PendingCountDidNotDecrease,
            Assert.Single(_sink.Events).Outcome);
    }

    [Fact]
    public void InitiallyContradictoryMembershipNeverGrantsRepairAuthority()
    {
        var structure = Structure(queued: 1, stacks: 2);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        structure.CompleteAction();

        ActionQueueCompletionFaultBridge.FinishStructure(
            structure, state, new InvalidOperationException());

        Assert.Equal(0, ActionManager.UnloadCalls);
        Assert.Equal(2, ActionManager.instance.actionableItems.GetStacks(structure));
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.InitialStateContradictory,
            Assert.Single(_sink.Events).Outcome);
    }

    [Fact]
    public void APostFaultDifferentialOtherThanOneIsNotRepaired()
    {
        var structure = Structure(queued: 2, stacks: 2);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        structure.queuedQuantity = 0;

        ActionQueueCompletionFaultBridge.FinishStructure(
            structure, state, new InvalidOperationException());

        Assert.Equal(0, ActionManager.UnloadCalls);
        Assert.Equal(2, ActionManager.instance.actionableItems.GetStacks(structure));
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.OmittedUnstackNotProven,
            Assert.Single(_sink.Events).Outcome);
    }

    [Fact]
    public void LifecycleReplacementRefusesRepair()
    {
        var structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        structure.CompleteAction();
        _lifecycle++;

        ActionQueueCompletionFaultBridge.FinishStructure(
            structure, state, new InvalidOperationException());

        Assert.Equal(0, ActionManager.UnloadCalls);
        Assert.Equal(1, ActionManager.instance.actionableItems.GetStacks(structure));
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.LifecycleChanged,
            Assert.Single(_sink.Events).Outcome);
    }

    [Fact]
    public void NativeUnloadThrowPreservesTheOriginalExceptionAndObservedAfterState()
    {
        var structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        structure.CompleteAction();
        ActionManager.ThrowAfterUnload = true;
        var exception = new InvalidOperationException("original completion failure");

        var returned = ActionQueueCompletionFaultBridge.FinishStructure(
            structure, state, exception);

        Assert.Same(exception, returned);
        Assert.Equal(0, ActionManager.instance.actionableItems.GetStacks(structure));
        var evidence = Assert.Single(_sink.Events);
        Assert.Equal(ActionQueueCompletionFaultOutcome.NativeUnloadThrew, evidence.Outcome);
        Assert.Equal(0, evidence.StacksAfterRepair);
        Assert.Equal(0, evidence.PendingAfterRepair);
    }

    [Fact]
    public void NativeUnloadNoOpFailsPostconditionWithoutReplacingTheException()
    {
        var structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        structure.CompleteAction();
        ActionManager.SuppressUnload = true;
        var exception = new InvalidOperationException();

        var returned = ActionQueueCompletionFaultBridge.FinishStructure(
            structure, state, exception);

        Assert.Same(exception, returned);
        Assert.Equal(1, ActionManager.instance.actionableItems.GetStacks(structure));
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.RepairPostconditionFailed,
            Assert.Single(_sink.Events).Outcome);
    }

    [Fact]
    public void SuccessfulCompletionAndFatalFailureAreNeverContained()
    {
        var structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var successfulState);
        structure.CompleteAction();

        Assert.Null(ActionQueueCompletionFaultBridge.FinishStructure(
            structure, successfulState, null));
        Assert.Empty(_sink.Events);
        Assert.Equal(1, ActionManager.instance.actionableItems.GetStacks(structure));

        ActionManager.ResetTestState();
        structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var fatalState);
        structure.CompleteAction();
        var fatal = new OutOfMemoryException();

        Assert.Same(
            fatal,
            ActionQueueCompletionFaultBridge.FinishStructure(structure, fatalState, fatal));
        Assert.Equal(0, ActionManager.UnloadCalls);
        Assert.Equal(
            ActionQueueCompletionFaultOutcome.FatalExceptionUncontained,
            Assert.Single(_sink.Events).Outcome);
    }

    [Fact]
    public void ObserverFailureCannotReplaceTheNativeExceptionOrUndoRepair()
    {
        ActionQueueCompletionFaultBridge.Install(new ThrowingSink(), () => _lifecycle);
        var structure = Structure(queued: 1, stacks: 1);
        ActionQueueCompletionFaultBridge.CaptureStructure(structure, out var state);
        structure.CompleteAction();
        var exception = new InvalidOperationException();

        var returned = ActionQueueCompletionFaultBridge.FinishStructure(
            structure, state, exception);

        Assert.Same(exception, returned);
        Assert.Equal(0, ActionManager.instance.actionableItems.GetStacks(structure));
    }

    private static StructureSO Structure(int queued, int stacks)
    {
        var structure = new StructureSO { queuedQuantity = queued };
        ActionManager.LoadAction(structure, stacks);
        return structure;
    }

    private static UpgradeSO Upgrade(int level, int queued, int stacks)
    {
        var upgrade = new UpgradeSO { level = level, queuedLevels = queued };
        ActionManager.LoadAction(upgrade, stacks);
        return upgrade;
    }

    private sealed class RecordingSink : IActionQueueIntegrityEventSink
    {
        internal List<ActionQueueCompletionFaultEvent> Events { get; } = new();

        public void Observe(in ActionQueueCompletionFaultEvent evidence) => Events.Add(evidence);
    }

    private sealed class ThrowingSink : IActionQueueIntegrityEventSink
    {
        public void Observe(in ActionQueueCompletionFaultEvent evidence) =>
            throw new InvalidOperationException("observer failure");
    }
}
