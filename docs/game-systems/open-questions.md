# Open questions

[Back to game systems](README.md)

The honest list. Everything here is something the rest of these pages either does not say, or
says with a hedge. Each entry states what is actually known, what is missing, and how it could
be settled — either by **playing and observing** (set up the situation, watch the numbers) or by
**reading the game's code and data** (decompile the assembly, read the serialized assets).
Anything that can only be settled by playing is worth doing deliberately, because most of these
gaps have persisted precisely because normal play never isolates them.

When one of these gets answered, the answer belongs in the page that owns the topic, and the
entry here should be deleted rather than annotated.

## Resources and growth

### Reverb Rate versus Replenish Ratio

**Known.** Both appear as terms in the resource Details panel, and both are understood to
protect against the big-number rounding failure where spending your whole stock across many
orders of magnitude rounds the remainder to literal zero instead of a small number. Both are
rare, and neither has ever driven a decision.

**Missing.** Which term does which. No formula, no threshold, no worked example. They may not
even be the same kind of mechanism.

**How to settle.** Read the code — both terms are named fields on the resource model and their
consumers are enumerable. Playing will not separate them; they fire in exactly the situations
where numbers are hardest to read.

### Missing-percent fill

**Known.** The description is that effects which "fill a missing percentage" compute against
**capacity, not current stock**, so multiplying max capacity and then firing a fill produces a
burst — and that during a burst window you want to be as empty as possible. This is described as
one of the largest strategic levers in the game.

**Missing.** Everything else. The effect has never been measured, no source effect has been
named, and it has not been confirmed whether the fill reads effective or base capacity. The
description is unverified, from a single source.

**How to settle.** Both. Find the effects with this term in the game's data first, then set up a
before/after with a known capacity multiplier and read the delta. Until then, treat the whole
mechanic as a hypothesis, not a plan.

### Which resources bear Interest

**Known.** Interest is gain proportional to your current stock, so when it dominates, hoarding
*is* growth and spending stock slows compounding — the reinvestment rule inverts. Exactly one
resource has ever been seen with an Interest Rate: Soul Shards, very late, in a maxed save.

**Missing.** How common interest is, which resources have it, whether it is authored per
resource or granted by upgrades, and at what point in a run it starts mattering.

**How to settle.** Read the data — the resource set is enumerable and the interest term is a
field on it. This is a cheap answer that nobody has gone and fetched.

### Overcap decay outside advancement resources

**Known.** Overcapping is real: a resource can sit above its cap, and a one-shot three-second
timer then pulls it back down. The measured loss rate is `0.85 · (Q − C) + 0.5` per second while
above cap, evaluated on discrete updates. Any nonzero gain or spend that uses the pausing path —
including purchases — resets the timer, which is why "touching" a resource holds an overcap.

**Missing.** Those constants were measured on **advancement** resources only. Whether ordinary
resources share them, or whether the base loss and overflow modifiers differ per resource class,
is not established.

**How to settle.** Read the data for the per-resource loss fields, and confirm with one observed
overcap decay on a normal resource. See [resources.md](resources.md).

### Spark's rubber band

**Known.** Spark is described as the only resource in the game that decays toward zero **even
below capacity**: you gain more the less you have and lose more the more you have, with the
equilibrium starting at zero, and it does not decay while its channel is running (or possibly
only above cap — even that detail is uncertain).

**Missing.** The formula. No coefficients, no equilibrium function, no confirmation of the
channel exemption.

**How to settle.** Read the code. The behaviour is stated in prose only and has never been
extracted.

### The Attribute Cost term

**Known.** "Attribute Cost" is one of the terms enumerated in the resource Details panel,
alongside Rate, Interest Rate, Capacity, Quality, Gained, Reverb Rate, Replenish Ratio and Decay
Ratio.

**Missing.** What it does, when it changes, and whether it is ever a decision. It has never been
explained or weighted.

**How to settle.** Read the data, then check a tooltip in the nested mode on a resource where it
is nonzero.

## Casting, spells and augments

### What Reserve Lv is

**Known.** The Casting page carries two dials side by side. Output Lv is understood: it raises
all spell power at a greater spell cost, and it is global to all casting. Its sibling **Reserve
Lv** (observed 49/49, with its own **Raise Reserve Lv** purchase) sits right next to it in the
same shape.

**Missing.** What it reserves, and what raising or lowering it does. It has never been explained
or experimented with.

**How to settle.** Play — step it down one level and watch what changes on the casting bar and
the resource panel. A tooltip in the calculated-sum mode may answer it outright.

### The Output Level curve

**Known.** One measurement exists, taken at Output 2: a spell went from 100 to 280 mana per cast
(×2.8) for 2.23 → 5.09 output (×2.28), with cooldown 7.35 → 9.19 s. Higher output means more per
cast and worse efficiency.

**Missing.** The per-level cost and power exponents. That single measurement had attribute
purchases interleaved with it, so it is not a clean single-variable reading, and it is one point
on a dial that goes past 50.

**How to settle.** Play, carefully: change nothing else, step the dial one level at a time, and
record cost, output and cooldown at each step. Or read the code, which is faster and exact.

### Per-slot augment data

**Known.** Each spell has its own glyph slots; you own a number of usable copies of each glyph
(the "1/1" reading), upgrades raise that number and grant free usages, and adding a spell to the
loadout bakes its current glyph layout in and raises its load cost. Quick and Heavy are worked
through in detail.

**Missing.** Which augments exist beyond those, what each costs in weight, and how many copies
of each are actually available at a given point. The identity, quantity and weight of what sits
in each slot has never been captured from a live game.

**How to settle.** Read the data for the catalogue; observe live for what a given save actually
holds. See [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md).

### Whether a Momentum build is viable

**Known.** Momentum is an emblem passive: up to 10 tokens, 4 s duration, and per effective stack
+8 % build speed, ×1.08 cantrip cooldown speed and ×1.04 cantrip power, all on one shared
countdown that does not refresh when a stack is added. At around five stacks it rivals the
standard cantrip charm buff.

**Missing.** Whether the build works. Momentum and the charm cannot both be equipped at the
weight caps observed, and the pairing with Kinetic Mind — the spell that would feed it — was
never tested. The comparison has only ever been done on paper.

**How to settle.** Play it. Equip the build for a measured window and compare output against the
charm build over the same window.

### The spell and emblem catalogues are lower bounds

**Known.** 21 spells with their type tags and 24 emblem passives have been enumerated.

**Missing.** Both numbers come from one save's play state, not from the game's full catalogue.
The real counts are higher and unknown.

**How to settle.** Read the data — the full authored sets exist there.

## Discovery

### Offer tables, rarities and multi-resource roll prices

**Known.** Rolls are priced per discovery tree on a ×10 ladder counted only on non-required
picks; rerolls and required picks do not advance the counter; required rolls cost mana; the
number of choices offered and the number of rerolls are themselves stats.

**Missing.** What is actually in each tree, the rarity levels attached to offers, and how
multi-resource roll prices are composed. These were named as follow-up tables and never
produced.

**How to settle.** Read the data — the trees and their contents are authored, which is exactly
why a well-informed player can know what a roll can produce before paying for it. See
[discovery.md](discovery.md).

## Concepts

### The invented-slot quirk

**Known.** In at least one place, buying something when you have zero free slots causes the game
to invent a slot in the UI rather than refuse the purchase — and a later upgrade that grants a
slot then leaves one permanently empty. There are said to be exactly three such places in the
game, and crafting is remembered as one of them. Separately, empty capacity slots are a real,
intentional thing that the game preserves in saves, so an empty row is not evidence of a lost
item.

**Missing.** Which three places. Whether concepts is one of them. Whether the behaviour is the
same quirk in all three or three different ones.

**How to settle.** Play — reproduce it once on crafting and watch the slot count, then check
concepts and the loadouts. Reading the code would identify the capacity-handling paths that can
do this.

## Progression, research and requirements

### Challenge requirement adjustments read inconsistently

**Known.** Challenges do not only change numbers, they adjust **requirements**: an active
challenge applied −5 to a research node's requirements, and the node itself showed a leeway of 5,
exactly as advertised.

**Missing.** The requirement-adjustment value on the same node read as **0** when inspected
directly. Either the display and the underlying value disagree, or there is a second mechanism
producing the leeway. Which one is unresolved, and it matters because it decides whether
challenge modifiers can be reasoned about at all before activating them.

**How to settle.** Read the code for how leeway is computed and where the adjustment is applied.
Confirm live with a second challenge that touches a different node.

### Whether a purchase can cost three or more resources

**Known.** Mixed-currency prices are routine (mana plus knowledge, and so on). In one full
snapshot of priced entities, nothing was priced in more than two resources.

**Missing.** Whether that is a design rule or an artefact of one save's progression. A late-game
entity priced in three currencies would change how costs must be read.

**How to settle.** Read the data across the full authored set, not one save.

### Spell levelling prerequisites are stricter than the button

**Known.** Spell recipes carry levelling prerequisites in their data, but the levelling button
does not consult them — the player-facing check is readiness only.

**Missing.** Whether the prerequisites are dead data, a legacy leftover, or enforced somewhere
else in a way a player can hit. Nobody has been blocked by them in observed play.

**How to settle.** Read the code for other consumers of that field; failing that, find a spell
whose prerequisite is unmet and try to level it.

## Challenges, time and prestige

### The challenge catalogue

**Known.** Challenges are fetched in sets, three can be active at once, they can be abandoned and
passed, they modify the rules of a run including its requirements, and they survive a reset.
Difficulty is chosen deliberately — an easy set makes a run relaxed and is a legitimate input to
other decisions.

**Missing.** What challenges exist, what each does, how difficulty is expressed, what rerolling a
set costs, and what passing one grants. Only the page layout and one challenge's effect have ever
been looked at.

**How to settle.** Read the data for the set; play to confirm the reward shape. See
[time-and-prestige.md](time-and-prestige.md).

## Systems nobody has walked

These are real, shipped features with confirmed presence in the game — several are named
directly by research nodes — that have never been explained, observed in play, or documented
anywhere. There is nothing to hedge here: they are simply unknown.

| System | What is actually known |
|---|---|
| Toxicity | Exists — a research node governs recovering from it. Nothing else. |
| Zeal | Exists — a research node governs recovering it. Nothing else. |
| Rituals (the feature) | The screen has Rituals, Discover and Mysticism pages; Discover is a compose-and-confirm surface; Mysticism is a purchase list with a decaying Stability meter. What a ritual *does* is unknown, as is what Stability gates. A research node extends ritual duration. |
| Alchemy (the feature) | The screen exists with a six-pool loadout, a Materials page and an Alchemy Level dial. What alchemy produces and what the six pools are is unknown. Not to be confused with concepts, which are internally alchemy-named. |
| Artificer, Construction, Mystic, Shaper | Named disciplines with their own progression tracks and advancement grants. Never played. |
| Dimensional | A World subtab. Never opened. |
| Aspects | Three pedestal slots holding placed aspects. What an aspect does, where they come from, and what the slots gate is unknown. |
| Plots | Underlying the harvest domain, with node-level harvest rates and prerequisites. Only the surface behaviour (nodes fill, you harvest them, the page must be open for the data to refresh) is known. |
| Equipment and artifact stats | The loadout binds weight and slots, and upgrading costs two gear currencies. What artifacts actually do is unknown. |
| Consumables | An Inventory panel exists on every screen and carries items with durations and types. Carry limits, use effects and what happens at capacity are undocumented. |
| Emblems | 24 emblem passives exist. Exactly one — Momentum — has been worked out. |

**How to settle.** Play them, in roughly the order a run reaches them, and write the page as you
go. The data will answer "what exists"; only play answers "what it is for".

## Numbers that need re-checking

### Aura effect-level magnitudes

**Known.** "Effect Level" raises an attribute's power per level, and a rough estimate exists for
a stack of +12 aura effect levels on ×1.047–1.05 auras (≈ ×1.7–1.8 output).

**Missing.** That number was never confirmed. Several effect-level purchases landed without a
before/after capture, so their real magnitudes — including the mental-acuity and learning ones —
are unverified estimates, not measurements.

**How to settle.** Play: capture the affected rate before and after a single effect-level
purchase, changing nothing else.

### Early-run numbers are inflated

**Known.** Most observed early-game rates come from a run that started with substantial carried
bonuses applied before anything else, so they are not what a fresh run looks like. Authored
constants (fixed cost curves, thresholds) are unaffected; every *rate* is.

**Missing.** A clean first-run baseline.

**How to settle.** Play a run without carried bonuses, or read the authored base rates directly.

### Everything is pinned to one build

**Known.** All the exact constants across these pages — the overcap rubber band, the XP
thresholds, the discovery price ladder, the augment multipliers, the emblem numbers, the
achievement-strength bonus — were read out of game version 1.0.5.

**Missing.** Any guarantee they survive a game update. A patch that rebalances any of these
silently invalidates the pages that quote them.

**How to settle.** Re-read the constants after any game version change, before trusting a number.

### The vocabulary table has never met the live UI

**Known.** The player-word to internal-name translation in
[ui-map.md](ui-map.md#vocabulary) is derived from the game's serialized data and is internally
well evidenced.

**Missing.** Confirmation against the running game. A screen label composed at runtime rather
than stored with the screen would be absent from the table entirely, and the
Attribute/Statistic inversion is exactly the kind of row worth double-checking on screen.

**How to settle.** Play — open the Statistics panel and one attribute purchase list side by side
and confirm the two words land where the table says.

### Pages nobody has opened

Several screens confirmed to exist have never been looked at, so their contents are absent
rather than summarized: Spell Types, Concepts' Discover and Loadout pages, both Automate pages,
Alchemy's Learn and Manage pages, Artifact Create, the Time Rune Archive, Dimensional, and the
Statistics, Achievements and Tips panels. The list is kept at the end of
[ui-map.md](ui-map.md#screens-the-pass-did-not-open).

**How to settle.** Open them and write down what is there. This is the cheapest item on this
page and it blocks several others.
