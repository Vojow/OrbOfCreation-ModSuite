# Shared queue-capacity snapshots

> **Lifecycle: Implemented / runtime validation pending.** The shared calculation and Auto Buy adoption are portable-tested; the installed field/method contracts are statically tested, while an interactive capacity-change probe remains.

[Back to plans](README.md) · [Orb Automata plan](automata.md) · [Runtime validation](../development/runtime-validation.md)

## Purpose

Queued automation must not treat native total capacity, live occupancy, native remaining room, an automation usage limit, and slots reserved for manual play as interchangeable values. `OrbModding.Common.QueueCapacitySnapshot` is the reusable fail-closed boundary for Auto Buy and future harvest, crafting, scribing, or alchemy adapters.

## Contract and calculation

Every snapshot records six separate values:

- `NativeCapacity` comes from the queue's authoritative native total-capacity contract.
- `NativeRemainingRoom` comes from the authoritative native live-room contract.
- `LiveOccupancy` is derived once as `NativeCapacity - NativeRemainingRoom`.
- `AutomationUsageLimit` is supplied by the owning automation policy.
- `ManualReservation` is supplied by the player-facing reservation policy.
- `UsableAutomationRoom` is derived once as `min(AutomationUsageLimit, max(0, NativeRemainingRoom - ManualReservation))`.

Each property exposes native, policy, or derived provenance. Negative inputs and native remaining room greater than native capacity are contradictory and reject the whole snapshot. A reservation larger than the current queue is valid policy state and produces zero usable room.

## Auto Buy native adapter

The supported game build exposes total capacity through the exact public field chain `ActionManager.instance.actionableItems.maxQueuedItems`, read with the validated `IntVariable.AsInt()` contract. Live remaining room comes independently from `ActionManager.GetRemainingRoom()`.

Auto Buy creates a complete snapshot when it probes a waiting queue, before scanning, before selecting a repeat limit, and again after live candidate/cost/reserve validation immediately before each native purchase call. The manual reservation and remaining batch usage allocation are passed into the snapshot once; callers do not repeat queue arithmetic. Invalid, missing, or contradictory reads perform no mutation.

The ranked scheduler remains unchanged: multiple prepared recommendations receive one independently validated level per pass, while a lone recommendation may use the snapshot's full usable room.

## Validation still required

On a disposable save, change native queue capacity while Auto Buy has prepared work and confirm the next queued level uses the refreshed capacity and occupancy. Repeat with a one-slot queue both with `LeaveQueueSlots=1` (no automated mutation) and `LeaveQueueSlots=0` (one independently validated level). Confirm manual reservation remains free and no contradictory snapshot produces a purchase.
