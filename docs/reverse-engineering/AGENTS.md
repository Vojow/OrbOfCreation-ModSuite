# reverse-engineering charter

How an engineer digs into the game's code: decompiling workflow, identity and type model,
naming traps, math internals, save format, UI internals, hooks, and native action surfaces.

- Techniques and code-level truths only; player-facing facts belong in `../game-systems/`.
- Cite against the audited build (`audited-build.md`); IL and asset citations are
  baseline-scoped and die with a game update — do not copy them into other layers.
- The game stays authoritative at runtime: findings here describe code, not guarantees.
