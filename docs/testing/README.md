# Testing doctrine

[Native contract workflow](native-contracts.md) ·
[Runtime validation protocol](runtime-validation.md)

Tests are the executable specification. Prose earns its place by explaining how
to choose evidence, not by restating test classes or enumerating their cases.
When prose and a test disagree, inspect the product contract and fix the wrong
one; do not preserve both stories.

## Evidence has boundaries

| Evidence | What it can prove | What it cannot prove |
|---|---|---|
| Portable tests against stubs | Policy, state transitions, ServiceCycle journeys, failure containment, and native-adapter behavior for the modeled contract | Installed metadata, Unity wiring, Harmony order, save behavior, layout, or real frame cost |
| Installed-game contracts | The admitted assembly pair and exact native types, members, signatures, visibility, inheritance, and source coverage | That a call has the intended runtime effect or that its postcondition is observable |
| Live runtime evidence | Actual wiring, mutations, persistence, player control, UI behavior, and performance on the recorded build/save/configuration | Broad deterministic regression coverage or behavior outside the observed scenario |

A claim stops at the edge of its evidence. A self-consistent stub that disagrees
with Unity is a defective model, not permission to weaken production behavior.
Installed metadata proves shape, not semantics. Live observation proves one
run, so preserve enough provenance to reproduce it.

The tests themselves show ownership and available scopes. While iterating, use
one generic selector and replace the token with the behavior or contract at
issue: `dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~<scope>"`.

## Design scenarios around authority

- Exercise the production engine through its real seams; a simulator that
  reimplements the decision algorithm can only agree with itself.
- Keep the simulated game authoritative for identity, availability, resources,
  cost, queue room, and mutation acceptance. Recreate native objects on
  lifecycle changes while retaining stable UUID plus expected type.
- Include manual or external interference wherever capacity, ownership, or
  freshness matters. Model exceptions, no-ops, partial results, and ambiguous
  postconditions, not only accepted actions.
- Cross a boundary deliberately: delayed callbacks go through the lifecycle
  kernel, mutation requests carry stable identities, and mixed-feature journeys
  assert uniqueness rather than relying on timing luck.
- Prefer several small journeys plus a bounded stress invariant over one large
  scenario whose failure explains nothing.
- Keep trace fixtures inside the versioned schema. Opaque payload bags, private
  save fields, and free-text log parsing create unreviewable second protocols.

## Turn traces into regressions

A trace is a hypothesis generator, not a test. Preserve the trace or recent-event
dump with the log, exact DLL hash, effective configuration, and audited game
identity. Reconstruct the causal sequence using stable entity identity, action
kind, receipt, planned and settled quantities, lifecycle/world/configuration
generations, and monotonic time.

First encode the smallest evaluator or boundary case that fails for the same
reason. Add a headless journey only when the defect crosses a receipt,
settlement, lifecycle edge, or multiple native actions. Add a deterministic
long-run invariant only when the claim concerns churn, fairness, retry bounds,
or total action count. The regression must fail against a faithful model before
the fix; if the relevant native fact cannot be modeled honestly, keep the claim
in runtime evidence instead of manufacturing certainty.
