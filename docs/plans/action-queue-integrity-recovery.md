# Native action-queue integrity and recovery

> **Lifecycle: Active.** This plan closes the persisted `ActionableListVariable` corruption observed on 2026-07-31 without editing a save or conflating AutoScribe with `ActionManager` capacity.

> **Implemented in the working tree.** Stack-aware WORLD/MCP rows, AutoBuy WORLD and
> main-thread integrity gates, read-only audio admission, exact completion-fault containment,
> detached ticket classification/recovery transactions, an MCP-only explicit operator recovery
> command, contracts, and portable coverage are in
> place. Installation and disposable-save runtime UAT remain open. A post-restart Upgrade mismatch
> is still deliberately non-repairable without an explicit pre-shutdown/operator ticket.

[Back to active plans](README.md) · [Queue/completion evidence](../reverse-engineering/auto-buy-queue-and-completion.md) · [Runtime validation](../testing/runtime-validation.md)

## Evidence to preserve

- Native capacity uses `itemStack.GetTotalStacks()`. The current world row reports unique members through `value.Count`/`GetUsedSpots()`, so it called 35 unique entries a consistent queue while native room proved 131/132 stacked actions.
- `ActionableListVariable.Process(float)` invokes `IActionable.CompleteAction()` and only then calls `Unstack()`. An exception after the actionable decrements its own pending count leaves one excess stack and aborts the remaining parallel lanes for that frame.
- The captured save persists the actionable list and stack record. Reload alone restores the poisoned queue.
- `UpgradeSaveData` persists `level` and `buildTime`, but not `queuedLevels`; an Upgrade stack-versus-pending mismatch first observed after restart is ambiguous and cannot authorize automatic removal.
- At least one contemporaneous `SoundManager.Play` null dereference was captured. The causal link to the Structure/Upgrade completion failure is strong but remains inferred until completion instrumentation records it directly.

## Safety decisions

1. Stable identity is exact UUID plus exact native type. Names are diagnostics only.
2. Background WORLD publishes immutable queue evidence; workers never inspect Unity objects.
3. AutoBuy stops submitting while the native action queue is unknown or contradictory. AutoScribe remains independent because it uses `ActiveScribeInstances`; Spell Leveling also stays independent because its mastery-level mutation does not use `ActionManager`.
4. A recovery GameAction re-resolves and revalidates every native fact on the Unity main thread immediately before mutation.
5. Recovery never calls `CompleteAction`, changes a level/pending count/action time/resource, edits a save, or synthesizes missing work. It may only remove a proven excess `ActionManager` stack.
6. Structure excess can be proven from serialized `queuedQuantity`. Upgrade excess may be repaired automatically only when proven in the same live lifecycle or named by an explicit pre-shutdown recovery ticket. A fresh post-restart differential alone is not enough.
7. Completion exception containment must return the original exception unchanged. The suite records and repairs the omitted outer unstack only when before/after evidence proves exactly that window.
8. Do not globally steal, stop, replace, or suppress one-shot audio. Native callers commonly use
   the `AudioElement` returned by `SoundManager.Play`, so returning null would move the failure into
   those callers. Instead, only identical `UpgradeSO` processing loops share a native element and
   release it after the final native owner finishes. Spell and brewing loops remain native-owned.

## Implementation

### 1. Correct the background-world model

- Add explicit `UniqueMemberCount`, `TotalStacks`, native capacity, remaining room, and structural consistency to the actionable queue row.
- Add ordered `WorldActionQueueMember` rows carrying queue UUID, exact member UUID/type, stack count, native pending count, action time, build speed, and a semantic verdict.
- Treat unknown types, duplicate/empty identity, negative counts, stack shortfall, total mismatch, or unreadable timing as unsafe. Only positive exact excess is repairable.
- Expose these immutable rows through Game MCP as `action-queue-members`; MCP must never re-read the live game for the projection.

### 2. Prevent another persistent poison

- Harmony-prefix/finalize exact `StructureSO.CompleteAction()` and `UpgradeSO.CompleteAction()`.
- Prefix captures lifecycle, exact identity/type, stack, pending, and action time.
- On a non-fatal exception, re-read the same facts. If the action began consistent, pending decreased, and `stack - pending == 1`, call the native unload boundary for exactly one stack, verify the exact deltas, publish a recovery receipt, and rethrow the original exception.
- Any identity, lifecycle, type, count, or postcondition disagreement performs no recovery and quarantines queue mutation.
- Record bounded audio-pool counts with the completion fault so the inferred audio cause becomes directly testable.

### 3. Contain unhealthy admission

- Add a lifecycle-scoped action-queue integrity coordinator holding detached UUID/type/count evidence only.
- The AutoBuy worker emits no purchase plan from an unsafe complete WORLD. The action adapter also checks the coordinator before its live queue-room read so a same-frame completion fault cancels already-planned work.
- AutoScribe and Spell Leveling do not observe this gate because neither mutation uses `ActionManager`.
- Runtime status becomes `TemporarilyBlocked / InvariantViolation`; saved configuration remains enabled.
- Resumption requires a strictly newer complete healthy publication after verified recovery.

### 4. Recover the captured persisted queue

- Add one bounded recovery GameAction that accepts an exact recovery ticket: queue UUID, lifecycle/fingerprint, exact UUID/type, observed stack, observed pending, and excess.
- Re-scan the entire native queue. Reject unknown types, deficits, changed identities/counts, lifecycle replacement, or any mismatch outside the ticket.
- Invoke `ActionManager.UnloadAction(exactMember, exactExcess)` and verify exact member-stack, total-stack, and remaining-room deltas while proving levels, pending counts, action times, and resources did not change.
- The current dump ticket identifies the stale Urn Structure and Upgrade `80bd4392-c08a-42fe-af4c-a60aa5b1887d`. The queued Upgrade `b64e9b9e-6c4b-47b8-be5c-9573c413a77e` is explicitly not part of that ticket.
- Permit one attempt per ticket/lifecycle. A throw or failed postcondition quarantines recovery and requires operator review.

### 5. Contain exhaustion and reduce pressure without changing unrelated sound ownership

- Scope aggregation with the exact private `UpgradeSO.PlayProcessSound()` method. Inside that scope,
  patch the exact `SoundManager.PlayLoop(AudioClip,float)` overload and share only exact clip-reference
  plus exact-volume matches. Every request retains a lease; `AudioElement.FadeOutDestroy(float)`
  releases one lease. The final proven owner bypasses the unreliable delayed fade and calls the exact
  public `AudioElement.Stop()` synchronously, returning only that tracked Upgrade element to the pool.
- When no identical Upgrade loop exists, start it natively only if one returnable allocator element
  remains afterward. At the reserve floor, skip only that new Upgrade processing loop; the exact
  caller stores a nullable handle and its cancel path already treats null as a no-op. Unknown pool
  topology runs native behavior unchanged.
- One-shot `SoundManager.Play`, toggled Spell loops, and Brewing loops are never aggregated or
  suppressed. Lifecycle transitions clear detached aggregation references and counters.
- Keep the AutoItems permanent-item audio preflight, but require two returnable entries so starting
  preparation cannot consume the final completion/progression slot.
- Before an AutoBuy Structure can add work require two returnable entries. Before an Upgrade require
  three: one possible processing loop, one immediate/completion demand, and one true spare.
- Audio refusal is transient, performs zero purchase calls, and waits for a newer world/configuration publication.
- Expose a fixed read-only `game_probe(audio_pool)` result with pool size, current index, idle,
  reusable non-looping, playing-looping, total returnable, aggregation policy, active groups/leases,
  native starts, coalesced requests, reserve suppressions, final stops, and stop failures. Expose
  bounded MCP control for enable, disable, and counter reset; never expose force-stop.

### 6. Correct feature-health projection

- A lifecycle publication with reason code `None` always stores the default empty reason, even if a
  positive projector supplied explanatory text.
- Auto Scribe action health takes precedence over an older planning decision. `QueueFull` therefore
  appears as a temporary native safety block, not the contradictory `EvidenceUnavailable / complete
  evidence` state.
- `EvidenceBlocked` with reason `None` is an invariant violation rather than positive evidence text.
- The planner publishes an explicit evidence-blocked signal; the zero-initialized coverage record is
  never used as a sentinel, so an ordinary covered/idle publication cannot fabricate that invariant.
- Auto Concept's operational projection carries no reason summary.

## Validation loop

1. Baseline: focused AutoItems/AutoBuy portable tests and the complete portable gate with retries disabled.
2. Component: queue unique-versus-stack counts, exact Structure/Upgrade parity, excess/deficit/unknown/duplicate identities, invalid timing, lifecycle reset, and deterministic anomaly ordering.
3. Recovery: exact success, drift rejection, unsupported type, lifecycle change, native throw, postcondition failure, idempotency, and no level/resource/pending mutation.
4. Harmony: normal completion untouched; exception before pending change untouched; exception after exact pending change repairs one omitted stack; bulk Structure/echo and Upgrade cases; original exception identity preserved.
5. Integration: unhealthy queue blocks AutoBuy without changing configuration; AutoScribe and Spell Leveling remain unaffected; strictly newer healthy WORLD resumes purchases.
6. AutoItems/audio: all existing audio-preflight regressions plus the two-slot permanent reserve,
   AutoBuy Structure/Upgrade true-spare thresholds, exact Upgrade/PlayLoop/FadeOutDestroy target
   binding, identical-loop coalescing, synchronous reference-counted final stop, stop-failure
   containment, reserve-floor suppression,
   unknown-topology pass-through, and no global audio-slot stealing.
7. MCP/health/trace: immutable pagination/search, stack sums, exact verdicts, bounded diagnostics, recovery receipt, and append-only result/status codes.
8. Static/installed: manifest source audit, installed metadata contracts for every reflected/Harmony target, real-reference Release and performance builds.
9. Runtime on a disposable copy: inspect the persisted 131-stack state, apply the exact recovery ticket, verify room rises only by the proven excess, observe the queue drain and AutoBuy resume, exercise Structure/Upgrade/Scroll/Relic at 1× and accelerated time, save/reload, and run a combined 15–20 minute trace.

## Acceptance

- No save is edited and no paid or legitimately queued action is discarded.
- Native queue capacity, WORLD, MCP, and UI agree on total stacked occupancy.
- A completion exception cannot cause the same action to complete twice or remain permanently stacked.
- An unknown or contradictory queue stops new purchases but not frames, player control, MCP, or AutoScribe.
- Repeated identical Upgrade processing sounds consume one native loop while all native owners
  retain correct cancellation semantics; unrelated loops and one-shot return contracts are unchanged.
- The captured stale queue is repaired only from its exact ticket, then persists healed through an ordinary native save/reload.
- Focused, full portable, installed-contract, real-reference, and runtime gates all pass on the exact candidate build.
