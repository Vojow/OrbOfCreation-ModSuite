# Casting and Spells

Casting is the game's first *active* production loop: resources that have no passive rate at all
(Knowledge and Thaumaturgy, early on) exist only because you cast for them. This page covers what a
spell is, how the loadout binds, and every dial that changes what a cast is worth.

Companion pages: [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md) for the
socketable modifiers, [mastery-and-xp.md](mastery-and-xp.md) for spell levels and the earned-by-doing
tracks.

## What a spell is

Every spell has a **cost**, a **cooldown**, and an **effect list**. The first spell of a run is a good
reference specimen (observed in-game):

| Gather Knowledge, Lv 1 | Value |
|---|---|
| Types | Primary, Divining, Cantrip |
| Cost | 100 mana |
| Cooldown | 7.35 s |
| Effect | +2.01 Knowledge per cast |

Casting is available from the Spellbook and from the hotbar; hotbar/keyboard casting does not require
the Spellbook screen to be open, so casting is never gated behind having the right tab visible. Some
spells open a **target prompt** when cast and resolve only once you pick a target.

Displayed cost is not the amount debited. The cost is assembled in stages — base cost, glyph
augmentation and conversion, per-level scaling, spell/global/type cost modifiers, percentage
multiplication, and finally rounding to **two significant digits** — and the actual spend then divides
that displayed number by the paying resource's **Quality**. The stages are not interchangeable and
cannot be folded into one multiplier; see [value-computation.md](value-computation.md).

## Keywords and the spell-type taxonomy

Spells carry **types** (the game also calls them keywords), and effects target those types. The full
serialized taxonomy is fifteen entries:

> Arcane, Corporeal, Dragon, Druidic, Expansion, Flow, Primary, Psionic, Storm, Alteration, Cantrip,
> Charm, Conjuration, Divination, Evocation.

Two things about that list matter in play.

**Divination is displayed as "Divining".** The tag is `Divination` in the data; every player-facing
surface says Divining. They are the same type.

**The effective type list can be changed by glyphs.** A spell starts from its authored tags, and glyph
setup can add or replace elemental tags before the spell's types are established. Every tag-targeted
buff — anything reading "all Cantrips", "Divining Spell Power", "Psionic power" — resolves against the
spell's *effective* type list at runtime, not against the name printed on the card. If you want to
know whether a buff hits a spell, read the spell's current types, not its display name.

Authored tags for the twenty-one spells present in the examined data:

| Spell | Types |
|---|---|
| Gather Knowledge | Primary, Divination, Cantrip |
| Amass Power | Primary, Expansion, Charm |
| Arcane Aura | Arcane, Primary, Charm |
| Attune Orb | Primary, Psionic, Charm |
| Channel Spark | Storm, Alteration, Cantrip |
| Conjure Life | Druidic, Conjuration, Cantrip |
| Conjure Space | Expansion, Conjuration, Cantrip |
| Construct | Corporeal, Divination, Cantrip |
| Create Spring | Flow, Alteration, Cantrip |
| Dense Expansion | Arcane, Conjuration, Cantrip |
| Expand Magic | Expansion, Divination, Cantrip |
| Industria | Primary, Corporeal, Charm |
| Kinetic Mind | Psionic, Expansion, Charm |
| Meditation | Corporeal, Psionic, Charm |
| Ocular Magnification | Expansion, Psionic, Charm |
| Psychic Blast | Primary, Psionic, Charm |
| Recharge | Primary, Storm, Charm |
| Shape Nature | Druidic, Corporeal, Charm |
| Transfigure | Flow, Alteration, Cantrip |
| Undergrowth | Druidic, Arcane, Charm |
| Whirling Sorcery | Primary, Expansion, Charm |

Those are the authored tags. Which spells you own is run state, and glyph-derived tags are runtime
state, so treat the table as the authored baseline rather than a live inventory.

The broad usage conventions the game follows: Divining spells tend to produce Mental and Magical
resources, Cantrips are the quickfire workhorses, and Charms are temporary buffs you toggle to create
a burst window.

## The effect grammar

Every effect in the game — on spells, attributes, upgrades, runes, glyphs — reads as three parts:

```
<term> <attribute> <keyword-target>
```

- **Term** — how it combines: additive, multiplicative, and the other folding types.
- **Attribute** — what it changes: power, cooldown, cost, capacity, gain, effect level, and many more.
- **Keyword-target** — what it applies to: one named entity, a category keyword (for example "all
  Cantrips" or "all Divining"), or something broad like "all" or "all capped".

Because the target is a keyword, "what helps this spell" is derivable rather than a matter of taste:
enumerate the spell's effective types and read which effects name them. The corollary is that the
value of a buff is a function of your **current loadout** — a Primary-targeted buff that was excellent
when your only spell was Primary is worth much less the moment your workhorse is Expansion instead.

## The loadout: two separate binders

The loadout is bound by **two independent budgets**, and confusing them is the most common modelling
mistake here.

**Spots** are the number of spells you can have loaded at once. **Weight** is a capacity budget spent
by what you load. A weight-0 spell still consumes a spot. An observed loadout sat at 4/4 spots with
weight only 4/6 — completely full and completely unable to accept another spell. A weight-3 spell in
that state is double-blocked: it needs both a free spot and three free weight.

The practical distinction: **spots are parallel cooldown lanes** (throughput), **weight is capacity**
(what you are allowed to carry).

The weight budget is one of the places the game is inconsistent with itself. The early Spellbook
Loadout upgrade presents it as **Spell Power 2/3**; the augment surfaces call the same budget **Spell
Capacity**; the late-game Spell Loadout page renders it as a **load bar** (observed 11/12) with a load
cost printed per spell. The `Improved Spell Weight` upgrade raises it by one.

Spot count is authored: the base is **3**, the **Bandwidth** upgrade adds one, and the **Casting
Mastery** research adds one, for a maximum of **five** active spell slots in the authored data.

Rules that follow from the two binders:

- **Duplicate slotting is legal, and cooldowns are per copy.** Two copies of Gather Knowledge in two
  spots run two independent cooldowns and roughly double both the casts per charm window and the spell
  XP accrual. Four copies of one spell is a legal loadout.
- **You cannot swap a spell while it is on cooldown.** Loadout changes therefore cost cast downtime on
  the lane you are editing; the practical trick is to edit a lane whose cooldown is short and
  predictable.
- **Fitting a new spell may require evicting one** — the game refuses with "No available spell spot".
- **Glyphs are baked in when you add a spell.** The glyph layout is chosen *before* the add, the add
  raises the spell's load cost accordingly, and there is no in-place edit afterwards. See
  [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md).

Owning a spell and loading it are separate decisions with separate budgets — the library holds spells
you are not currently paying weight or a spot for.

## Charms and charm windows

Charms are toggled temporary buffs. They occupy a spot and weight like anything else, and their value
is entirely in the window they open: you toggle the charm, then spend the window casting the spells it
buffs.

Worked example (observed, Whirling Sorcery, a Primary/Expansion Charm):

| Property | Value |
|---|---|
| Cast cost | 200 mana + 2 Thaumaturgy |
| Toggle duration | 30 s |
| Cooldown | 30 s |
| Effect | +43.3 % Cantrip Spell Power |

With a ~1-2 s cast and a 7 s cooldown on the buffed spell, three to four casts fit inside one window;
a measured window produced 12 Knowledge against 8 unbuffed (+50 %). With two copies of the buffed
spell loaded, roughly eight casts fit. A 30 s duration against a 30 s cooldown means the charm can run
at effectively full uptime.

Charms are also the usual target of per-spell upgrade lines: "Improve Whirling Sorcery" *added* a row
to the charm, so it became +46.9 % Cantrip Spell Power **and** ×1.12 Magic Resources Gained while
toggled — see [Spell upgrades edit other spells](#spell-upgrades-edit-other-spells) below.

## Channeled spells

A channeled spell holds the caster for its duration and behaves unlike anything else in the loadout:

- **A channel blocks all other casting.** Casting anything else aborts the channel early.
- **A channel drains on top of its cast cost.** You pay the cast cost to start, then a per-second drain
  for as long as it runs. If you can afford the cast but not the drain, you get a short channel and
  little else — affordability has to be checked against the sustained rate, not the entry price.
- The real cost of a channel is therefore **loadout downtime**, not the mana.

Observed specimen (Channel Spark, Lv 2 Storm/Alteration/Cantrip): weight 1, 39.8 s cooldown, channel
up to 16 s, −110 mana/s while channelling, +5.37 Spark/s. A full channel is roughly 86 Spark for
roughly 2,100 mana all-in.

## Charging

Some spells can be **held to charge**: you trade cast time for power on that cast. The mechanic is
unlocked by the **Charged Spells** research (observed: ×1.40 Cantrip and ×1.10 Charm charge effect).
Because charging costs time and not resources, it is pure profit whenever your loop is limited by
cooldowns rather than by throughput, and pure loss when the reverse is true.

Internally the charge state runs 0.67 s past the spell's maximum charge time before it resolves.

## Output Level — one global dial

**Output Level is a single global dial for all casting. There is no per-spell output level.** It is a
plus/minus stepper on the Casting page (observed as "Output Lv 51/54"), and each level raises all
spell power at a greater spell cost. It is free to move in either direction at any time, which makes
it the one continuously reversible control in the casting system.

Measured across a single step from Output 1 to Output 2 (observed in-game; some attribute purchases
landed between the two readings, so treat the ratios as indicative rather than as the authored curve):

| Reading | Output 1 | Output 2 | Ratio |
|---|---|---|---|
| Gather Knowledge cost | 100 mana | 280 mana | ×2.80 |
| Gather Knowledge yield | 2.23 Knowledge | 5.09 Knowledge | ×2.28 |
| Gather Knowledge cooldown | 7.35 s | 9.19 s | ×1.25 |
| Whirling Sorcery cost | 200 mana | 550 mana | ×2.75 |
| Expand Magic cost | 180 mana | 500 mana | ×2.78 |

So a higher Output Level is **more per cast and worse per mana**, plus a longer cooldown. The dial's
ceiling is raised by the `Raise Output Lv` upgrade (observed requirement: Casting Level 2/2).

### Reserve Level

The Casting page carries a second, structurally identical dial next to Output: **"Reserve Lv 49/49"**
with its own `Raise Reserve Lv` upgrade. **What Reserve Level does is currently unknown** — it has been
observed on screen but never explained, tooltipped, or measured here. Do not assume it mirrors Output.
Tracked in [open-questions.md](open-questions.md).

The pattern of a **level dial plus a raise-the-cap purchase** recurs across the game (Alchemy Lv, the
Scribe's Starting Level); see [ui-map.md](ui-map.md).

## Casting Level

Casting Level is a third progress track, global rather than per-spell, and it is fed by **all** casts.
Its tooltip describes it as proficiency in casting.

- **Casting XP is itself a resource, with a passive +1/s rate.** Idle time feeds the track even with
  no casting at all, and the resource has its own "Maxed in" clock like any other.
- **Per level: ×1.04 Cantrip Spell Power and ×1.004 Charm Spell Power.** Both compound over levels, so
  the track is a slow global multiplier on the primitives rather than a one-off.
- Higher-level spells generate significantly more Casting XP per cast.
- A purchasable upgrade raises the maximum Casting Level, and the level can be tuned down again.

Casting Level appears in the requirement graph in its own right — the `Raise Output Lv` upgrade was
observed requiring Casting Level 2/2. Requirement rows naming "Output Lv" (Scholarism required Gather
Knowledge mastery 1/2 **and** Output Lv 1/2) read against the dial; the two are separate counters that
are easy to conflate, and only the Casting Level requirement has been seen tooltipped.

## Spell upgrades edit other spells

A whole class of upgrades does not buff a number — it **appends a row to another entity's effect
list**. "Improve Whirling Sorcery" turned a single-effect charm into a two-effect charm. Some later
spells are designed to do nothing but this.

The consequence for reading the game: a spell's effect list is run state, not a static property of the
spell. Two saves can have the same spell at the same level with different effects.

## The second loadout (Duplicate Spellbook)

The **Duplicate Spellbook** upgrade gives you a second saved loadout to switch between. Its limits:

- It **does not enable dual-casting**. Only one loadout is live at a time.
- You cannot switch while effects are ongoing.
- **Cooldowns freeze in place while a loadout is unloaded** — they do not tick down in the background,
  so switching away and back does not launder a cooldown.

## Where the numbers come from

Formulas and folding order live in [value-computation.md](value-computation.md); what the produced
resources do lives in [resources.md](resources.md); how you acquire new spells at all lives in
[discovery.md](discovery.md).
