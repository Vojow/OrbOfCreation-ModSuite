# Docs maintenance

Every file here is read by future sessions, so each line has recurring cost. The tree is a
compact ledger of end-state truth: what is known now, and where the gaps are.

## Layers

- `game-systems/` — player-facing game facts and math
- `strategy/` — how to play well (opinions and policies)
- `reverse-engineering/` — how an engineer digs into the game's code
- `runtime-architecture/` — how the mod suite is designed
- `testing/` — gates and per-feature risk contracts
- `user-guide/` — player-facing suite documentation
- `development/` — contributor setup, releases, engineering references

## Rules for every change

- State the end state, not the journey: no changelogs, no "no longer/previously", no discovery
  narration, no inline hedging theater. A fact appears once, in the file that owns it; link
  instead of restating.
- Mechanics first: general rules are the content; observations are examples and must read as
  examples. Partial catalogues are labeled partial.
- Progressive disclosure: each folder's README is a one-line-per-file index; files stay short
  and single-topic. Adding a file means adding its index line.
- Adding implies cleaning: when new lines land, the lines they obsolete are removed — here and
  in every file that duplicated them.
- When a gap closes, the answer moves to the owning page and the gap entry is deleted.
- `AGENTS.md` is the canonical guidance file in each folder; its sibling symlink alias exists
  for tooling that expects a different filename. New folders get both.
