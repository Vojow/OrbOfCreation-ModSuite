# Reverse-engineering evidence strength

[Back to reverse-engineering notes](README.md) · [Native contract workflow](../development/native-contract-manifest.md)

`OrbModding.Common.EvidenceAssessment` gives classifiers and resolvers one stable vocabulary for what is known, how it was established, and whether facts conflict. Strength never replaces required sources: an active mutation must meet both its minimum level and every source required by that feature.

## Levels

| Level | Meaning | May authorize active mutation by itself? |
|---|---|---|
| `Unresolved` | Required facts are missing, unknown, or contradictory. | No. |
| `Inferred` | A relationship follows from other evidence but has not been observed or audited directly. | No. |
| `RuntimeObserved` | Exact native runtime type, identity, registry, or relationship evidence was read successfully. | No; audited identity/relationship sources may still be missing. |
| `SerializedAssetVerified` | The relationship or stable identity is verified from the canonical serialized-asset mapping and also observed through the required runtime sources. | Yes, only when the feature's required-source mask is complete. |
| `StaticallyVerified` | The exact managed signature or implementation fact is verified from audited assembly metadata or IL. | Yes, only when it is the right evidence kind and all runtime/identity sources required by the feature are also present. |

`IsContradictory` is independent of level. A contradiction always degrades the effective level to `Unresolved` and fails `Meets(...)`, even if several strong individual facts were observed.

## Sources

The bounded source mask names facts rather than free-form confidence text: `StaticContract`, `SerializedAsset`, `RuntimeNativeType`, `StableIdentity`, `RuntimeRegistry`, and `NativeRelationship`. Display names are deliberately absent; they are diagnostics only and cannot upgrade evidence.

## Active Alchemy mutation policy

An `AlchemyDomainClassification` may drive Auto Concept or Mentor Alchemy mutation only when `IsMutationGrade` is true. That requires:

- level `SerializedAssetVerified` or stronger;
- exact audited managed contract;
- serialized-asset UUID mapping;
- exact runtime recipe/type evidence;
- stable UUID evidence;
- the lifecycle-scoped `ConceptRecipes` registry snapshot; and
- verified membership or verified exclusion from that snapshot.

Contradictory ordinary/Scholar type evidence, a Scholar type outside the concept registry, same-UUID native reference replacement, missing sources, and unknown UUIDs remain unresolved and fail closed. Tests assert the exact level and source mask so a later game or mapping update produces a reviewable contract diff instead of silently changing mutation authority.
