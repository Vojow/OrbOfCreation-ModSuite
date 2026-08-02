# runtime-architecture charter

How the mod suite is designed: boundaries, services, world collection, observability.
`game-boundary-doctrine.md` is the rule-set; everything else describes the system as it is.

- Describe what IS, not decision history. A decision record earns an entry only while source
  code cites its number; when the citation dies, so does the entry.
- Keep the service roster synced with `src/` — every shipped service gets its rows the change
  that ships it.
- Each rule is specified in exactly one file; other files point at it.
