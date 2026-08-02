# Reverse engineering Orb of Creation

[Back to documentation](../README.md)

How an engineer digs into this game's code: the tools, the shapes the assembly repeats, the
places its names mislead, and the action surfaces already decompiled. A techniques manual — it
exists so nobody pays the decompile cost for the same finding twice.

Three folders divide the subject:

| Folder | Question it answers |
|---|---|
| this one | how the game is built, and how to find out more |
| [`game-systems/`](../game-systems/README.md) | what the game does, in the player's terms |
| [`runtime-architecture/`](../runtime-architecture/README.md) | how the suite decides to act on it |

A fact a player could observe belongs in `game-systems/`. A fact that costs IL, serialized
assets, or a live registry read belongs here.

## Index

- [Audited build](audited-build.md) — the pinned baseline, its assembly hashes, and how to move it.
- [Decompiling workflow](decompiling-workflow.md) — the tools, the order to use them in, and how to cite what you find.
- [Architecture](architecture.md) — the lifecycle and manager loops every gameplay object is driven by.
- [Identity and registries](identity-and-registries.md) — UUID is identity; type is the boundary; a name is never either.
- [Type model](type-model.md) — the concrete/type/registry/modifier shape every domain repeats, and where a bonus can double-apply.
- [Naming traps](naming-traps.md) — the places where the code's word and the player's word mean different things.
- [Resources and BigDouble](resources-and-bigdouble.md) — big numbers, the resource API, and the cost math you have to reproduce exactly.
- [Save system](save-system.md) — the save format, the collection pipeline, and how to read one by hand.
- [UI internals](ui-internals.md) — lazy construction, latching, UI-only paths, and the quirks that look like bugs.
- [Modding hooks](modding-hooks.md) — where to attach, what not to touch, and how to prove a mutation landed.
- [Requirements](requirements.md) — the requirement graph: what gates actually read, node visibility states, and walking a chain to the real blocker.
- [Native action surfaces](native-action-surfaces.md) — the purchase, consumable and crafting paths, already decompiled.
