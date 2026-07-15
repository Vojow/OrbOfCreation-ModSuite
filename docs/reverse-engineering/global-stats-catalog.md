# Global and global-ish stat catalog

[Back to index](README.md) · [Achievement Resonance](../plans/achievement-resonance.md) · [Audit](audit.md)

## Modifier scopes

The game has four useful levels of modifier scope:

1. **Attribute groups** merge a modifier into many serialized member records. These are the best native global targets.
2. **Player `NumberVariable`s** are singleton-like values used across a broad subsystem.
3. **Type objects** merge or supply values to every object assigned to a domain type.
4. **Concrete upgradeable objects** expose common property names but must be targeted individually or enumerated by the mod.

For Achievement Resonance, prefer the narrowest native target that really represents the intended bonus. Do not enumerate hundreds of objects when an audited group already owns the relationship.

## Attribute groups — exhaustive mapping list

All 24 mapped `AttributeGroupSO` assets are possible broad targets. Their names and UUIDs are verified; exact members are serialized and must be logged at runtime.

| Group | UUID | Likely scope | Resonance status |
|---|---|---|---|
| `GlobalSpeedGroup` | `8a199f0d-48dd-4c3e-840e-d97a1b7dca4b` | Broad speed properties | Best speed proof of concept |
| `GlobalSpeedSpecialGroup` | `9cabe820-71b9-4250-a7a0-3b9e00e03453` | Special speed properties | Probe; may overlap global speed |
| `AgromancySpeedGroup` | `2ca21dd7-da00-4016-a489-21136825bc9d` | Agromancy speed | Optional domain target |
| `AlchemySpeedGroup` | `f73fbe8c-394b-4602-a8f6-95b324b2a793` | Alchemy speed | Optional domain target |
| `MentalSpeedGroup` | `dd38a2ac-45bc-41dc-acfb-8b1fa81c2c3e` | Mental/research speed | Optional domain target |
| `PhysicalSpeedGroup` | `13f68484-a2db-4e1a-ba90-18de2b0ecacf` | Physical/manufacturing speed | Optional domain target |
| `AgromancyPowerGroup` | `026977c7-9d5e-4762-b67a-8df4163c9a51` | Agromancy power | Recommended power component |
| `AlchemyPowerGroup` | `0861aea1-4f80-45f3-a190-1cac2533e41c` | Alchemy power | Recommended power component |
| `ManufacturingPowerGroup` | `317d234b-354e-41bb-a252-6a280fb506ff` | Crafting/manufacturing power | Recommended power component |
| `MentalPowerGroup` | `633688bb-983e-4200-9d08-8c87779333f0` | Mental/research power | Recommended power component |
| `AllDurationGroup` | `b096ccd2-7ff4-4ac2-8cc8-da215677e299` | Duration properties | Good optional target after probe |
| `AllSpecialsGroup` | `bfed13da-c722-416b-a2fa-a0366a49d156` | Special-effect properties | Good optional target after probe |
| `AllEchoRatingGroup` | `445d64b5-4bfb-4c6c-80ae-5afacf7f245e` | Echo ratings | Optional advanced bonus |
| `AllAlchemicCapacities` | `d168de6a-6141-42c1-a2dd-49580bf05174` | Alchemy capacities/slots | Powerful; off by default |
| `AlchemyDrainGroup` | `a67aa652-8071-457b-8b34-c0bc2adc4c5e` | Alchemy drain/cost | Direction must be inverted correctly |
| `AgromancyRefundGroup` | `8fb5fcdb-83cf-4d7e-843b-5ed38b906905` | Agromancy refunds | Optional efficiency bonus |
| `SpecialResourceReplenish` | `f01ebcd9-b9bf-45e2-8b69-511f46114424` | Resource replenishment | Optional; large economic impact |
| `LuckGroup` | `a743549e-4deb-4072-94b5-46387497e9b2` | Composite luck | Probe overlap before use |
| `LuckBasicGroup` | `7f7bb329-acf2-4e33-bb40-32e66048b603` | Base luck ratings | Optional |
| `LuckEffectGroup` | `9185a454-8ef2-418d-96b8-d16d0969e038` | Luck effect strength | Optional |
| `LuckLimitGroup` | `bfef171c-1a90-466d-a8b6-d73519d95048` | Luck caps | Powerful; off by default |
| `LuckPenaltyGroup` | `9693082a-9776-4e48-a20c-62de743fe7bc` | Luck penalties | Requires reduction semantics |
| `PhysicalPrecisionGroup` | `435da7eb-0071-40ae-900c-f9b55b4341cc` | Physical precision | Optional combat/production target |
| `ManifestionSpellTypeXp` | `eb66964c-9875-4d41-8c71-58cba9ddbdda` | Manifestation spell XP | Narrow progression target |

## Player-wide number variables

Every `NumberVariable` exposes the native `Value` modifier accessor, so it can be targeted with `NumberVariable.PersistentEffect`. These are the broad gameplay values held by `Player`.

| Category | Variables | Recommendation |
|---|---|---|
| Construction | `GlobalBuildSpeed`, `GlobalStructureCost`, `EchoBuildRating`, `PowerBuildRating` | Speed is safe; cost needs reduction semantics; ratings change build rules |
| Consumables/items | `GlobalConsumablePrepSpeed`, `BonusItemRating`, `ReplenishItemRating` | Good optional speed/luck family |
| Discovery | `DiscoveryRarityRating` | Strong but understandable optional bonus |
| Resources | `ResourceOverflow`, `ResourceOverflowLoss` | Advanced; loss direction must be tested |
| Equipment | `EquipmentPower`, `EquipmentExperienceRate` | Good optional power/progression targets |
| Rituals | `RitualPower`, critical/echo ratings and limits, fail penalty, spoils, jumpstart | Use power first; treat limits and penalties separately |
| Spells | `SpellPower`, `SpellSpecial`, `SpellDuration`, `SpellCastSpeed`, cooldown speed/time, cost/drain cost, mastery/experience rate, charge/critical/echo/flash values | Coherent optional family; check overlap with groups |
| Progression | `AllSpellTypeXpMod`, `AllSpellTypeXpReqScalingMod` | XP gain may be positive; requirement scaling is a cost |

Integer variables such as maximum slots, output levels, reserve levels, and Achievement Strength itself are technically targetable but are not ordinary percentage stats. Resonance should not modify them in its default bonus model.

## Type-wide global-ish targets

These mapped assets are especially broad because their names indicate an all/global type:

| Type object | UUID | Useful properties |
|---|---|---|
| `GlobalConsumableType` | `315471ca-0d15-455d-92da-f9d5f95a3c33` | `Power`, `Duration`, `Special`, `PrepSpeed`, `BonusLevels` |
| `AllEquipmentType` | `1f57c04b-8b34-4304-a038-4e7b943c9403` | `Power`, `TypeSlots` |
| `GlobalNodeType` | `d51ffa89-fc3a-48ed-bb08-e92450aee01f` | yield, special, action/growth/rest speeds, cost, quality, recovery |
| `GlobalResourceType` | `c8f9e0c8-2b5d-48f6-9ead-27b3eb7389d4` | merged resource rates and behavior |
| `GlobalCappedResourceType` | `b5a19071-8156-494b-8986-b3c42f37b73e` | capped-resource capacity behavior |
| `GlobalRitual` | `6accc1ac-0432-4edd-8766-7b60c635c2b8` | power, speed, special, duration, ratings, completion cost/rate |
| `GlobalStructureType` | `fed272e3-495e-42ec-9bfc-7741a1814ee1` | power, power scaling, speed, costs, build speed, ratings, levels |
| `RuneTypeGlobal` | `3f8ccba2-0481-4401-b269-978600cb0208` | power, power scaling, mastery XP, free usage |

These are candidates, not automatic substitutes for attribute groups. A type object affects only objects bound to that type, and the same concrete property may also be a member of an attribute group.

### Verified global resource fan-out

`ResourceTypeSO.RegisterResource()` connects its merged records to each member resource. The global resource targets therefore provide these broad native controls:

| Target/property | Effect | Default guidance |
|---|---|---|
| `GlobalResourceType.Rate` | Passive flat resource production | Recommended resource-rate bonus |
| `GlobalResourceType.GainRate` | All eligible normal, non-raw gains | Optional; can compound with `Rate` |
| `GlobalCappedResourceType.MaxQuantity` | Existing capped-resource capacity | Recommended capacity bonus |
| `GlobalCappedResourceType.MaxQuantityRate` | Capacity growth rate | Optional after runtime testing |
| `GlobalResourceType.Quality` | Resource quality | Advanced |
| `GlobalResourceType.Replenish` / `ReplenishTime` | Replenishment strength/timing | Advanced; direction-sensitive |
| `GlobalResourceType.DecayRating` / `DecayTime` | Decay behavior | Advanced; direction-sensitive |

Only generated resources not marked `excludeFromGlobals` register with `GlobalResourceType`. Only resources already reporting a maximum additionally register with `GlobalCappedResourceType`.

Do not combine `Rate` and `GainRate` into one hidden modifier. Their effects can compound on passive production and should be independently visible in config and tooltip output.

## Exhaustive upgradeable property vocabulary

IL inspection of every `GetUpgradeModAccessorInternal()` override found the following public property names. This is the complete static accessor vocabulary in the current main assembly; effect-property lists can add indexed targets at runtime.

| Object family | Modifier accessors |
|---|---|
| `AdvancementSO` | `Power`, `Level` |
| `AlchemyRecipeSO` / `AlchemyTypeSO` | `Power`, `Speed`, `DrainCost`, `Special`, XP, overdrive equivalents, `CompletionTime`, `TimeScaling`, usage slots, `EffectLevels` |
| `ConsumableTypeSO` | `Power`, `Duration`, `Special`, `PrepSpeed`, `BonusLevels` |
| `CraftingRecipeSO` / type | `Power`, `Speed`, `Cost`, `CostIncrement`, `Efficiency`, auto/multi speed penalties, magnitude increment |
| `EquipmentSO` / type | `Power`, `ExperienceRate`, `TypeSlots` |
| `HarvestActionSO` / type | `Power`, `Speed`, `Cost`, `GrowthSizeMod`, `RefundRating` |
| `HarvestElementSO` / type | power, harvest/growth/rest/action speeds, growth, drain cost, quality, capacities, auto-generation, XP, action cost |
| `PassiveAbilitySO` / type | `Power`, `Cooldown`, `Cost`, `Duration`, `MaxStacks`, `TokenRate` |
| `PlotNodeActionSO` | `Power`, `Speed`, `Cost`, `GrowthSizeMod` |
| `PlotNodeSO` / type | yield, special, action/growing/resting speed, action cost, XP, size, quality, recovery, natural growth/power |
| `ResearchSO` / type | power, bonus/base levels, level caps, investment, leeway, requirements, total level |
| `ResourceSO` | rates, capacity, quality, gain/loss, rest, attribute cost, reservation, reverberation, replenish, decay, rally, drain, indexed effect properties |
| `RitualSO` / type | power, speed, special, duration, echo/critical ratings and power, chains, completion cost/rate |
| `SpellRecipeSO` | `Power`, `Special`, `Cost`, `CooldownSpeed`, `Duration` |
| `StructureSO` / type | power, power scaling, speed, active/passive cost, cost scaling, attribute-rank effects, drain, levels, build speed, ratings, indexed properties |
| `TimeRuneSO` / type | `Power`, `PowerScaling`, `MasteryXpMod`, `FreeUsage` |
| `NumberVariable` | `Value` |
| `AttributeGroupSO` | `MergingRecord` |

The assembly contains 193 modifier-record fields across upgradeable object families. The table groups equivalent recipe/type accessors so the design remains readable.

## Scaling conclusion

There is no safe global scaling switch.

- Clearly beneficial candidates: structure `PowerScaling`, time-rune `PowerScaling`, selected spell/effect scaling records.
- Ambiguous candidates: instance scaling, duration scaling, time scaling.
- Usually harmful when increased: `CostScaling`, XP requirement scaling, completion-time requirements, drain cost.

The first public version should expose **Power Scaling** only after the runtime probe identifies the exact target records. Cost or time scaling should be separate reduction bonuses, not members of a generic Scaling option.

## Recommended Resonance v0.1 targets

| Bonus | Native target | Default |
|---|---|---|
| Speed | `GlobalSpeedGroup` | Enabled after overlap probe |
| Power | four domain power groups | Enabled after membership probe |
| Duration | `AllDurationGroup` | Optional |
| Special | `AllSpecialsGroup` | Optional |
| Discovery luck | `DiscoveryRarityRating` or a proven non-overlapping luck group | Off |
| Efficiency | selected refund/cost-reduction targets | Off |
| Power scaling | curated concrete/type records | Off until proven |
| Capacity/limits | alchemy/resource groups or integer variables | Advanced, off |
| Resource rate | `GlobalResourceType.Rate` | Optional, recommended over `GainRate` |
| Resource capacity | `GlobalCappedResourceType.MaxQuantity` | Optional |
| Casting speed | `SpellCastSpeed` and `SpellCooldownSpeed` | Optional |
| Casting strength | `SpellPower`, `SpellSpecial`, `SpellDuration` | Optional |
| Casting progression | `SpellMasteryRate`, `SpellExperienceRate` | Off by default |

Before applying any of them, the probe must emit each group member as:

```text
group UUID -> target UUID/type -> propertyType[propertyIndex]
ratio, ratioExp, orderAdjust, prerequisites, current modifier record
```

That report is the missing evidence needed to prevent double application.
