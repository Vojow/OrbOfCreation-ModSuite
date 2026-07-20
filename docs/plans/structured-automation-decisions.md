# Structured automation decisions

> **Lifecycle: Implemented for Auto Buy in the next beta; broader adoption and interactive validation pending.** Issue #27 establishes the shared contract and migrates Auto Buy without changing its scheduling or purchase policy.

[Back to plan index](README.md) · [Performance architecture](performance-suite.md) · [Runtime validation](../testing/runtime-validation.md)

## Purpose

Automation outcomes must be understandable without parsing changing English text or depending on another plugin's internal types. Logs, tooltips, tests, telemetry, and future Orb Insights consumers should observe the same stable decision evidence while the game remains authoritative for native state and mutation admission.

## Shared contract

`OrbModding.Common.AutomationDecision` schema version 1 provides:

- append-only, explicitly numbered codes for eligibility, configuration, progression, native contracts, resources, queues, targeting, scheduling, and bounded capacity;
- accepted, rejected, deferred, skipped, dropped, and failed dispositions plus explicit retry triggers;
- stable entity identities using domain, canonical UUID or stable ID, and expected native type; display names remain presentation-only;
- normalized scientific resource values, validated queue facts created from `QueueCapacitySnapshot`, and structured native state codes;
- an immutable canonical resource-constraint collection;
- a condition key for deduplication and an instance key that additionally includes the lifecycle generation;
- one presenter used by logs and tooltips; free-form technical evidence never classifies or deduplicates a decision;
- an exception-isolated process-wide publisher implementing the Common-only observation boundary required by future Insights work.

Condition keys exclude display names, technical wording, observed resource quantities, occurrence counts, lifecycle generation, and live queue occupancy. They include stable policy thresholds, identities and expected types, decision code/disposition, retry contract, native state, and queue policy. A lifecycle generation belongs only to the instance key.

## Auto Buy adoption

Auto Buy no longer owns a parallel rejection-reason enum. Candidate evaluation, reserve and affordability blockers, scan deferral, coordinator admission, native admission, queue waits, disabled state, and successful recommendations now produce Common decisions.

The candidate index reads structured constraints for exact threshold parking. Telemetry counts stable codes and deduplicates condition keys. The engine exposes its latest decision, publishes only condition transitions, and clears publication state at lifecycle invalidation. Verbose logs and the gameplay tooltip render through the same presenter.

This migration does not change candidate order, purchase concurrency, native queue capacity, reserve checks, mutation verification, or the one-native-mutation-per-frame coordinator contract.

## Performance constraints

- Publishing an unchanged condition performs no subscriber call.
- Publishing itself allocates no per event; subscriber-array changes occur only at subscribe or unsubscribe time.
- Decisions without resource blockers allocate no constraint collection.
- Auto Buy transfers one exact normalized blocker array into the immutable Common DTO instead of creating a list, converting it, and cloning it again.
- Formatting remains outside candidate evaluation and is limited to tooltip reads or already rate-limited logs.

## Deferred work

- Auto Cast, Auto Concept, and Mentor attempt-outcome adoption should be separate bounded changes after this contract is reviewed.
- Unified feature capability, waiting, blocked, and unhealthy state belongs to issue #28 rather than overloading attempt decisions.
- Mod Config validation and transaction failures belong to issues #32 and #34; they are not automation decisions.
- Orb Insights UI and history remain planned. It can subscribe through Common without referencing Automata internals.

## Verification

Portable contract tests freeze every public numeric decision code, validate identity and queue normalization, prove constraint immutability and order-independent keys, isolate failing subscribers, and assert code-based Auto Buy telemetry. Existing headless E2E and performance simulations remain authoritative for queue output, candidate handoff, operation counts, and lifecycle behavior.

Runtime UAT should confirm that the Auto Buy tooltip follows disabled, eligible, resource-blocked, native-blocked, and queue-wait transitions; equivalent repeated conditions do not create log noise; and queue saturation behavior remains unchanged.
