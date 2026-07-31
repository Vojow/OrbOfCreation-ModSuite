# Auto Buy native purchase pipeline

[Reverse-engineering index](README.md) · [Queue and completion](auto-buy-queue-and-completion.md) · [Native contracts](../testing/native-contracts.md)

## Scope and evidence boundary

This document connects the audited game-member inventory to the suite's
active Auto Buy purchase path. It describes three different kinds of evidence and does not
promote one into another:

- **Static native contract:** exact type/member shape in the audited installed
  assemblies, recorded in [`data/native-contracts.json`](../../data/native-contracts.json).
- **Suite implementation:** ordering and fail-closed behavior in the active
  native adapters under `src/AutoBuy/ServiceCycle/Native/`, which are the only
  Auto Buy code that touches the game. Auto Buy has no capture of its own: the
  facts it decides from arrive on the shared world snapshot published by world
  collection.
- **Runtime behavior:** side effects and callback order observed in Unity. Only
  statements explicitly labelled runtime-observed carry this authority.

The manifest proves that the selected members exist with the expected shape.
It does not by itself prove the internal IL order of resource deduction, queue
insertion, echo actions, UI refresh, or completion effects.

The legacy engine and its incremental catalog have been deleted. The sections
below that describe them are kept as a record of the native surface and of how
it was once driven; they are history, not a description of the running code, and
are written in the past tense for that reason.

The active ServiceCycle service decides from the shared world snapshot:
candidates, availability, level, queued state and price all arrive as published
rows, and the cost chain in particular is now computed by `GameCostMath` rather
than asked for. What Auto Buy calls natively is the action boundary —
`CanPurchase()`, `Purchase()`, and `ActionManager.GetRemainingRoom()` — plus a
refusal-diagnostics cold path that runs only after `CanPurchase()` has already
said no.

## Audited native surface

| Concern | Structure contract | Upgrade contract | Evidence |
|---|---|---|---|
| Registry | `StructureSO.All` | `UpgradeSO.All` | Static contract |
| Availability | `IsAvailable()` | `IsAvailable()` | Static contract |
| Native admission | `CanPurchase()` | `CanPurchase()` | Static contract |
| Cost | `GetPurchaseCost()` → `ResourceCostList` | `GetPurchaseCost()` → `ResourceCostList` | Static contract |
| Current level | `GetPurchaseLevel()` | `GetPurchaseLevel()` | Static contract |
| Queued state | `GetQueuedQuantity()` | `GetQueuedPurchaseLevel()` | Static contract |
| Mutation | `Purchase(bool)` | `Purchase()` | Static contract |
| Completion | `CompleteAction()` | `CompleteAction()` | Static contract/Harmony target |
| Queue signal | `QueueBuild(int)` | `Purchase()` | Static contract |
| Finite lifecycle | not used by adapter | `HasFiniteLevels()`, `IsMaxLevel()`, `IsMaxQueuedLevel()` | Static contract |

This table records what the installed assemblies offer, not what the suite calls
each cycle. On the ordinary path only `CanPurchase()` and `Purchase()` are
invoked, at the action boundary; the rest are read once per collection into the
shared world snapshot, and nothing prices through `GetPurchaseCost()` — the suite
owns that arithmetic and publishes `WorldPurchaseCost`.

One cold path adds to that. When `CanPurchase()` refuses, the adapter asks the
game *why*, so a refusal can name a cause instead of being a silent skip:
`IsAvailable()`, `IsMaxLevel()`, `IsMaxQueuedLevel()` and
`GetPurchaseCost().HasEnough()` — the game's own verdict on the price, not a
re-pricing. The same exact cost list is decoded into every resource UUID, cost,
bandwidth flag, and live `GetTrueQuantity()`/`GetMissing()` value. An incomplete
read carries an explicit status rather than a partial list. The manifest carries
these under the owner *Automata Auto Buy refusal diagnostics* at place `action`;
`IsAvailable` remains at place `capture`, since collection already calls it on
every entity of every cycle.

The shared queue contract includes unique members plus stack-aware
`ActionableListVariable.GetTotalStacks()/GetRemainingRoom()/HasRoom()`; capacity
is not `value.Count`. WORLD publishes the detached model, while the action boundary
rechecks exact member UUID/type and stack-versus-pending parity. Upgrade
single-level isolation additionally uses `GlobalVariables.GetMultiBuy()` and
`IntVariable.AsInt()/SetValue(int)`. Structure fallback grouping may read
`Player.GetBulkDevelopment()`.

## What the deleted incremental catalog established

The pre-collection pipeline enumerated both registries itself, sliced the walk, and admitted a
candidate only on a complete contract. Its slicing did not survive — a shared collection pass
replaced per-feature enumeration — but three of its rules did, and they are why the current shape
looks the way it does.

- **Identity is the stable UUID plus the exact audited native type.** A UUID/type contradiction is
  invalid, and a same-UUID native-reference replacement advances the lifecycle epoch rather than
  being treated as the same object.
- **Registry presence is never availability or completion.** Locked content stays visible and can
  become active after progression, so the two questions are asked separately.
- **A partial cost vector never authorizes a purchase.** World collection resolves cost rows the
  same way: a candidate whose vector cannot be fully resolved is skipped rather than guessed at.

The earliest real Unity point at which every registry is complete remains a runtime contract, which
is why lifecycle transitions still invalidate and recollect rather than assuming one permanent
startup snapshot.

## Immediate pre-mutation validation

Every action the worker planned is revalidated on the Unity main thread before it
is allowed to mutate:

1. require a healthy queue in the pinned WORLD, then re-scan exact live native
   member stack-versus-pending parity at the action boundary;
2. require two reusable audio elements for Structure, or three for Upgrade, leaving one true spare
   after immediate/completion demand and the Upgrade processing loop, without changing native audio
   ownership;
3. read live queue room through `ActionManager.GetRemainingRoom()` and subtract
   the configured reserve; the worker does not bound its plan by the queue at all,
   so this read is the only queue authority;
4. resolve the candidate from its stable UUID to the live native object;
5. call `CanPurchase()`, which folds in live requirements and queue admission —
   the two things that can change between planning and acting;
6. call the candidate adapter.

`IsAvailable()` is deliberately *not* read on the admitting path. Availability is
a published snapshot field, so the worker never plans an unavailable candidate,
and re-reading it to admit would pay for a fact already known.

The same is now true of the per-level prerequisites, which used to be the one
admitting term the snapshot could not carry, because the game's own answer takes
the level as an argument. The conditions are published as rows and the worker
evaluates them for the level a purchase would reach — `level + queuedLevels + 1`
for an upgrade, `quantity` for a structure — so a candidate whose next level is
gated never reaches the boundary at all. A condition the suite cannot evaluate
counts as gated. This is what stopped Auto Buy planning `ScribeScroll4` against
an unfinished `ImprovedScribing`. See W58.

## Refusal diagnosis

`CanPurchase()` refusing a purchase the worker planned means one of two things.
If `HasEnough()` is the only refusing term, resource quantities moved after
collection through drain or queue-time spending. That is expected snapshot
staleness: the action records every live row, same-batch resource overlap,
collection-to-admission time, and world-generation delta, then returns a
pre-native skip. Common holds the service behind its world-freshness gate until
a later collection; configuration is untouched.

An availability or level-cap contradiction is structural. If every readable
term passes, the parameterized per-level prerequisite is the remaining term by
elimination. Those cases remain invariant violations: they terminate the batch
and stand Auto Buy down after writing the full diagnostic. None of these cold
reads happen on an admitted purchase.

## Upgrade processing-loop containment

Installed IL shows `UpgradeSO.PlayProcessSound()` starts `customProcessingSound.PlayLoop()` for
queued Upgrade work, and `CancelProcessSound()` fades its stored handle only after that native owner
finishes. Many simultaneous queued Upgrades can therefore pin the fixed audio pool with identical
processing loops. The other loop producers are toggled Spell persistence and the Brewing station;
they have different ownership lifecycles and are not merged here.

The suite establishes a thread-local scope only while the exact private
`UpgradeSO.PlayProcessSound()` body runs. Within that scope, exact clip-reference and exact-volume
matches share one `AudioElement`. Every native request adds a lease. The exact public
`AudioElement.FadeOutDestroy(float)` boundary consumes one lease. Intermediate owners suppress the
native fade; the final proven owner synchronously invokes `AudioElement.Stop()` on that exact tracked
element and returns it to the pool. Runtime evidence showed that the game's delayed fade coroutine
could leave completed Upgrade loops pinned under sustained churn. A new unique Upgrade loop is
refused at the one-slot reserve floor because the
audited Upgrade caller only stores the nullable result and its cancel path accepts null. If the
allocator topology cannot be read, native `PlayLoop` runs unchanged.

This does not patch `SoundManager.Play`, so callers that immediately configure the returned
one-shot handle keep their native contract. It also does not aggregate Spell or Brewing loops,
advance the allocator index, stop unrelated live audio, or retain Unity references across lifecycle
changes.

## Mutation transaction

### Structure

1. Capture `GetQueuedQuantity()`.
2. Invoke `StructureSO.Purchase(true)`, once per requested level, re-reading
   `CanPurchase()` before each level past the first.
3. Capture `GetQueuedQuantity()` again.
4. Accept an exact delta of `+1` for a single-level request, and a delta in
   `[1, count]` for a group.

`Purchase(true)` forces exactly one level and consults no multiplier, so a bulk
structure buy is the same call repeated inside one verifier scope — which is what
the Bulk Development grouping mode asks for. A group that stops early because the
game stopped admitting is a partial success, not a refusal. The Boolean argument
shape and exact queued-state methods are statically verified. The meaning of the
`true` argument and the internal native order of resource spending versus
`QueueBuild` are not asserted here without a reviewed IL/runtime observation.

### Upgrade

1. Resolve and read the global multi-buy variable.
2. Set it to the requested level count and verify the readback.
3. Capture `GetQueuedPurchaseLevel()`.
4. Invoke `UpgradeSO.Purchase()`.
5. Capture `GetQueuedPurchaseLevel()` again.
6. Accept a delta in `[1, count]`. `Purchase()` honours the multiplier but the
   game may afford fewer levels than asked for, so any committed level is a
   success and only a zero delta is a miss.
7. Restore the original global multi-buy value and verify restoration on every
   exit path.

If multi-buy entry or restoration cannot be verified, no mutation is attempted
and Upgrade purchasing is quarantined. Structure purchasing is independent of
that global quarantine.

## Mutation outcomes

`NativeMutationVerifier` distinguishes the observable boundary, not the game's
internal intent:

| Outcome | Native invocation known to have started? | Safe interpretation |
|---|---:|---|
| Before capture failed | No | No mutation authority was obtained |
| Execution threw | Yes | Ambiguous even when an after-state can be read |
| After capture failed | Yes | Ambiguous |
| Postcondition failed | No exception, but no queued delta | Benign skip |
| Verified | Yes | The expected queued delta was observed |

A call that threw is ambiguous and blocks that candidate until a newer lifecycle.
A call that completed cleanly and simply moved nothing is a benign skip, not a
fault: the batch advances to its next action. Reserving the fault classification
for real exceptions is deliberate — around a fifth of attempts in observed play
are zero-delta misses, and treating each as a fault discarded the rest of the
batch.

A definite structural rejection before any native call terminates the batch.
A price-only admission refusal is instead a pre-native skip and requires a fresh
world before re-planning. An ambiguous mutation remains blocked until lifecycle
recovery.

## What remains unknown

- exact native IL order of queue insertion, resource deduction, notifications,
  and `QueueBuild`/`Purchase` callbacks;
- which native failures can throw after a partial side effect in the audited
  game build;
- whether all completion/echo paths produce the same Harmony callback order;
- whether queue capacity can change outside the currently observed progression
  paths;
- exact availability/registry populations at named player progression stages.

These unknowns require an installed-assembly IL audit or sanitized runtime
observation before they can strengthen simulation authority.
