# Unified feature health reporting

> **Lifecycle: Next beta / runtime validation pending.** Common defines the status contract and registry; Automata, Mentor, and Mod Config adopt it in the next-beta line. Interactive tooltip and layout validation remains required.

[Back to plan index](README.md) · [Runtime validation](../development/runtime-validation.md)

## Purpose

Players need to tell the difference between a feature they disabled, content they have not unlocked, normal lifecycle waiting, a temporary safety block, an unavailable game contract, partial degradation, and a real fault. These states must come from the same runtime evidence used by each feature rather than from button-local strings or configuration alone.

The game and each owning plugin remain authoritative. Common transports immutable status snapshots and transitions; it does not infer progression or aggregate unrelated feature failures into a synthetic plugin failure.

## State contract

The append-only Common state set is:

| State | Meaning |
|---|---|
| `ConfigurationDisabled` | The saved feature configuration is off. |
| `Locked` | Audited progression evidence says the feature is not unlocked. |
| `NotReady` | Configuration is on, but lifecycle, registry, queue, or initialization state is not ready yet. |
| `Operational` | The feature is configured on and its current runtime contract can operate. |
| `TemporarilyBlocked` | A recoverable runtime condition such as emergency stop, queue pressure, native busy state, or a safety pause prevents work. |
| `ContractUnavailable` | The required native identity, type, accessor, or evidence contract is unavailable. The feature fails closed. |
| `Degraded` | At least one configured capability is operational while another independent capability is unavailable or faulted. |
| `Faulted` | Runtime mutation or invariant evidence shows that the feature cannot safely continue in the current lifecycle. |

Each snapshot carries a stable `(plugin GUID, feature ID)` key, display label, the separate configured-enabled bit, an append-only reason code, optional stable native identity, and lifecycle generation. Presentation wording is diagnostic; condition identity uses the stable fields so wording changes do not create telemetry noise.

## Precedence and aggregation

Feature projection follows this order:

1. saved configuration disabled;
2. parent feature disabled;
3. emergency or explicit safety block;
4. lifecycle or initialization not ready;
5. progression locked;
6. unavailable contract or fault evidence;
7. temporary native condition;
8. operational.

Mentor publishes spell, artifact, and alchemy domains independently. A broken optional domain cannot mark operational siblings as failed. Its root is `Degraded` when at least one configured domain remains operational and another is blocked by contract or fault evidence. The root is `NotReady` when configured domains are only locked or initializing, and becomes globally blocked or faulted only when no configured domain can operate and the evidence warrants that stronger state.

Automata publishes Auto Buy, Auto Cast, Auto Concept, and spell leveling separately. Candidate-level Auto Buy decisions remain in the structured decision channel; only the current feature condition is projected into health, preventing per-candidate status publication.

## Registry and lifecycle

`FeatureStatusRegistry` has one owning main thread. Publishers acquire one disposable registration per stable feature key, update only their own key, and dispose it on plugin teardown. Duplicate owners and cross-thread reads fail closed. Snapshots are deterministic by plugin and feature ID.

Transitions are emitted only when canonical condition evidence changes. Subscriber exceptions are isolated. A lifecycle generation change is itself a transition even if the visible label is unchanged, ensuring consumers cannot retain stale health from another save, reset, NG+, or scene instance.

## User interface

- Gameplay buttons use the shared presentation projection and keep their primary `ON`/`OFF` label and styling tied only to configured user intent. Locking, initialization, emergency blocks, contract failures, degradation, and faults never replace that primary state.
- Tooltips use the same projection and show runtime health plus bounded structured reason evidence rather than private string parsing. Each visible line occupies its own native tooltip node; no node relies on embedded or automatic multiline layout that can overlap later rows.
- Orb Mod Config joins statuses by the exact catalog plugin GUID and displays configured intent and runtime health on separate line-oriented fields in a dedicated runtime band, separate from staged/saved setting feedback.
- Auto Buy formats structured reserve constraints as deterministic per-resource groups with required, available, cost, reserved, and shortfall fields; long resource names and technical detail are bounded and wrapped.
- Saving configuration reports only that configuration was saved. It does not claim that the runtime has already applied the value.
- A plugin that does not publish the contract is shown as not reporting runtime status; Mod Config does not invent one.

## Performance and safety boundaries

- Status projection reads already cached engine, lifecycle, unlock, and failure evidence. It performs no registry discovery, reflection, native mutation, or full candidate scan.
- Publishers update at the plugin lifecycle/tick boundary, never from worker threads or per-candidate loops.
- Disabled modules do not start background work merely to report status.
- Health reporting cannot clear a failure, alter configuration, grant XP, submit a queue action, or bypass final native validation.

## Automated verification

Portable tests cover:

- frozen state and reason numeric identities;
- all required states and contradictory snapshot rejection;
- deterministic snapshots, unique ownership, disposal, main-thread access, and subscriber isolation;
- transition suppression when only wording changes and transition emission when identity or lifecycle generation changes;
- configuration-disabled, unlock, initialization, temporary block, contract failure, domain degradation, fault, recovery, and lifecycle-reset projections;
- exact-GUID Mod Config joins, deterministic feature ordering, and unpublished-plugin behavior;
- controls, tooltips, notices, and Mod Config consuming the same projected state without changing engine scheduling;
- stable primary `ON` intent across rapid operational, waiting, blocked, degraded, unavailable, and fault transitions;
- bounded line-oriented reserve presentation, deterministic resource ordering, large scientific values, long names, and the one-native-node-per-visible-line layout invariant.

Installed-game verification still checks real references and package composition. Runtime UAT must confirm compact labels, tooltip readability, Mod Config layout at supported resolutions, transition recovery after load/reset, and isolation of one deliberately unavailable optional domain.

## Definition of done

- Every supported active feature reports one of the eight states with stable reason evidence.
- Configuration and runtime state are visibly separate.
- A locked or initializing feature is not called failed.
- One unavailable Mentor domain does not mark operational siblings failed.
- Equivalent conditions generate no repeated subscriber or logging work.
- Scene, save-load, reset, NG+, contract failure, and recovery transitions have portable coverage and pass installed-game contracts.
