# Orb Of Creation ModSuite agent guidance

BepInEx 5 mods for Orb Of Creation. Preserve native progression, action queues,
saves, and player control; fail closed when a game contract is unknown.

## Boundaries

- Never install DLLs into the game, edit an active save, create tags, publish a
  release, or push unless the user explicitly asks for that action.
- Unity objects and game APIs stay on the Unity main thread.
- Identity is stable UUID plus expected native type; names are diagnostics only.
- The game owns identity, structure, and transaction execution. The suite owns
  only audited math and policy. Revalidate mutable native facts at the action
  boundary and verify every mutation's postcondition.
- Scene, save-load, reset, and NG+ transitions invalidate native references.
- One capability has one GameAction; features, tooling, and tests use that same
  mutation boundary.

## Change discipline

- Follow [the engineering doctrine](docs/development/engineering-doctrine.md).
- Keep changes focused and leave unrelated work untouched.
- Update maintained behavior documentation with behavior changes.
- Nested `AGENTS.md` files add rules for their subtrees.

## Verification

- `./script/test` is the complete portable and profile gate, with a hard
  60-second limit per attempt.
- Native-boundary changes also run installed-game contracts against the audited
  game copy, after the portable gate and never in parallel with it.
- Reconcile test, contract, exemption, entity, and compiler-warning counts; an
  unexplained delta is a failure, not a new baseline.
- Report a gate as passing only when it ran against the current working tree.

## References

- Development workflow: `CONTRIBUTING.md`
- Testing and runtime validation: `docs/testing/README.md`
- Native boundary: `docs/runtime-architecture/game-boundary-doctrine.md`
- Runtime architecture: `docs/runtime-architecture/README.md`
