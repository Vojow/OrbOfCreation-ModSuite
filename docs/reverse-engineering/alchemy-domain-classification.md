# Alchemy gameplay-domain classification

[Back to reverse-engineering index](README.md) · [Identity and registries](identity-and-registries.md) · [Entity correlations](entity-correlations.md)

Orb Of Creation implements player-facing Scholar Concepts with the same `AlchemyRecipeSO` and `AlchemyTypeSO` native classes used by ordinary alchemy. Runtime type alone therefore cannot identify the gameplay domain. `OrbModding.Common.AlchemyGameplayDomainClassifier` combines stable identity, exact native type, and the concept-specific registry; asset names are never evidence.

## Audited identities

The UUIDs below come from the canonical [`data/entity-mappings.tsv`](../../data/entity-mappings.tsv) mapping and the concept registry/type relationship audited for Auto Concept.

| Domain | Asset | UUID | Expected native type |
|---|---|---|---|
| Registry | ConceptRecipes | `c8ff8e01-c042-49c2-86a2-e374f82c280c` | `AlchemyRecipeListVariable` |
| Ordinary | Alchemy | `f9c93e42-e9e8-4fe3-a1f3-5aec5430b5c2` | `AlchemyTypeSO` |
| Ordinary | Brewing | `d2947f69-d989-465d-8159-204285ed57be` | `AlchemyTypeSO` |
| Ordinary | Dismantle | `7b89d22c-75ae-4945-9356-833382c9a167` | `AlchemyTypeSO` |
| Ordinary | Enchantment | `2ffcbbc4-49a7-45db-b3ae-4a3c57362255` | `AlchemyTypeSO` |
| Ordinary | Refinement | `32b6b099-19f2-4470-b47b-6c2a8b0388e1` | `AlchemyTypeSO` |
| Ordinary | Transmutation | `b42c6192-7d9b-40d0-aa40-3d46a9348e52` | `AlchemyTypeSO` |
| Scholar | Reductive | `47b787b9-d4cd-43c8-a7e3-63a1e4e0ae94` | `AlchemyTypeSO` |
| Scholar | Reflective | `8f258dcc-c39a-4d64-b915-4239e746c49d` | `AlchemyTypeSO` |
| Scholar | Conceptualization | `69842862-dfce-4a9e-a73b-f757c72e49dc` | `AlchemyTypeSO` |

## Decision matrix

| Exact recipe type and stable UUID | ConceptRecipes snapshot | Audited type UUID | Result |
|---|---|---|---|
| Yes | Member | Scholar | `ScholarConcept` |
| Yes | Not a member | Ordinary | `OrdinaryAlchemy` |
| Yes | Not a member | Scholar | `Unknown` (conflicting evidence) |
| Yes | Member | No Scholar type | Snapshot initialization blocks |
| Missing or wrong | Any | Any | `Unknown` |
| Yes | Unavailable, empty, or invalid | Any | `Unknown`; initialization is retryable or blocked |

Type assets can be classified directly from exact `AlchemyTypeSO` plus one of the nine stable type UUIDs. A UUID not in the audited mapping returns `Unknown` even if its name resembles a known family.

## Lifecycle and performance contract

Call `TryInitialize` outside hot hooks after the runtime registry is ready. Initialization resolves four exact native types, validates the `ConceptRecipes` asset, and enumerates only that scoped registry. It does not call `Resources.FindObjectsOfTypeAll`, enumerate the global `AlchemyRecipeSO.All` catalog, or search assets by name.

The ready classifier caches concept membership, lifecycle-bound native references, decoded type evidence, and later ordinary-recipe results. `ConceptRecipes` resolves through the shared typed registry resolver; every classification carries that resolver's lifecycle generation. `ClassifyRecipe` returns the cached object for repeat calls only while the captured generation remains current. A same-UUID replacement object is rejected until the owner calls `InvalidateLifecycle()` and obtains a fresh snapshot. Owners must still invalidate on scene change, save load, reset, and NG+ transitions. A disabled consumer should not initialize or rebuild the classifier in the background.

`AlchemyDomainClassification` exposes the domain, stable recipe/type UUID evidence, detailed flags, shared [evidence level and sources](evidence-strength.md), lifecycle generation, mutation-grade decision, and diagnostic reason. Auto Concept and Mentor Alchemy require `IsMutationGrade`; `Unknown`, insufficient-source, and contradictory results are ineligible and repeated diagnostics remain rate-limited.

This shared classifier does not decide discovery, availability, completion, mastery eligibility, or mutation authority. Those remain live native-domain checks in each consuming module.
