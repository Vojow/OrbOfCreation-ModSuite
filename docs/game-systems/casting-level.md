# Casting Level

Casting Level is a global proficiency track, fed by **all** casts.

- **Casting XP is itself a resource with a passive +1/s rate**, so idle time feeds the track with no
  casting at all, and it carries its own "Maxed in" clock like any other resource.
- **Per level: ×1.04 Cantrip Spell Power and ×1.004 Charm Spell Power**, both compounding — a slow
  global multiplier on the primitives rather than a one-off.
- Higher-level spells generate significantly more Casting XP per cast.
- A purchasable upgrade raises the maximum Casting Level, and the level can be tuned down again.

Casting Level appears in the requirement graph in its own right: e.g., `Raise Output Lv` requires
Casting Level 2/2, and Casting Level bumps are what make the next `Raise Output Lv` purchasable.

Requirement rows naming "Output Lv" read the **dial's raised maximum**, not its current setting —
tuning the dial up does not satisfy a gate; buying `Raise Output Lv` does. Casting Level and Output
Lv are separate counters and are easy to conflate.
