# Economy mechanics

[Back to reverse-engineering notes](README.md)

This reference describes the economy implemented by the audited Orb of Creation
v1.0.5-2 managed build. It separates authored values from runtime calculations because
the strategist must value the result the game computes, not a tooltip approximation.
The assembly evidence below names metadata tokens and IL offsets in
`Assembly-CSharp.dll`; the serialized evidence names lines in a read-only `game_data.json`
dump extracted from the installed game's serialized data. The audited assembly and
its SHA-256 are recorded in [the audit](audit.md#audited-build).

## 1. Progression levels increase advancement capacity

A progression resource does not grant one unit of an advancement resource when it
levels. Its `perLevelEffects` add one to selected advancement resources' *maximum
quantity*. `ResourceSO.CheckIfLeveledUp` increments the progression level, subtracts the
threshold, and applies the level effects; `ApplyMaxQuantityFromLevels` folds the
resulting level modifier into each target's maximum
(`ResourceSO::CheckIfLeveledUp`, token `0x06001292`, `IL_001D`–`IL_0048`;
`ResourceSO::ApplyMaxQuantityFromLevels`, token `0x06001281`,
`IL_000A`–`IL_0032`). Thus a Wizardry level while Glyph Upgrades reads `2/2` produces
`2/3`: nothing is wasted and the current quantity is not overcapped.

The progression-level grant table is:

| Progression | Advancement capacity added per level |
|---|---|
| Wizardry | +1 Magical, +1 Glyph |
| Scholar | +1 Cognitive, +1 Technology |
| Alchemy | +1 Cognitive, +1 Materials |
| Artificer | +1 Ability, +1 Equipment |
| Construction | +1 Technology, +1 Equipment |
| Druid | +1 Ability, +1 Magical |
| Mystic | +1 Technology, +1 Materials |
| Shaper | +1 Technology, +1 Glyph |
| Orb | +1 Orb |

The serialized definitions prove the Scholar, Shaper, and Wizardry cases directly
(`game_data.json:189141`–`189207`, `189357`–`189423`,
`189573`–`189639`); the other progression definitions occupy
`187526`–`189356`.

Advancement caps can also be raised by authored effects outside this table. In this
dump, Magical capacity is raised by Wizardry, Druid, Magical-resource-type levels,
Perfect Auras, and other effects targeting its maximum; Cognitive by Scholar, Alchemy,
Parchment-resource-type levels, Perfect Learning, and Cognitating; Technology by
Scholar, Construction, Mystic, Shaper, Spacial-resource-type levels, Perfect
Laboratory, Technology Tycoon, Bending, Technology Mastery (+2), and the Ability,
Cognitive, and Magical conversion effects (+1 each); Glyph by Wizardry, Shaper, and
Boost Glyphs (+50%). These are modifier providers, so the effective cap is their folded
result rather than a stored integer. The four relevant resource definitions begin at
`game_data.json:182925`, `183428`, `183766`, and `184442`.

## 2. Pool-roll prices count non-required picks per discovery tree

The price index is the current discovery tree's `poolDiscoveredCount`, not a global
counter. `GetNextPoolItemCost` passes that count to `GetPoolItemCostAt`
(`DiscoveryTreeSO::GetNextPoolItemCost`, token `0x06000AB9`,
`IL_0000`–`IL_000C`). `DiscoverItem` increments it only when the selected discovery is
not required (`DiscoveryTreeSO::DiscoverItem`, token `0x06000AAD`,
`IL_0000`–`IL_0024`). Therefore required picks and rerolls do not escalate the price;
non-required picks do, independently for each discovery tree. Production, progression,
and picks in another category do not enter the index.

`GetPoolItemCostAt` walks that tree's tiered cost entries, uses the index within the
selected tier to scale its base costs, applies only that tree's configured research
reduction, and rounds to two significant digits
(`DiscoveryTreeSO::GetPoolItemCostAt`, token `0x06000ABA`,
`IL_0000`–`IL_00A0`; `DiscoveryTreeSO::GetTotalCostReductionLevel`, token
`0x06000ACF`). The Spell Discoveries tree gives its first five pool picks a base cost
of 90 Knowledge and scales them with `DiscoveryTreeCostFlat`; that modifier's authored
`MultiStacking` adjustment of 9 converts to a factor of 10. The sequence is therefore
90, 900, 9,000, 90,000, and 900,000 Knowledge. The infinite tier then begins at
1,000,000 Knowledge plus 500 Thaumic Scrolls
(`game_data.json:311232`–`311251`, `374460`–`374520`).

Glyph Discoveries is a separate tree with a base of 200 Knowledge plus 50 Thaumaturgy
and `DiscoveryTree_Glyphs` scaling (`game_data.json:312043`–`312105`,
`374346`–`374393`). Its exponent-bearing modifier list is evaluated by the same
`ValueModifierList` machinery; it must not be reduced to a guessed global “times 50”
rule.

Initialization recounts discoveries in that tree's main pool
(`DiscoveryTreeSO::Initialize`, token `0x06000A9D`, `IL_00BC`–`IL_00C2`;
`DiscoveryTreeSO::CountDiscoveredItems`, token `0x06000ABB`,
`IL_0030`–`IL_005B`). A persistent reset clears recipe/glyph discovery and the tree's
choice state (`DiscoveryTreeSO::ResetData`, token `0x06000AF0`;
`SpellRecipeSO::ResetData`, token `0x06001488`; `GlyphSO::ResetData`, token
`0x06000C14`), so NG+ resets this escalation.

## 3. Splash is rarity-normalized and is not conserved

For a splash amount \(S\), the game first selects the \(N\) resources registered to
the resource type that are discovered and have a positive calculated rarity. It then
calls `Gain` on every eligible resource with:

\[
  \text{pre-gain share}_i = \frac{S/N}{R_i}
\]

(`ResourceTypeSO::GetSplashableResources`, token `0x0600133D`, including predicate
token `0x060036DA`; `ResourceTypeSO::GainResources`, token `0x06001326`,
`IL_0000`–`IL_0048`). The calculated rarity is:

\[
  R_i = \frac{r_{\max}}{100r_i}
\]

where \(r_i\) is that resource's lifetime production rate and \(r_{\max}\) is the
largest positive rate among visible generated resources. Consequently:

\[
  \text{pre-gain share}_i =
  \frac{100S\,r_i}{N r_{\max}}
\]

(`ResourceManager::CalcGlobalProgress`, token `0x060006B7`;
`ResourceManager::CalcRarity`, token `0x060006B8`).

The “average” is not a rolling window. `GetAvgLifeTimeRate` delegates to the
since-beginning rate, `lifetimeQuantity / Player.resetTimePassed`
(`ResourceSO::GetAvgLifeTimeRate`, token `0x060012D8`;
`ResourceSO::GetLifeTimeRateSinceBeginning`, token `0x060012D7`). Non-splash gains
increase `lifetimeQuantity`; splash gains deliberately do not. A capped resource
registers only the amount that actually fitted
(`ResourceSO::RegisterGain`, token `0x06001284`).

Splash is therefore intentionally non-conserving. Even before individual `gainRate`
modifiers and capacity/overflow handling, its sum is
\((100S/N)(\sum r_i/r_{\max})\), not \(S\). The observed 10.4 Mental becoming 7.79
Knowledge plus 0.421 Psi need not leave a distributable remainder; other eligible
Mental resources may also receive shares, and the per-resource scaling is not a
partition of 10.4. The Mental set in the dump contains Control, Knowledge, Psi, Skill,
and Cognitive Disc (`game_data.json:350945`–`351019`).

A player or mod can affect splash weights through non-splash lifetime production,
visibility/discovery and resource-type membership, caps that limit registered lifetime
gain, resets and discovery timing, and each recipient's final gain-rate modifier.
Current quantity is not itself an input. A mod that changes serialized membership or
feeds raw/non-splash gains changes the exchange weights; a splash does not recursively
teach the rarity calculation.

## 4. Rarity Value is a dynamic inverse production ratio

The tooltip's “Rarity Value” is the runtime `calcRarityValue`, not the resource's
authored rarity field (`ResourceSO::GetBaseTooltipNodes`, token `0x06001307`,
`IL_060F`–`IL_0640`). It is the formula \(r_{\max}/(100r_i)\) above. The fastest
visible generated resource has a value of 0.01. A Psi value of \(3.37\times10^4\)
means Psi's lifetime production rate is 3.37 million times slower than the fastest
eligible resource.

It is dynamic: lifetime non-splash production and the visible comparison population
change it. It is not a static purchase price or a measure of current holdings. It is
usable as the game's *splash allocation exchange weight*, but not as a general
cross-resource utility exchange rate.

The distinct authored `rarityValue` is returned by `GetRarity`; its quality-adjusted
form is `rarityValue * quality / 100` and is used by cost-list rarity calculations
(`ResourceSO::GetRarity`, token `0x060012D9`;
`ResourceSO::GetRarityValue`, token `0x060012DA`, `IL_0000`–`IL_0022`). For example,
Psi's serialized static rarity is 2 (`game_data.json:186260`–`186342`). Strategist
code must not confuse that authored cost weight with the calculated splash value.

## 5. Persist runes leave permanent advancement, not a selected rune

Time runes themselves are save-persistent objects, but a persistent reset serializes
them, resets the world, reloads the persistent save, and then runs persistent-reset
cleanup (`GameManager::PersistentResetGameState`, token `0x0600055B`,
`IL_0042`–`IL_00A7`). `TimeRuneSO.Cleanup` clears the rune's current level, discovery,
and discovery-rarity state (`TimeRuneSO::OnPersistentReset`, token `0x06001844`;
`TimeRuneSO::Cleanup`, token `0x06001867`, `IL_0000`–`IL_0015`). A Time Persist rune
is therefore not still picked or active as a rune in the next run.

What survives is the persistent advancement XP that the rune granted while it was
levelled. Ability Persist, for example, gives XP to `AdvTimeAbility`, whose advancement
definition is persistent (`game_data.json:248204`–`248338`,
`295209`–`295330`; `AdvancementSO::IsPersistent`, token `0x0600082E`;
`AdvancementSO::ApplyEffects`, token `0x06000822`). Its already-earned advancement
levels keep applying even when the rune is not repicked. Re-picking is needed only to
buy more rune levels and grant more persistent advancement XP.

Rune mastery uses an `ExperienceContainer` initialized from the saved mastery level and
XP (`TimeRuneSO::Initialize`, token `0x06001822`, `IL_0000`–`IL_003B`). A rune level
adds mastery XP, then `GetGainedLevelsSingle` repeatedly subtracts complete thresholds
and retains the remainder (`TimeRuneSO::LevelUp`, token `0x06001827`,
`IL_0014`–`IL_005F`; `ExperienceContainer::GetGainedLevelsSingle`, token
`0x06001C34`, `IL_001E`–`IL_0057`). Persistent advancement XP uses the same container
model. Overflow is neither discarded nor clamped at a tier boundary, and one grant can
cross multiple thresholds.

The rune purchase cost is zero while `freeUsages > level`; otherwise it evaluates the
leveling cost at `level + 1 - freeUsages`
(`TimeRuneSO::GetResourceCost`, token `0x0600183E`). Persist runes have a base cost of
1 Time Advancement and a +1 `MultiDiminishing` per-level curve
(`game_data.json:248266`–`248280`). With one free use, their observed costs are exactly
0, 1, 2, 3, 4, and so on.

Finally, `ApplyMasteryLevels` adds the raw mastery level to the player's total rune
mastery (`TimeRuneSO::ApplyMasteryLevels`, token `0x0600182E`). The serialized
`TotalTimeRuneMastery` variable applies +1 raw to “Starting Time Advancements” per
point (`game_data.json:215786`–`215849`, `216223`–`216303`). Thus every rune mastery
point adds exactly one starting Time Advancement.

## 6. Attribute completion drives progression

Every completed attribute level runs its `StructureTypeSO` develop effects with the
number of newly developed levels as the ratio
(`StructureTypeSO::ExecuteOnPurchase`, token `0x060017FD`,
`IL_0000`–`IL_001F`). Each authored tab type has non-scaling effects that grant +1 of
its tab XP and +1 Orb XP per completed level. Queuing or paying is not enough: the
level contributes when development completes.

The mappings are Alchemist → Alchemy; Arcanist, Flameweaver, Stormshaper, and Wizardry
→ Wizardry; Artificer and Reinforced → Artificer; Dimensional → Shaper; Druidry →
Druid; Mystic → Mystic; Scholar → Scholar; Workshop → Construction. Every one also
grants Orb XP. The Scholar and Wizardry type records show the common shape directly
(`game_data.json:337106`–`337233`, `339052`–`339179`).

Tab XP thresholds start at 40 and add 10 per already-applied level through `XpPage`;
the sequence is 40, 50, 60, … . Orb XP starts at 50 and adds 5 through `XpOrb`; its
sequence is 50, 55, 60, … (`game_data.json:311748`–`311791`). “Wizardry Lv3 in 60”
therefore displays the next threshold with two prior levels applied. “Scholar Lv1 in
21” is 21 XP remaining out of the 40-point first threshold, not a 21-point threshold.
XP beyond a threshold rolls into the next level through
`ResourceSO.CheckIfLeveledUp`. The advancement-capacity grant table is in section 1.

## 7. Augment glyph levels are global; socket copies compound

Quick has two different effect sets:

- Each socketed copy modifies that spell's cost by ×1.15 and cooldown by ×0.70.
- Every purchased Quick level applies global Cantrip modifiers of ×1.04 cooldown
  speed and ×0.94 cost.

The authored socket and leveling records prove those factors
(`game_data.json:307242`–`307360`, `307439`–`307510`).
`GlyphSO.ApplyLevels` applies `levelingEffects` to their target independently of any
spell socket (`GlyphSO::ApplyLevels`, token `0x06000BBC`,
`IL_0068`–`IL_0080`). The per-level Cantrip passives therefore remain active globally
once Quick is levelled; Quick need not be socketed.

`ApplyLevels` also adds one `maxUsages` per total glyph level and one `freeUsages` per
six levels (`GlyphSO::ApplyLevels`, token `0x06000BBC`,
`IL_000A`–`IL_0063`). Quick starts with max usage 1 and free usage 0
(`game_data.json:307513`–`307528`), so absent outside modifiers level \(L\) yields
max usage \(1+L\) and free usage \(\lfloor L/6\rfloor\).

Max Usage is the total number of copies available across the equipped loadout, enforced
after subtracting equipped copies (`UIGlyphListItem::GetInteractableMaxLoadout`, token
`0x06002402`, `IL_0008`–`IL_0032`). Free Usage is per-spell: that many copies of this
glyph in each spell do not charge the glyph's usage resource/weight
(`Spell::GetNonFreeUsesOfGlyph`, token `0x0600104A`;
`GlyphSO::GetUsageCostOfRecord`, token `0x06000C08`,
`IL_0047`–`IL_0070`). `freeLoadoutUsages` is a separate loadout-wide waiver handled by
`SpellUsageCalculator` (token `0x060010A5`).

For \(q\) Quick copies on one spell, the glyph passes \(q\) into each modifier
(`GlyphSO::GetModifiers`, token `0x06000BFF`, nested token `0x0600349A`).
`MultiStacking` exponentiates its factor, so the socket effects are ×\(1.15^q\) cost
and ×\(0.70^q\) cooldown (`ValueModifier::MultiplyScalar`, token `0x0600204B`).
Each non-free copy costs one Spell Weight; copies are not merged into one weight.

## 8. Modifier folding is type-ordered and stage-ordered

The five `ValueModifierType` operations are:

| Type | Adjustment of current value \(v\) | How same-type modifiers combine |
|---|---|---|
| Raw | \(v+a\) | add adjustments |
| MultiDiminishing | \(v(1+a)\) | add adjustments |
| MultiStacking | \(vf\) | multiply factors |
| Reduction | \(v/(1+a)\) | add adjustments |
| Exponent | \(v^e\), with inverse exponent below 1 | multiply exponents |

The operations are implemented by `ValueModifier.Adjust` (token `0x0600203C`).
Serialized adjustments for multiplicative one-default types are converted to real
factors by adding one (`ValueModifier::ConvertToReal`, token `0x06002065`).
`AddModifier` performs the same-type combination above (token `0x06002048`).

Within one order, `CombineSameOrderList` emits types in enum order: Raw,
MultiDiminishing, MultiStacking, Reduction, Exponent (token `0x06002059`). Across
orders, `CombineSameOrderLists` sorts ascending and `AdjustWith` applies in that order
(tokens `0x0600206D`, `0x06002069`). This explains Investment Power: two +18%
`MultiDiminishing` tiers add to +36%, so 225 × 1.36 = 306. Two ×1.18
`MultiStacking` entries would instead give ×1.18².

There is an additional outer order: gameplay calculations apply separate modifier
records in their authored pipeline. A normal resource gain applies its resource
`gainRate` to the requested non-raw amount, then resolves overflow/capacity
(`ResourceSO::Gain`, token `0x06001282`, `IL_0020`–`IL_0046`). Splash first computes
the section 3 allocation and then enters that gain pipeline. A spell cost is built from
base cost, glyph augmentation/conversion, per-level scaling, spell/global/type cost
modifiers, percentage multiplication, and two-significant-digit rounding
(`Spell::ComputeCost`, token `0x06000FB2`). Actual spending then divides the displayed
cost by resource quality (`ResourceSO::GetTrueSpend`, token `0x060012BC`).
`ResourceCostList.Apply` maps a supplied modifier over every cost entry (token
`0x06001E31`). There is no safe universal shortcut that combines modifiers belonging
to different pipeline stages into one bag.

## 9. Overcap loss waits three seconds, then rubber-bands to cap

Every resource initializes a one-shot loss timer of exactly three seconds
(`ResourceSO::Initialize`, token `0x06001274`, `IL_0046`–`IL_006C`).
`IncrementLoss` advances it only when explicit percentage loss is nonzero or the
resource is over capacity with overflow rubber-banding enabled
(`ResourceSO::IncrementLoss`, token `0x06001295`).

For the advancement resources in the dump, explicit loss is zero, base loss is 0.5,
and the overflow-loss modifier is 100%. Once the timer engages and \(Q>C\), the loss
rate is:

\[
  0.85(Q-C) + 0.5
\]

units per second, evaluated on discrete updates until quantity reaches capacity
(`ResourceSO::GetLossRate`, token `0x060012D5`, `IL_005F`–`IL_00D5`;
advancement definitions at `game_data.json:182925`, `183428`, `183766`, `184442`).
It is an exponential-like pull on the excess plus a fixed tail, not a fixed percentage
of total quantity. It stops at the cap.

With `pauseLossOnChange`, any nonzero public `Gain` or `Spend` call whose
`pauseLoss` argument is true resets the three-second timer
(`ResourceSO::Gain`, token `0x06001282`, `IL_000F`–`IL_0020`;
`ResourceSO::Spend`, token `0x0600128C`, `IL_000F`–`IL_001F`). Purchases normally use
that spend path. Direct internal quantity mutation does not. During the resource tick,
an active modifier-backed rate or active drain also keeps resetting the timer
(`ResourceSO::Increment`, token `0x06001275`, `IL_0050`–`IL_006E`;
`ResourceSO::HasActiveRate`, token `0x060012AA`). A plain authored continuous base rate
uses the internal gain path and does not by itself count as a touch, matching the
observed distinction between discrete updates and continuous production.

## 10. Momentum is a shared-timer accumulating emblem

Momentum is a global `AccumulatingPassiveType` and `EmblemPassiveType` passive with ten
tokens and a four-second duration before scaling. Each effective stack contributes +8%
additive build speed, ×1.08 Cantrip cooldown speed, and ×1.04 Cantrip power
(`game_data.json:266784`–`266943`). Kinetic Mind's trigger adds one Momentum token and
causes a Mental splash; its effect/special scaling can change the granted amounts
(`game_data.json:274328`–`274534`).

`InternalChangeTokens` caps the stored value and changes effects only as whole token
boundaries are crossed; `GetPassiveTokenRatio` maps those whole stacks into effect
strength (`PassiveAbility::InternalChangeTokens`, token `0x06000E9E`;
`PassiveAbility::GetPassiveTokenRatio`, token `0x06000EB7`). Momentum has
`tokenIndividuateDuration = false`. `IncrementDurations` therefore runs one shared
countdown; each expiry removes one token, then the next token gets the next duration
interval (`PassiveAbility::IncrementDurations`, token `0x06000E97`). Adding a stack
does not refresh the countdown already in progress, and the whole stack does not expire
at once.

The dump contains 24 emblem passives: Absorption, Accelerated, Cursed, Alch. Affinity,
Focused, Fury, Hasted, Mind Affinity, Momentum, Resting Spells, Static, Amassed,
Beaming, Disintegrating, Mana Affinity, Ingenuity, Learned, Lingering Growth, Lucky,
Magic Affinity, Nature Affinity, Quick Witted, Spacial Affinity, and Vessel. The
`EmblemPassiveType` definition begins at `game_data.json:368609`; not all emblems use
Momentum's accumulating/shared-timer shape.

## 11. Achievement Strength is one percent global gain per point

When achievements apply, each completed achievement contributes its total strength as
a raw addition to the player's Achievement Strength
(`AchievementSO::ApplyEffects`, token `0x060007FB`, `IL_0000`–`IL_002A`).
`GetTotalAchievementStrength` sums the authored base strength across completed levels,
using the achievement's per-level strength curve for later levels
(`AchievementSO::GetTotalAchievementStrength`, token `0x06000801`).

The Achievement Strength variable applies a +1% `MultiDiminishing` modifier to the
global resource type's gain rate per point (`game_data.json:215132`–`215280`).
Therefore 28 strength produces exactly ×1.28 All Resources Gained. Strength rises by
completing more achievement levels; each achievement supplies its own base
`achievementStrength` and optional per-level curve. The same strength also contributes
one Starting Time Advancement per point after the time-reset prerequisite is met, a
separate effect in that variable's record.

## 12. Resource and spell tags drive modifier targeting

The serialized resource-type taxonomy is:

> Blooming, Building, Celestial, Elemental, Energetic, Essence, Hexed, Liquid,
> Magic, Mental, Metal, Natural, Parchment, Spacial, Advancement, All Capped, All,
> Influential, Progression, Spiritual, Tempered.

The serialized spell/effect-target taxonomy is:

> Arcane, Corporeal, Dragon, Druidic, Expansion, Flow, Primary, Psionic, Storm,
> Alteration, Cantrip, Charm, Conjuration, Divination (displayed as “Divining”),
> Evocation.

The type records occupy the resource/spell type section beginning at
`game_data.json:350945`; Cantrip and Divination begin at `357899` and `358672`.

`Spell.Initialize` loads glyphs and then establishes spell types
(`Spell::Initialize`, token `0x06000FA8`). `SetupLimitedElementalType` starts from the
recipe's tags and may add or replace elemental tags based on glyphs (token
`0x06000FEA`). `GetAllSpellTypes` combines negative/exclusion and augmented type lists
(token `0x06000FEF`). Spell calculations then request property records such as Power,
CooldownSpeed, and Cost from every applicable `SpellTypeSO`
(`SpellTypeSO::GetValueModifierRecord`, token `0x060014C7`). A tag-targeted buff is
therefore predictable by membership in the spell's *effective* type list, including
glyph changes, rather than display-name inference.

In the serialized play-state dump, the currently discovered spells and authored tags
are:

| Spell | Tags |
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

Gather Knowledge's record starts at `game_data.json:229626`, and it is indeed Divining:
the internal tag is named `Divination`. This ownership list is a snapshot of the
serialized dump, not a claim about a later live save; authored recipe tags are stable,
while discovery and glyph-derived effective tags are runtime state.
