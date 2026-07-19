# Automation admission adapters

Lifecycle: next beta / runtime validation pending.

## Goal

Keep shared automation policy independent of arbitrary game-domain methods. The game remains authoritative for identity, availability, native readiness, costs and drains, queue requirements, mutation, and postconditions; adapters translate those facts into a small fail-closed boundary.

## Implemented boundary

`AutomationAdmissionSnapshot` is an immutable value containing only:

- the action family and stable UUID plus expected native type;
- known/unknown availability and native-admission facts;
- decoded immediate costs and progressive drains;
- the number of native queue slots required.

`AutomationAdmissionPolicy` accepts only a complete normalized snapshot. Unknown identity, availability, native admission, immediate cost, drain cost, or queue requirements reject the action. It does not reflect game objects or invoke domain methods.

The supported runtime adapters are deliberately family-specific:

| Family | Native adapter | Mutation/postcondition |
|---|---|---|
| Structure purchase | `ReflectionStructurePurchaseAdapter` | Exact `Purchase(bool)` contract and queued-quantity delta `+1`. |
| Upgrade purchase | `ReflectionUpgradePurchaseAdapter` | Exact `Purchase()` contract, scoped native multi-buy value of one, and queued-level delta `+1`. |
| Spell cast | `ReflectionAutoCastCandidate` through `AutoCastAdmissionAdapter` | Spell identity/readiness/cost/drain/target preflight and exact native Fire-hook epoch delta `+1`. |
| Concept assignment | `ReflectionConceptRuntime` | Exact recipe/instance identity, projected drain admission, quantity mutation, and exact queued delta. |
| Spell level purchase | `ReflectionSpellLevelRuntime` | Exact recipe identity/readiness/cost plus native single/all mutation and mastery postcondition. |

Structure and Upgrade no longer share conditional reflection for their lifecycle, queue, and purchase contracts. Auto Buy and Auto Cast normalize their family-specific facts before reserve, fullness, prioritization, and scheduling policy uses them. Every native mutation retains its immediate live revalidation and capture-execute-capture verification.

The Common action-family vocabulary reserves `HarvestAction` and `ScrollConsumption`. Future Auto Harvest or scroll-use work must add an audited family adapter and complete normalized admission facts; registry presence or a display name is never enough.

## Cost decoding and game-version gate

Spell cost decoding is all-or-nothing. Every bounded entry must provide a stable resource UUID, numeric amount, and readable live quantity. Duplicate UUIDs must reference the same native resource object. One malformed or contradictory entry rejects the whole vector; an empty decoded native list remains a valid free action.

Automata now treats installed game-assembly hash mismatch as a mutation contract failure. It publishes contract-unavailable feature health and does not install Harmony patches, create native automation runtimes, or acquire action-family ownership. A warning alone is not permission to mutate an unaudited game build.

## Verification

Portable tests cover cross-adapter policy equivalence, fail-closed unknown facts, mixed valid/malformed cost vectors, assembly mismatch gating, and the existing lifecycle, queue, reserve, and mutation-postcondition behavior. Installed-game contract tests and real-reference builds confirm exact signatures. Interactive validation remains required for normal automation and the visible unavailable-contract state on an intentionally unaudited build.
