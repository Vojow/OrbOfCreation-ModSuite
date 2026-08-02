# game-systems charter

Player-facing facts and math about Orb of Creation, one mechanic per file, constants from game
v1.0.5. What a player could learn from the game — never suite internals, never code identifiers,
never play advice.

- README.md is the one-line index; read it first, then only the files the task needs.
- Describe a mechanic as the player meets it — UI vocabulary first. Where the game's internal
  model differs from the UI story (a spendable point that is really a capacity allocation), that
  difference is signal: state it in a single labeled **Code shape:** note, never blended into the
  player-facing text.
- Play advice belongs in `../strategy/`, code-level material in `../reverse-engineering/`.
- Numbers from a single save are observations — label them as such; only authored constants are
  stated as rules.
- Unknowns are single entries in `open-questions.md`; when one is answered, the answer lands on
  the owning page and the entry is deleted.
