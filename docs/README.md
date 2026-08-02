# Documentation

Four knowledge layers and the practical pages. One line each; the link is that folder's own index.

## Knowledge layers

- [Game systems](game-systems/README.md) — how Orb of Creation itself works, version 1.0.5. Facts
  only. Read before reasoning about any in-game number.
- [Strategy](strategy/README.md) — how to play it well: per-resource policies, run planning, the
  advisor program. Opinion, built on game systems.
- [Reverse engineering](reverse-engineering/README.md) — how to dig into the game's assemblies and
  what the digging established. Read before touching a native member.
- [Runtime architecture](runtime-architecture/README.md) — how the suite is built: three
  publications, two service shapes, where the game may be touched. Read before changing runtime code.

## Practical pages

- [User guide](user-guide/installation.md) — install, configure, troubleshoot, uninstall. What
  players are told.
- [Testing](testing/README.md) — evidence layers, test selection, and the ordered V0–V7 runtime
  gates. Read before calling anything green.
- [Engineering doctrine](development/engineering-doctrine.md) — the review rules, each earned by a
  concrete defect. Read before arguing a review point.
- [Development setup](development/setup.md) — build, portable tests, and an authorized local install.
- [MCP tools](development/mcp-tools.md) — the performance-debug game MCP reference.
- [Contributing](../CONTRIBUTING.md) — the contributor workflow.
- [Releasing](releasing.md) — the owner's tag-and-publish procedure and the review checklist before it.
- [The north star](north-star.md) — the goal every change serves. When a change conflicts with it,
  say which one is wrong.

Released behavior is documented beside the code it describes: `src/README.md` for the layout, then
the per-feature `src/Automata`, `src/AutoItems`, `src/AutoScribe`, `src/Mentor`, and `src/ModConfig`
READMEs. This tree explains the game, the strategy, the design, and the process — never what a
shipped feature currently does.
