# The requirement graph

## Displayed level is a three-term sum

```
displayed level = purchased level + base levels + bonus levels
```

**Requirement checks do not use the displayed term.** They evaluate purchased and base levels only,
so a node showing a healthy green `+5` while its purchased level is `0` **fails** a requirement of
`≥ 5`, and the interface gives no hint that the number it shows is not the number being tested. Bonus
levels are power, not progress.

Bonus levels also **do not advance the paid cost curve**: a node at purchased level 2 with 2 bonus
levels costs what level 2 alone costs.

## Visibility and availability are separate

A node is **hidden** until its prerequisite tier and its gating levels are met. It then becomes
visible but possibly still unavailable, and only later purchasable. So "I can't find it" is a data
question rather than an interface bug, and seeing a node does not mean you can act on it.

Requirements can also be **per level**: the same node can demand different things at level 3 than it
did at level 1.

## Requirements reach across systems

A requirement is not restricted to the system its node lives in. E.g., *Arcanism II* costs only
`10,000` Arcanum but requires the *Arcane Dominion* **research** to have at least one level — the
research merely being available at level 0 is not enough. Requirements also routinely demand
**possession of a resource** you do not yet produce, and some are **hard gates** on a specific level
count rather than soft scaling conditions.

## A worked chain

```
Improved Scribing            — hidden
├── requires Technology tier              — met
└── requires Scribism >= 1                — NOT met (level 0)
    ├── Scribism requires Improved Concepts >= 1   — met
    └── Scribism requires possession of Ink        — NOT met (0 Ink)
        └── Ink is produced by Refine Ink
            └── Refine Ink is hidden until Research Scribing >= 1
                └── Research Scribing requires:
                    ├── Innovation tier
                    ├── Research Electric >= 1
                    ├── possession of Elementia     — NOT met (0)
                    └── hard gate Expert Items >= 5 — NOT met
```

The last line is the trap: a visible `Expert Items (+5)` bonus still fails the `>= 5` gate, because
purchased level is 0. Working backwards from a wanted node is the only reliable way to find the real
blocker.

## Challenges modify requirements

Challenges can modify requirements themselves, applied as passive modifiers — e.g., a challenge
applying `-5` to a research node's requirements, which the node displays as `leeway 5`. See
[challenges.md](challenges.md).
