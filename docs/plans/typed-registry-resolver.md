# Lifecycle-aware typed registry resolver

> **Lifecycle: Implemented for the next beta; interactive validation pending.** Common owns UUID/type resolution and lifecycle evidence used by Automata, Mentor, and the shared Alchemy classifier.

[Back to plans](README.md) · [Evidence strength](../reverse-engineering/evidence-strength.md) · [Identity and registries](../reverse-engineering/identity-and-registries.md)

## Contract

`TypedRegistryResolver` resolves one non-empty stable UUID through the audited `IdScriptableObject.RuntimeLookup`, requires the exact expected managed type, reads the value's native `GetGuid()`, and stamps the result with the shared lifecycle generation. It never falls back to an object or display name.

Every result records UUID, exact expected type, stable status, evidence level and sources, captured lifecycle generation, current-generation validity, and a diagnostic reason. `RegistryNotReady`, global `NotFound`, and `StaleGeneration` are retryable because registries may initialize or register content later. Wrong type and contradictory UUID/reference evidence fail closed for the lifecycle. Missing audited fields or accessors are contract failures. Retryable results are never cached.

## Membership

`ResolveMember` first resolves the entity and registry by UUID/type, then verifies every scoped-list entry. It returns `Included` or `Excluded` as distinct successful membership outcomes with `NativeRelationship` evidence. Null entries, wrong native types, unreadable UUIDs, or a same-UUID replacement reference cannot prove exclusion and fail closed.

## Adoption

- The shared Alchemy classifier resolves `ConceptRecipes` through Common, carries the resolver generation, and invalidates its ready snapshot if that generation changes.
- Auto Concept resolves `ActiveConcepts` and `ConceptRecipes` through Common and refuses stale ready snapshots.
- spell leveling resolves the exact `UnlockLevelAllSpells` upgrade through Common and refuses a stale capability snapshot.
- Mentor progression gates resolve `MasteriesEnabled` plus each exact domain `ViewSO` through Common while retaining native `IsAvailable()` as the live unlock authority.

Portable tests cover registry readiness, late same-generation registration, missing UUIDs, wrong type despite a matching name, key/UUID contradiction, successful resolution, included/excluded/malformed membership, mid-read lifecycle changes, and same-UUID replacement across generations. The installed-game contract suite pins `RuntimeLookup` and `GetGuid()` metadata.
