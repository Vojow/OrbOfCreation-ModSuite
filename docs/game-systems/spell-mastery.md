# Spell mastery

Mastery cannot be bought. It is a per-spell experience track that fills **only by casting**, and it
sits in front of purchases that no amount of saving will unlock. Casting that spell fills its bar;
nothing else does.

Requirement rows read `Req. <spell> mastery <have>/<need>` and appear early, in three recognisable
shapes:

- **Unlock gates** — e.g., Novice Spells needs Gather Knowledge mastery.
- **Per-spell upgrade lines** — mastering a spell is what unlocks that spell's upgrade line at all.
- **Conjunctive gates** — mastery combined with another counter, such as Output Lv.

The planning consequence is that time-to-milestone is not only a resource question: a gated purchase
is reached by **scheduling casts**, and a loadout that cannot cast the gating spell cannot progress
toward the gate at all.

## The readiness threshold

With `masteryReqBase = 600`, a spell-mastery XP scaling of MultiStacking factor 16.62 and a Reduction
of 0.4, the XP needed at mastery level `L` is

```
600 × 16.62^L / (1 + 0.4 × L)
```

E.g., a first level needs 600 XP and the next needs 7.12e3 — roughly a twelvefold step, and it keeps
going.

XP per cast is not flat: the game's tooltip states that XP generated is based on **cost, speed and
level**, which is why a levelled spell feeds its own mastery faster than an unlevelled one and why
cheap spam is a poor mastery engine.

## Confirm Mastery and the spell-type tracks

The Spells page's **Confirm Mastery** action is gated on **three parallel spell-type XP tracks**, one
per type the spell carries, plus a resource cost rendered red when unaffordable. E.g., for a
Primary/Divining/Cantrip spell: 0/300 Primary, 0/200 Divining, 0/500 Cantrip.

**One cast feeds all three**, because the spell carries all three types. The tracks are per type, not
per spell, so casting any Cantrip advances the Cantrip track for every spell that needs it, and a
loadout whose spells share types converges much faster than one built from disjoint tags. Which types
a spell carries can be changed by glyphs; see [spell-types.md](spell-types.md).

## What mastery pays out

**Each mastery level grants +1 Time Advancement on your next run**, which makes casting the main way
run-local activity turns into cross-run capital. See
[time-advancements.md](time-advancements.md).

## Buying mastery velocity

**Spellcraft** is a Wizardry attribute buying **+8.79 % Spell Mastery Rate per level, ×1.04 per
level** — a direct, purchasable knob on how fast every mastery bar fills. It also appears in the
requirement graph in its own right (e.g., `Improved Spell Weight` requires Spellcraft level 2/5).
