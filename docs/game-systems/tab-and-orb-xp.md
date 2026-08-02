# Tab XP and Orb XP

**Every completed attribute level grants `+1` XP to its tab's progression track and `+1` Orb XP.**

The word *completed* is load-bearing. Queueing the level does not pay; paying for it does not pay;
the level has to finish developing in the queue. A purchase that develops five levels grants five of
each. Orb XP accrues from **every** tab — there is no attribute that pays its tab but not the orb
bar.

## Thresholds and rollover

| Track | Threshold at level `L` | Sequence |
|---|---|---|
| Tab progression | `40 + 10·L` | 40, 50, 60, 70, … |
| Orb XP | `50 + 5·L` | 50, 55, 60, 65, … |

XP beyond a threshold **rolls over** into the next level, one grant can cross more than one
threshold, and nothing is lost at a boundary.

Reading the display takes care, because the number shown is sometimes the next threshold and
sometimes the remainder: `Wizardry Lv3 in 60` is the level-3 threshold with two prior levels already
applied, while `Scholar Lv1 in 21` is 21 XP remaining out of the 40-point first threshold. Compare
against the table.

## Which attribute feeds which tab

An attribute's *category* decides the tab it pays into.

| Attribute category | Progression tab |
|---|---|
| Alchemist | Alchemy |
| Arcanist, Flameweaver, Stormshaper, Wizardry | Wizardry |
| Artificer, Reinforced | Artificer |
| Dimensional | Shaper |
| Druidry | Druid |
| Mystic | Mystic |
| Scholar | Scholar |
| Workshop | Construction |

Purchase lists carry that tab's counter in the header — e.g., `Wizardry 160/420` on one observed
save.
