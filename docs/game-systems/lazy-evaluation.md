# Lazy evaluation

A value is recalculated when something reads it, not when its inputs change.

- **Displays lag.** A number nothing is currently reading keeps the answer it last worked out.
  Opening the screen or hovering the entry is what forces the recalculation.
- **Tooltips freeze while open.** A tooltip caches its content when it is built, so a value moving
  underneath will not update until the tooltip is rebuilt.
- **Some screens are the only thing that advances their own data.** Agromancy is the known case; see
  [agromancy.md](agromancy.md).
- **Numbers settle just after a load.** Derived values resolve as things read them over the first
  moments of play.

None of this loses progress: production, queues and timers all run on their own clock. It only
affects what a display shows at a given instant.
