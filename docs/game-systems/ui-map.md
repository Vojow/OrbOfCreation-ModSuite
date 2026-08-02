# UI map

[Back to game systems](README.md)

Orb of Creation looks enormous — seven screens, thirty-odd pages, hundreds of buttons — but it
is assembled from a very small kit of parts. Learn the seven interaction patterns below and
every page you have never opened becomes readable on sight: you already know where the cost
line is, what turns red, and what the button at the bottom will do.

This page describes game version 1.0.5. The screen inventory comes from the game's own screen
data; page contents were read off the running game during a screen-by-screen pass on
2026-08-02. Screens that pass did not open are listed at the end, and anything not directly
observed is marked inline.

## The screens

Seven screens sit on the top bar. Each has its own strip of subtabs directly under the header.

| Screen | Subtabs | What it is for |
|---|---|---|
| Magic | Casting · Spellbook · Augments · Wizardry | Casting spells and everything that shapes a cast |
| Scholar | Concepts · Research · Scribe · Scholar | The knowledge economy: passive scaling, timed research, scribing |
| World | Aspects · Dimensional · Agromancy · Druidry | The physical/natural domains, including harvesting |
| Workshop | Artifacts · Workshop · Artificer · Crafting | Gear and item production |
| Alchemy | Alchemy · Materials · Alchemist | Learned alchemical effects and the materials behind them |
| Rituals | Rituals · Discover · Mysticism | Ritual activation and discovery |
| Time | Reset · Challenges · Time Runes | The run-spanning layer: prestige, challenges, runes |

Two more strips are always present and belong to no single screen:

- **Upgrades | Inventory** — a persistent right-hand panel visible on every screen. Upgrades is
  the one-shot purchase list; Inventory is your carried items. A strip appearing on a screen
  that seems unrelated to it is not a glitch; this pair genuinely follows you everywhere.
- **The casting bar and the bottom level bar** — spell buttons stay reachable while you browse
  other screens, and the global bar along the very bottom of the window is Orb XP, which every
  completed attribute level feeds. See
  [progression-advancements.md](progression-advancements.md).

### Magic

| Page | What lives there |
|---|---|
| Casting | The cast surface plus two global dials: **Output Lv** (observed 51/54) with a **Raise Output Lv** purchase beside it, and **Reserve Lv** (49/49) with **Raise Reserve Lv**. Output Level is global to all casting — there is no per-spell output level. |
| Spellbook › Unlock | The **Spellcraft** compose-and-confirm surface. You place components; the game resolves which spell you unlock. There is no recipe list here. |
| Spellbook › Loadout | The budgeted loadout: a load bar (observed 11/12), per-spell load costs, add-from-library, remove and reorder, and the **Augment Glyphs** panel showing usable copies as "1/1". Glyphs are chosen *before* a spell is added; adding bakes them in. |
| Spellbook › Spells | Per-spell management: spell levels and **Confirm Mastery**, with cost lines and three parallel spell-type XP tracks. |
| Spellbook › Spell Types | The per-type view. Contents not read in detail during the pass. |
| Augments › Glyphcraft | The **Glyphcraft** compose-and-confirm surface for augment glyphs. |
| Augments › Upgrade | Per-glyph levels. Levels raise how many copies of a glyph you may use and how many are free, and carry passive effects even while the glyph is unsocketed. |
| Wizardry | The attribute purchase list for this screen, with a progression counter (observed 160/420). |

The naming here is the game's worst trap; [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md)
untangles it, and [casting-and-spells.md](casting-and-spells.md) covers the dials.

### Scholar

| Page | What lives there |
|---|---|
| Concepts | Concept slots, stacks and their background drain. The game's data also carries a Discover and a Loadout page under Concepts; neither was opened during the pass. See [concepts.md](concepts.md). |
| Research › Expert / Technology / Innovation | Three research trees. Each node is a timed job: point costs per school (red when short), a duration (20.0 s observed), and the output it produces. The five spendable school-point balances sit across the top of the page. |
| Scribe › Manual | A drop slot plus recipes (an "Advancement" recipe observed), and a **Starting Level** dial (4/4). |
| Scribe › Automate | The game's own standing-order page for scribing. Not opened during the pass. |
| Scholar | The attribute purchase list (observed 82/320). |

### World

| Page | What lives there |
|---|---|
| Aspects | Three pedestal slots holding placed aspects. Discovery *offers* are not a standing page here — they arrive as events. |
| Dimensional | A discipline page. Never walked; see [open-questions.md](open-questions.md). |
| Agromancy | Harvesting. Nodes fill over time and are then harvested, with minutes between actions. Note the game only refreshes harvest data **while this page is open** — leaving it closed does not stop the world, but the numbers you last saw are stale. |
| Druidry | A discipline page with its own attributes. |

[world.md](world.md) covers the mechanics.

### Workshop

| Page | What lives there |
|---|---|
| Artifacts › Loadout | A doubly budgeted loadout: **weight** (10/12) and **slots** (4/4). An item whose weight will not fit renders red before you try — the page does its own fit check. |
| Artifacts › Create | Artifact creation. Not opened during the pass; expected to be another compose-and-confirm surface (unverified). |
| Artifacts › Upgrade | Per-artifact levels, priced in two gear currencies (observed 11 and 0). |
| Workshop | An attribute purchase list. |
| Artificer | An attribute purchase list. |
| Crafting › Manual | Seven recipes (Paper among them) with full cost lines; unaffordable costs render red and one row was greyed out entirely. |
| Crafting › Automate | The game's own standing-order crafting page. Not opened during the pass. |

The **Manual | Automate** split appears on both Crafting and Scribe. It is the game's own
separation of one-off crafting from standing orders, and the Manual side works on its own.
See [crafting-and-equipment.md](crafting-and-equipment.md).

### Alchemy

| Page | What lives there |
|---|---|
| Alchemy › Learn | Learning alchemical effects. Expected to be a compose-and-confirm surface (unverified — not opened during the pass). |
| Alchemy › Loadout | A budgeted loadout with **six separate pools** (observed 2/9, 36/40, 0/14, 0/5, 8/8, 8/8) and a per-effect slot cost. The game's data also lists a **Manage** page here that the pass did not open. |
| Materials | The materials behind alchemy. |
| Alchemist | An attribute purchase list (observed 20/260). |

An **Alchemy Lv 43/43** dial with a **Raise Alchemy Level** purchase sits on this screen, the
same shape as Magic's Output Level.

### Rituals

| Page | What lives there |
|---|---|
| Rituals | Ritual activation. |
| Discover | The **Devote** compose-and-confirm surface. |
| Mysticism | An attribute purchase list (observed 40/110) plus a **Stability** meter that visibly decays (4.73e3 at −0.376 %/s). |

### Time

| Page | What lives there |
|---|---|
| Reset | The prestige decision, stated as three facts: your starting Time Advancements next run (83 observed), the delta against your last run (+9), and what survives a reset — Challenges, Achievements, Time Advancements. |
| Challenges › Active / All | A **New Challenges** button that fetches a fresh set, per-row Abandon, activation from the inactive list, and a **Passed** state. Three challenges can be active at once. |
| Time Runes › Create | The **Runecraft** compose-and-confirm surface. |
| Time Runes › Upgrade | Rune levelling, paid in Time Advancements. |
| Time Runes › Archive | Runes you are no longer holding. Not opened during the pass. |

[time-and-prestige.md](time-and-prestige.md) explains what any of it is worth.

## Seven interaction patterns

Every page above is one of these seven, or a purchase list with a dial bolted on.

### 1. Compose-and-confirm discovery

Layout: **cost header, component row, composition area, Confirm.** Four instances are pixel
identical — Spellcraft (Magic › Spellbook › Unlock), Glyphcraft (Magic › Augments ›
Glyphcraft), Devote (Rituals › Discover) and Runecraft (Time › Time Runes › Create). Alchemy ›
Learn and Workshop › Artifacts › Create are almost certainly the same page (unverified).

The direction matters and catches people out: **you compose components and the game resolves
what you get.** You do not pick a target from a list, because no recipe list exists anywhere in
the game. Paying the cost is the commitment; after the roll you must take one of the offered
results. [discovery.md](discovery.md) covers pool pricing, rerolls and the timing rules.

### 2. Budgeted loadouts

Three confirmed: the Spell Loadout (a load bar with per-spell costs), the Alchemy Loadout (six
independent pools with per-effect slot costs) and the Artifact Loadout (weight *and* slots, two
binders at once). The shared grammar: a budget readout as `used/total`, a per-item price
against that budget, and **red for anything you cannot afford or that will not fit** — the
pages preflight the fit themselves, so a red number means "this will not go in", not "this
looks expensive".

Spell loadouts have a second binder that is easy to miss: slots are separate from weight, so a
weight-0 spell still costs you a lane.

### 3. Level dial plus raise-cap purchase

A `Lv n/max` stepper with a purchase next to it that raises the max. Seen on Casting (Output Lv
51/54 with **Raise Output Lv**, red and labelled "Has Requirements" when gated), Casting again
(Reserve Lv 49/49 with **Raise Reserve Lv**), Alchemy (Alchemy Lv 43/43 with **Raise Alchemy
Level**) and Scribe (Starting Level 4/4). Dials are two-way — you can tune them *down* at any
time, which is the standard move when a spell's cost outgrows your income.

### 4. Purchase lists

A scrolling list of levelled attributes with a progression counter in the header (Wizardry
160/420, Scholar 82/320, Alchemist 20/260, Mysticism 40/110). Cost lines turn red when
unaffordable. Every completed level feeds both that screen's progression track and the global
Orb bar — see [attributes-upgrades-development.md](attributes-upgrades-development.md) and
[progression-advancements.md](progression-advancements.md). Some pages add a page-specific
meter to the same layout, such as Mysticism's decaying Stability.

### 5. Manual craft rows

A recipe list where each row carries its full cost line and a craft action, red when
unaffordable and greyed when unavailable. Workshop › Crafting › Manual and Scholar › Scribe ›
Manual are the two instances; Scribe adds a drop slot for the input.

### 6. Timed jobs

Research only, so far. A node states its point cost per school, a duration, and its output
rate; starting it runs a timer and drains a resource for the duration. Your spendable school
points sit at the top of the page, and the costs redden when you are short.

### 7. Mastery confirm

The Spells page's **Confirm Mastery** button: cost lines (red when unaffordable) plus three
parallel spell-type XP tracks that fill from casting, not from spending. This is the
earned-by-doing gate — you cannot buy or save your way past it, only cast.
[mastery-and-xp.md](mastery-and-xp.md) has the tracks and thresholds.

## Vocabulary

The game, its own tooltips, and its internals disagree about names in ways that will bite you
when reading anything written by someone else.

| What you see on screen | What it is called internally | Where you meet it |
|---|---|---|
| Attribute | `StructureSO` — a *structure* | Levelled things on purchase lists: Cauldron, Machinery, Hydro Aura |
| Attribute category | `StructureTypeSO` | Alchemist, Swift, Grove, Workshop |
| Upgrade | `UpgradeSO` | The persistent Upgrades panel; one-shot purchases |
| Statistic | `AttributeSO` | The Statistics panel — never purchasable |
| Research | `ResearchSO` | The Research pages |
| Resource | `ResourceSO` | The resource bar |

The first and fourth rows invert intuition: an on-screen **Attribute** is a structure
internally, and the internal *attribute* type is what the game displays as a **Statistic**.
When someone says "attribute", they almost always mean the purchasable levelled thing.

Three more traps live in ordinary play:

- **Glyph** means two different things. The pool-unlocking kind expands what you can later roll;
  the socketable kind (found under Augments › Glyphcraft) modifies a spell. Both are called
  glyphs in places.
- The game's own tooltips call the pool-unlocking kind **Recipe Books**, while the upgrade
  tooltips for the very same things call them Glyphs. Long-running solo development, several
  renamings.
- **Concepts** (the Scholar feature) are implemented on top of alchemy-named machinery
  internally, and have nothing to do with the **Alchemy** screen, which is a separate feature
  several milestones further along.

Verification status, honestly: the table above is derived from the game's serialized data, not
from reading the live screens. It has never been checked against the running UI, so a label
composed at runtime rather than stored with the screen would not appear here. The
Attribute/Statistic row is the one worth a deliberate look.

## Interaction facts

Small behaviours that are easy to mistake for bugs.

- **The mouse wheel has no owner.** The game's code contains no wheel handler at all; the engine
  delivers the wheel to whatever scrollable element happens to be on top under the cursor. If
  scrolling goes somewhere unexpected, move the pointer, do not hunt for a setting.
- **Tab clicks reselect, they never toggle.** Clicking the screen you are already on re-selects
  it; there is no close gesture and no way to click a screen "off".
- **Top-bar buttons arrive late.** They are built a couple of seconds after the scene loads
  (2–4 s observed, depending on the machine) and the game emits no signal when that finishes. A
  top bar that looks empty right after loading is still starting up.
- **Screens cross-fade.** The incoming page settles a beat after the click, so what is on screen
  during the transition is a blend of both.
- **Tooltips nest, recursively.** Inspect a term inside a tooltip and you get that term's own
  tooltip — attribute type, effect levels, cost scaling, each with its own explanation. There is
  also a modifier-key mode that shows the calculated sums of the effects rather than their
  parts.
- **Tooltips are not a complete model.** They tell you the terms but not always the order of
  operations: how effects chain and fold, and whether multiple levels add or multiply. When the
  displayed number and your arithmetic disagree, the fold order is usually the reason —
  [value-computation.md](value-computation.md) has the real rules.
- **Red and grey mean different things.** Red is "you cannot pay for this / it will not fit";
  grey is "this is not available to you at all".

## Screens the pass did not open

Listed so nobody mistakes silence for absence: Spellbook › Spell Types (contents), Scholar ›
Concepts › Discover and › Loadout, Scribe › Automate, Crafting › Automate, Alchemy › Learn,
Alchemy › Manage, Workshop › Artifacts › Create, Time Runes › Archive, World › Dimensional, and
the Statistics / Achievements / Tips panels. The game's data confirms these screens exist; what
is on them is not recorded here. They are tracked in
[open-questions.md](open-questions.md).
