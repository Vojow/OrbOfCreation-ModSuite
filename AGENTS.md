# Orb Of Creation ModSuite agent guidance

BepInEx 5 mods for Orb Of Creation. Preserve native game progression, action
queues, saves, and player control. Fail closed when a game contract is unknown.

## Hard rules

- Never install DLLs into the game, create tags, publish releases, or push
  unless the user explicitly asks for that action.
- `OrbChronomancer` and `OrbAchievementResonance` are experimental and must
  never enter supported builds or packages.
- Nested `AGENTS.md` files apply to their subtrees (`src/`, `tools/`).

## Runtime invariants

- Unity objects and game APIs stay on the Unity main thread.
- Never edit an active save file.
- Identity is stable UUID plus expected native type; names are diagnostics only.
- The game stays authoritative for availability, cost, quantity, queue room,
  and completion; revalidate native state immediately before mutating it.
- Scene, save-load, reset, and NG+ transitions invalidate cached native
  references.

## Verification

- `./script/test` runs the complete portable gate (hard 60-second limit).
- `dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true`
  runs the portable tests directly.
- Only report a test as passing if it ran against the current working tree.

## House rules

- Keep pre-existing and unrelated changes out of your task.
- Update behavior documentation alongside behavior changes. Change project
  versions only when the task calls for it.

## Reference docs (read when relevant, not before every task)

- Plans and lifecycle status: `docs/plans/README.md`
- Testing hub and runtime validation gates: `docs/testing/README.md`
- Runtime architecture: `docs/runtime-architecture/README.md`
