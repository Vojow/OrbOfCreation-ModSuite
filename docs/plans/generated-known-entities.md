# Generated known-entity identities

> **Lifecycle: Implemented for the next beta; runtime validation remains authoritative.** Issue #29 replaces supported-module UUID literals with a deterministic generated subset.

[Back to plans](README.md) · [Entity mappings](../../data/README.md) · [Typed registry resolver](typed-registry-resolver.md)

## Boundary

`data/entity-mappings.tsv` remains the canonical audited mapping. `data/known-entities.tsv` explicitly selects only the 16 identities currently owned by the shared Alchemy classifier, Auto Concept, spell leveling, and Mentor progression unlock gates. The full reverse-engineered catalog is not a production API.

Each suite-internal `KnownEntity<TContract>` uses a generated suite-owned marker to prevent category mix-ups and carries its stable UUID, expected managed type name, and diagnostic asset name. Consumers resolve the generated type name at runtime before exact registry validation. Generated signatures never embed `Assembly-CSharp` types, so a removed game type fails only the affected feature closed instead of preventing the shared catalog from loading. Names remain diagnostic only.

## Reproducibility and failures

`tools/generate-known-entities.ps1` validates canonical and selected UUIDs, duplicate symbols and UUIDs, identifier syntax, and exact name/type agreement. Normal mode rewrites `KnownEntities.Generated.cs` as UTF-8 without a byte-order mark and LF line endings. `-Verify` makes no changes and fails when the checked-in output is missing or stale.

The Common build runs verification before compilation, so invalid UUIDs, duplicate selection, canonical removal, unexpected type/name drift, or hand-edited generated output stop the build. Portable tests independently compare generated declarations, the explicit selection, and canonical mappings.

Generated metadata never authorizes a mutation. `TypedRegistryResolver` still requires the installed `Assembly-CSharp` contract, exact runtime type, stable native UUID, current lifecycle generation, and any required list-membership evidence.
