# Shared gameplay invalidation bus

> **Lifecycle: Implemented for the next beta; interactive validation pending.** The bounded Common bus and its first Automata, Mentor, and Mod Config publishers are portable-tested. This is not released behavior until the next-beta branch completes review and runtime validation.

[Back to plans](README.md) · [Performance architecture](performance-suite.md) · [Runtime validation](../testing/runtime-validation.md)

## Purpose

The suite observes the same gameplay changes through several native hooks. Repeating a complete cache refresh for every callback creates noisy work during purchase and completion bursts. `GameplayInvalidationBus` gives supported plugins one bounded, generation-aware way to report that cached or scheduled work became stale without moving native mutation, XP capture, or lifecycle safety into an asynchronous callback.

The game remains authoritative. A bus event is permission to re-read the affected state later, not evidence that an item is available, affordable, complete, owned, or safe to mutate.

## Event contract

Each event carries:

- one or more change kinds: lifecycle, progression, resource quantity/rate, queue, inventory, registry, or configuration;
- the shared lifecycle generation;
- the Unity frame that produced the burst;
- an optional stable domain, UUID, and expected native type;
- bounded diagnostic source text and a first-publication sequence.

The bus never retains a native or Unity object. Entity events require a stable UUID plus expected native type; an unresolved identity becomes a conservative family-wide event. Names remain diagnostics only.

Events with the same generation, frame, and target coalesce by merging their kind flags. A family-wide event dominates narrower entities in that family without losing their kinds. Different frames remain distinct and preserve first-publication order.

## Delivery and boundedness

Publish and delivery are main-thread-only. `Pump(Time.frameCount, GameplayInvalidationBus.DefaultMaxOperationsPerFrame)` delivers only completed frame bursts, so plugin `Update` order cannot split same-frame coalescing. The shared bus applies one 64-operation cap across every plugin call in a frame and resumes the exact event/subscriber cursor later. Delivery also uses one measured `SuitePerformanceCoordinator` work item and eight-operation cooperative leases, so plugin load order cannot multiply or hide callback time from the shared soft/hard CPU budget.

Subscriber callbacks may only mark owned work dirty or request an existing bounded worker. They must not scan registries, reflect native contracts, sort complete catalogs, log per event, or mutate gameplay. Callback failures are isolated in a 32-entry diagnostic ring.

Pending work is capacity-bounded. When precise capacity is exhausted, the bus replaces it with a conservative global invalidation instead of silently dropping correctness work. Metrics expose publications, coalescing, supersession, overflow promotion, stale discards, delivery operations, callback failures, and off-thread rejections. Publishing alone never executes delivery; disabled Automata, Mentor, and Mod Config owners do not call `Pump`, while another enabled suite owner may drain shared events without waking disabled feature catalogs.

## Lifecycle barrier

The bus observes `GameLifecycleMonitor`. Every accepted transition discards pending and partially delivered work from the old generation and queues one barrier for the newest generation. Late publications stamped with an older generation fail closed.

Plugins still handle lifecycle cancellation synchronously. Auto Buy prepared mutations, Mentor capture/grant leases, and other safety-sensitive work must be cancelled before another native action can start; the one-frame bus path only coordinates secondary cache and scheduling invalidation.

## First adoption slice

- Automata keeps manual Structure/Upgrade queue changes and completion settlement immediate, then mirrors stable typed targets for other consumers. Auto Concept active-list changes publish inventory; discovery/mastery changes publish progression.
- Mentor keeps progression relationship evidence and XP capture synchronous, then mirrors stable spell, artifact, and ordinary-alchemy progression. XP quantities never enter the bus. Spell-loadout membership changes publish broad inventory.
- Mod Config publishes exact successful apply targets only after the full transaction and owner saves succeed. Validation failure or rollback publishes nothing. Its external-change polling remains the compatibility fallback for third-party plugins.
- Resource threshold snapshots remain on Auto Buy's value-carrying direct path; generic invalidation must not discard previous/current quantities or undo threshold parking.

## Verification

Portable tests cover 10,000-event burst coalescing, merged kinds, distinct frames, broad dominance, targeted filtering, FIFO delivery, shared operation and measured CPU budgets, lossless coordinator resumption, multiple matching/nonmatching subscribers, disabled-owner idleness, callback deferral and failure isolation, conservative overflow, main-thread enforcement, lifecycle purging, late-generation rejection, and newest-barrier dominance. Module tests cover immediate native safety paths alongside mirrored delivery and transactional configuration publication.

Interactive validation still must confirm that real completion and configuration bursts remain responsive, the shared pending peak stays bounded, no stale work crosses save/load or NG+ boundaries, and disabled modules do not start catalog work merely because another plugin publishes.
