# Game MCP (performance-debug only)

The suite serves a localhost MCP endpoint from inside Orb Of Creation when, and only when, it was
built with the `perf-debug` profile. Ordinary and release builds compile the server and every MCP
gadget out.

## Connect

Close the game before installing, then launch it through Steam:

```sh
tools/install-supported-suite.sh perf-debug
open "steam://rungameid/1910680"
```

The endpoint starts on the title screen, before a run is loaded:

```text
http://127.0.0.1:19106/mcp
```

A general MCP client can use:

```json
{
  "mcpServers": {
    "orb-of-creation": {
      "type": "http",
      "url": "http://127.0.0.1:19106/mcp"
    }
  }
}
```

The transport accepts streamable-HTTP JSON-RPC POSTs, negotiates MCP versions
`2025-11-25`, `2025-06-18`, and `2025-03-26`, and returns one JSON response per request. Clients
advertise `application/json, text/event-stream`, send `Content-Type: application/json`, and send the
negotiated `MCP-Protocol-Version` after `initialize`. Notifications return HTTP 202 with no body.
The listener is fixed to IPv4 loopback and rejects non-loopback origins.

The Start screen displays a large native-styled ModSuite status card directly beneath the game's
version number in both build modes. Its headline names `PERF-DEBUG · MCP READY` or
`RELEASE BUILD · MCP OFF`; the card also shows audit health, loopback endpoint, suite version, and
process ID. No card means that process did not load ModSuite; differing PIDs expose duplicate game
instances. Red means the control plane failed, amber names an intentional or compatibility-limited
state, and green means the perf-debug agent endpoint and audited game are ready. In perf-debug, an
agent can attach immediately and call `game_continue`; tools that need the Main scene or a
published WORLD reject with an exact not-ready reason until the run loads.

The dependency-free test client performs the handshake automatically:

```sh
tools/game-mcp-client.py doctor
tools/game-mcp-client.py continue
tools/game-mcp-client.py tools
tools/game-mcp-client.py call world_overview
tools/game-mcp-client.py measure-reads
```

`--transcript PATH` records exact request and response JSONL. Evidence belongs under ignored
`artifacts/`; it must not be force-added to Git.

## Architecture and safety

HTTP workers never read Unity objects or suite publications. Every stateful tool submits one
immutable operation to the next Unity-frame boundary. After the ServiceCycle pump, that boundary
atomically claims every pending request, pins one immutable world/configuration context by reference,
and executes the complete claim in submission order. There is no MCP shadow world, timer, polling
loop, priority queue, four-command throttle, or listener-lifetime gameplay lease. With no requests,
Unity performs only the inbox empty check.

Published world facts are preferred. Because this is a localhost-only debugger, a parameterized
tooltip, inspected panel, screen catalog, fixed probe, or framebuffer read may instead run only in
the exact requesting operation on Unity's main thread. Direct queries never mutate gameplay, never
return a pending receipt, and never introduce polling. UI changes are classified separately from
gameplay. A missing or partially collected read returns `unavailable` with an exact `reasonCode`
and reason.

World reads contain one decision-relevant `worldGeneration`, status/refusal evidence, and the
requested data. They do not repeat lifecycle/configuration generations, request fields,
capture/respond timestamps, or mailbox internals. Gameplay
operations re-resolve UUID/native type and invoke the same canonical GameAction as features/tests,
including current lifecycle/configuration/emergency/ownership/native admission and exact mutation
receipts. Save deletion, save import/export, run reset, arbitrary clicks, arbitrary keys,
caller-supplied reflection/native invocation, and progression unlocking are absent. The complete
frame and data-lifetime contract is in
[Game MCP frame operations](../runtime-architecture/game-mcp-frame-operations.md).

Every game-domain `BigDouble` is one JSON string produced by the shared MCP number formatter, never
a JSON number or a text/mantissa/exponent object. Zero is `"0"`. Every nonzero magnitude uses a
normalized mantissa with at most two decimal places and a lowercase `e` exponent without a plus
sign: `"1.4e4"`, `"7.82e4"`, `"1.1e24"`, and `"1.23e-3"`. This is the only numeric notation and has
no precision or verbosity option.

The game aggressively caches some derived values until their screen has been viewed. That upstream
behavior is not silently worked around here. If a stale cache prevents a native action, the
terminal rejection must name the stale cached fact and the screen-view condition; the MCP server
does not refresh it by hidden navigation.

## Tool surface

| Tool | Purpose |
|---|---|
| `world_overview` | Compact collection, economy, progression, and running-state summary |
| `world_categories` | Discover every published table and exact collection availability |
| `world_list` | Page compact identity-plus-scan rows in one category |
| `world_get` | Read an ordered 1–200 UUID list from one immutable generation; optional native-type assertion |
| `entity_catalog` | Search every live-registry identity and available player-facing name, including loaded entities hidden by progression |
| `explain_entity` | Evaluate one UUID's gates, requirement graph, exact costs, and blockers from one immutable generation |
| `world_search` | Search stable-UUID entity categories; composite diagnostic rows are excluded |
| `suite_health` | One compact runtime, feature, service, STOP, scene, and contract-health shape |
| `suite_configuration` | Read the single committed configuration and writable setting catalog |
| `trace_health` | Read trace-writer health, segment, record, and byte counters |
| `game_purchase` | Buy a structure or upgrade derived from its UUID |
| `game_cast` | Fire or release one equipped spell |
| `game_concept` | Add, remove, or rotate one concept assignment |
| `game_harvest` | Harvest an audited pair derived from a plot UUID |
| `game_spell_level` | Buy one spell mastery level or invoke level-all |
| `game_discovery_offer` | Initiate, select, confirm, or reroll one Discovery Tree offer lifecycle |
| `game_discover` | Discover one published alchemy recipe, equipment asset, glyph, ritual, or time rune |
| `game_equipment` | Equip/increase or unequip/decrease one created artifact using live native multi-buy |
| `game_spell_workbench` | Select an authored spell recipe, discover it, or create another equipped instance |
| `game_spell_composition` | Set the global spell output level or replace one equipped spell's augment stack |
| `game_spell_loadout` | Remove one exact equipped runtime spell or move it to another loadout slot |
| `game_targeting` | Submit a specific eligible target, submit a native random target, or cancel the current request |
| `game_consumable` | Use, cancel, discard, randomize, or reorder one published consumable |
| `game_craft` | Execute one published direct or queued crafting recipe |
| `suite_config_set` | Commit one allowlisted setting through the configuration store |
| `suite_emergency_stop` | Engage or resume the suite's shared emergency stop |
| `game_screenshot` | Return the framebuffer as inline MCP image content |
| `game_continue` | Continue the already-selected save from the Start scene |
| `game_screen_catalog` | Read compact named tabs with the active tab/subtab marked and grouped |
| `game_navigate` | Navigate a catalog tab/subtab and optional published plot UUID |
| `game_tooltips` | Page through active tooltip-bearing elements by exact indexed path |
| `game_tooltip` | Read typed authored/computed node trees, nested links, and open inspected panels |
| `game_probe` | Read one fixed native fact not carried by `WORLD` |

`world_overview` deliberately contains only facts a strategist normally wants before choosing a
detailed read: collection completeness with total successfully read and skipped row counts,
unavailable categories, resource-row count, unlocked
structure count, purchasable-upgrade count, discovered/mastery-ready recipe
counts, available views, visible plots, and current action/spell/concept/plot occupancy. Exact rows
remain in list/get/search.

`world_categories` is the authoritative inventory. Each row reports `category`, native type,
identity mode, row count, and exact availability. Internal world-property and row-type names are
not protocol data. `world_get` always requires `category` plus one `uuids` list, including when the
list has one item.
`expectedNativeType`, when supplied, is a strict assertion and rejects a mismatch. Composite tables
cannot be addressed by an arbitrary related UUID; use `world_list`.

Supply `uuids` with 1–200 canonical UUIDs. Results preserve input order without repeating an index or UUID on
successful rows. A typed not-found or invalid result repeats the implicated UUID because that is
failure evidence. Every row shares the response's one `worldGeneration`; the server does not issue
or retain a snapshot token across calls.

`entity_catalog` complements `world_search` with the game's complete live runtime identity registry.
At the first stable Playing world capture after `RuntimeReady`, the suite validates and copies that
registry once for the lifecycle. Searches cover UUID, exact runtime type, Unity asset name, and
player-facing `GetName()`, so loaded entities hidden or not yet revealed by progression are findable
without navigation. Before that bind, or when its declared contracts fail, the tool returns
`unavailable` rather than substituting the build-time TSV fixtures.

A match contains `uuid`, `name`, `nativeType`, and one `category`. `category=not-world-projected`
means that the live registry identity has no world row. `nameSource=asset` appears only when no
player-facing name exists and the Unity asset name supplied the label; absence means the name is
player-facing. `internalName` appears only when it differs. The same immutable
catalog reference is pinned with the answering world and supplies names for every MCP entity
reference; UUID-only joins are unnecessary. Catalog membership and naming do not prove current
visibility, availability, or a world-category row. Lifecycle replacement clears the catalog before
the next bind, so no prior-save Unity reference or label survives.

`world_list` and `world_get` use the same deliberate player-relevant row projection:
stable identity plus the small set of availability, level, quantity, occupancy, readiness, or
progress fields useful for comparing rows. Raw capture inputs, cached implementation fields,
resource traits, rate inputs, and modifier structs stay out of world rows. `explain_entity` owns
deeper evaluated evidence. Purchase-cost rows are composite and therefore remain a `world_list`
surface.

`purchase-costs` is the only modifier-adjusted live cost category. Spell and alchemy cost rows are
immediate/drain observations and are not mislabeled as purchase prices. Each structure/upgrade cost
row exposes `baseCost`, verified `effectiveCost`, optional `groupLevels`/`groupCost`, and named
`costModifiers`. It also exposes the resource's same-generation canonical spendable `amount`, the
`totalCost` after duplicate-resource rows are combined, `resourceAffordable`, and
`purchaseAffordable`, with reason codes only when false. Ordinary resources spend true holdings;
bandwidth resources spend headroom. These fields use the same exact combiner as Auto Buy and do not
include Auto Buy's configurable reserve or excess policy.

A `resources` row is deliberately only named identity, canonical spendable `amount`,
`netRatePerSecond`, and, when the resource is capped, `capacity` plus `atCapacity`. A negative native
capacity is the game's uncapped sentinel and is never serialized as a magnitude. The old
`balance`/`quantity`/`trueQuantity` ambiguity is gone. Detailed
factor math will belong to a future Details-panel tool; it is not leaked through world rows.

Research rows distinguish the native evaluator's base and effective requirement levels. Their
`requirementLevelAdjustment` is the difference between those two native results, not a suite-owned
recalculation. `requirementAdjustments` carries each directly authored passive or active modifier,
including its amount and source UUID/type. This matters for challenge effects: the game installs
them as persistent/passive modifiers, so an active-only modifier count can be zero even while both
the research tooltip and native availability evaluator apply the challenge adjustment. Type-wide
research modifiers are included in the native effective result but are not misattributed as direct
members of the research row.

`crafting-recipe-types` describes the game's crafting families; `crafting-recipes` contains the
actual recipes and is the pre-decision surface for `game_craft`. A recipe row leads with `visible`,
`startingAmount`, `craftTimeSeconds`, and `canStart`; when false, `blockers` names the failed native
visibility, purchase, queue, page-relation, or output-capacity axis. `execution` identifies the
direct, existing-stack, or new-instance route. Page routes include current `queuedAmount` and the
named queue's used/maximum slots. `purchaseAmount` and named `nextCosts` carry the exact next cost,
canonical spendable resource `amount`, and affordability before any mutation. Direct costs use the
same `recipeCost.Multiply(purchaseAmount)` lineage as native `Execute`; page costs use the same
`GetTotalCost(previous,purchaseAmount)` lineage as native `QueueCraft`. Named `types`, `inputs`,
`outputs`, and `consumableOutputs` preserve authored order. An input
uses `cost` for the recipe requirement and canonical `amount` for what is spendable now (bandwidth
headroom for a bandwidth resource); outputs use `yield`. Only failed engagement-drain evidence is
emitted in `drainBlockers`. The category is unavailable unless both recipe and resource collectors
are clean.

`world_search` deliberately indexes only categories whose identity mode is
`stable_entity_uuid`. It does not pretend that an owner/resource UUID uniquely identifies a
composite diagnostic row. If an owner UUID exists only in `entity-requirements`, search returns an
authoritative empty entity result; `world_list(entity-requirements)` retains the exact localized
owner, ordinal, and runtime type evidence. If a searchable entity row itself is returned and that
entity owns an unmodeled leaf, the search result is explicitly incomplete for that entity.

### Discovery decision loop

`discovery-trees` is the pre-decision surface for `game_discovery_offer`; attempting an action is
never the way to learn its cost or choices. Every row names the tree UUID/type, semantic mode,
rerolls left, discovered count, and whether discoveries remain. Authoring/debug members and duplicate
identity (`treeId`, overrides, debug mode, and bonus-level cost) are intentionally absent.

In Idle mode, `initiate` reports `available`, a stable false `reasonCode` when needed, and each exact
cost line as a named `resource` plus `cost` and canonical spendable `amount`. In Choice mode,
`offers` contains named UUID/category/native-type references in native order. `selectedOffer`
appears only after selection. `rerollAvailable` appears only in Choice mode. An empty offer set
omits `offers`.

These values are copied during the shared 250-millisecond world capture from lifecycle-bound
delegates for native visibility, immediate-required state, current choices, exact next cost,
affordability, and resource true quantity. The MCP worker only projects the immutable row. A choice
UUID must resolve in the same generation as an alchemy recipe, equipment, glyph, ritual, spell
recipe, or time rune. If it does not, only the implicated tree read returns
`discovery_offer_read_incomplete` with `implicatedOffers`; the UUID is never silently omitted.
Current offers are also resolvable through `world_get` and `explain_entity`, including authored
metadata and applicable discovery predicates.

### Generic discovery decisions

Every `alchemy-recipes`, `equipment`, `glyphs`, `rituals`, `spell-recipes`, and `time-runes` row
has one `discover` decision from the native `IDiscoverable` evaluator. It names whether the entity
is visible, already discovered, required for downstream play, currently discoverable, and
affordable. Its ordered `costs` pair each named resource's scientific-string `cost` with the same
canonical spendable `amount` used everywhere else. Failed decision axes carry a stable reason;
attempting a mutation is never required to learn affordability.

`game_discover` owns alchemy recipes, equipment, glyphs, rituals, and time runes. Spell recipes use
`game_spell_workbench`, because selection, discovery, and equipped-instance creation are one
native workbench lifecycle. Discovery trees use `game_discovery_offer`. This capability split is
validated from the UUID's published category before any native call.

The `equipment` category is also the artifact-loadout pre-decision surface. Each row names the
artifact and its primary equipment type, current/maximum stacks, global and type-slot occupancy,
live multi-buy, usage-cost resources with current holdings, and the exact next equip/unequip amount
or refusal. Call `game_equipment` with `mode:"equip"` or `mode:"unequip"`; there is no amount
argument because one call reproduces one native player click. A committed call returns the complete
newer equipment row inline, so no read-back is required. Usage reservations, effects, and
attunement are post-state evidence, never payment-verification gates.

The MCP-only decision/action sequence is seven calls when two offers need explanations:

1. `world_list(category="discovery-trees")` and choose a named Idle row whose
   `initiate.available` is true.
2. Call `initiate`; its terminal response waits for and returns the named ordered Choice offers.
3. Call `reroll` when `rerollAvailable=true`; its terminal response returns the replacement offers.
4. Call `explain_entity` for the candidates that require comparison. No catalog name joins are
   needed because every reference already carries its name.
5. Call `select`; its terminal response includes `selectedOffer`.
6. Call `confirm` with that UUID; its terminal response is the Idle tree plus the next initiate
   costs. There are no post-mutation `world_get` calls, snapshot tokens, or receipt polls.

### Spell discovery and creation loop

`spell-recipes` is also the pre-decision surface for `game_spell_workbench`. Each named row
contains the authored ordered `coreGlyphs` with current owned and bonus levels, whether that exact
base recipe is selected, equipped slot positions, and current loadout count/capacity. An
undiscovered recipe exposes `discover`; a discovered recipe exposes `create`. The action object
always retains its named exact costs and spendable amounts, including before selection. If the
recipe is not currently selected, `select.available=true` and the paid action reports
`selection_required` rather than hiding its economics.

The MCP-only base-recipe sequence is:

1. Page or search `spell-recipes`; compare names, core-glyph holdings, discovery costs, and
   affordability.
2. Call `game_spell_workbench(mode="select", spellRecipeUuid=...)`. The compact committed result
   is the selected recipe row, with `discover` or `create` now callable.
3. For an undiscovered recipe, call `discover`. The returned newer row reports discovered state,
   any native auto-equipped slot, and creation economics for the next decision.
4. If another instance is wanted, select the base recipe again and call `create`. The returned row
   reports the new equipped-slot state and remaining capacity.

Every referenced entity is named inline. No catalog join, world-generation argument, payment
stanza, receipt poll, or post-mutation `world_get` is required. B-002 deliberately selects the
authored base recipe by stable recipe UUID; augment and output-level composition is a separate
family.

### Spell composition loop

The same `spell-recipes` row is the pre-decision surface for `game_spell_composition`. Its
`outputLevel` gives the current and live maximum global selector. Every `equipped` row names the
runtime spell UUID and recipe, slot, output/effective/mastery levels, exact applied augments,
duration/toggle properties, and current native usage verdict. `augmentOptions` names every
spell-augment glyph with owned/bonus level, availability, maximum/current uses, mastery
requirement, and duration/toggle restrictions. Cast and drain cost rows use the same named
resource, spendable `amount`, and affordability vocabulary as the rest of MCP.

The MCP-only composition sequence is:

1. Read one `spell-recipes` row and choose a named equipped runtime spell plus valid named augment
   options from that row.
2. Call `game_spell_composition(mode="set_output_level", outputLevel=...)`. The compact committed
   result returns the newer global output and all affected equipped spell facts.
3. Call `game_spell_composition(mode="set_augments", spellInstanceUuid=...,
   augmentGlyphs=[...])`. The compact committed result returns that exact runtime spell's newer
   applied stack, options, derived levels, usage verdict, and cast/drain economics. An empty array
   clears the stack.

The boundary revalidates the output range or exact runtime spell identity and every glyph's exact
type, availability, augment role, maximum uses, combined compatibility, and mastery requirement on
the Unity main thread. Success is the requested global value or exact target/stack outcome. It has
no payment framing, receipt poll, request echo, world-generation argument, or read-back call.

### Spell loadout loop

`spell-slots` is the pre-decision surface for `game_spell_loadout`. Each occupied row contains the
named runtime spell and recipe, exact slot, active cast/ready/attune state when applicable, the
game's current remove verdict, and every other destination in native order with a named occupant or
empty marker. The shared loadout summary exposes equipped count, capacity, and whether a hole
exists.

The MCP-only loadout sequence is:

1. Read `world_list(category="spell-slots")` and choose one exact runtime `spellInstance.uuid`.
2. Call `game_spell_loadout(mode="move", spellInstanceUuid=..., destinationSlot=...)`; success
   returns the complete newer named loadout and all next decisions.
3. Call `game_spell_loadout(mode="remove", spellInstanceUuid=...)` only when that row's
   `remove.available` is true; success returns the complete newer named loadout with the target
   absent.

The boundary re-resolves the runtime UUID and native remove verdict or slot range on the Unity main
thread, acquires the family permit last, and verifies only requested identity/outcome. Weight,
glyph usage, drain, and resource accounting are observations, not gates. There is no generation,
payment, receipt, request echo, catalog join, or post-mutation read-back.

### Targeting decision loop

`targeting` is the pre-decision surface for `game_targeting`. It is empty while no target request
is pending. Its active row names the requesting effect, identifies the native selection kind,
reports whether cancellation is available, and carries every eligible structure in native order.
Each candidate is fully named and includes current committed/effective level, availability, and
work-in-flight state. Costs and affordability are absent because targeting spends no resource.

The MCP-only targeting sequence is:

1. Read `world_list(category="targeting")` and compare its named ordered candidates.
2. Call `game_targeting(mode="submit", targetUuid=...)` to submit one exact candidate, or
   `game_targeting(mode="randomize")` to let the native request choose and immediately submit.
3. Call `game_targeting(mode="cancel")` when the active row reports cancellation available.

Submit and randomize success return the named submitted structure plus the complete newer target
state. Cancel returns the complete newer state. In every mode that means the next named request and
candidates, or `pending:false`; no follow-up read is needed.

### Consumable decision loop

`consumables` is the pre-decision surface for `game_consumable`. Each row contains its named
identity, amount and queued amount, level holdings, family types, immediate and held costs with
current resource amounts, native affordability/use admission, pending usages, current inventory and
hotbar placements, and every same-list destination. The row's `use`, `cancel`, `discard`, and
optional `randomization` objects are the next decisions; no trial action is needed to learn them.

The MCP-only consumable sequence is:

1. Read `world_list(category="consumables")` and choose from named costs, holdings, usages, and
   action verdicts.
2. Call `game_consumable(mode="use", consumableUuid=...)` or
   `game_consumable(mode="cancel", consumableUuid=...)`.
3. Call `game_consumable(mode="discard", consumableUuid=..., amount=...)` for a positive amount,
   `game_consumable(mode="set_randomization", consumableUuid=..., enabled=...)`, or
   `game_consumable(mode="move", consumableUuid=..., list="inventory|hotbar",
   destination=...)` for a zero-based same-list position.

Every committed mode returns the newer named target row, the complete newer named inventory and
hotbar, and all next decisions. Use also returns targeting state if it opened a request. There is no
payment stanza, receipt, world-generation argument, catalog join, or post-mutation read-back.

### One-shot crafting decision loop

`crafting-recipes` carries everything needed to choose a one-shot craft: player-facing identity,
native route, purchase amount, exact named costs and current holdings, affordability, queue
identity/room/current quantity, outputs, and blockers. The MCP-only sequence is:

1. Read `world_list(category="crafting-recipes")` or batch exact recipes through `world_get`.
2. Choose a row whose `canStart` is true after comparing `nextCosts`, outputs, and queue state.
3. Call `game_craft(recipeUuid=...)`.

Success returns the complete newer named recipe decision, including the next cost and queue state.
It has no receipt, payment stanza, world-generation argument, or read-back requirement. A timed
recipe without one stable loaded authored page refuses rather than guessing a queue; failure after
native work retains decomposed route and queue evidence. Auto Scribe calls the same GameAction with
its own existing planner, so MCP crafting does not create a second Scribe implementation.

### Entity explanation

`explain_entity` accepts one canonical `uuid` and pins the latest immutable world publication before
it resolves or evaluates anything. The envelope's `worldGeneration`, entity
row, predicates, requirements, costs, and blockers all come from that one publication;
the tool neither retains a snapshot token nor follows a newer publication during the call.
Named identity appears once. When the resolved native entity implements the audited `ITooltipable`
contract, its authored `GetDescription()` text leads the explanation after identity; no description
is invented when that source is absent. The `state` row uses the same curated player surface as
world reads rather than serializing the collector's complete internal struct.

Only applicable predicate slots are present: `visible`, `available`, `canDevelop`, `canPurchase`,
`canDiscover`, and `canUse`. Presence means applicable. An evaluated slot carries `value`, a stable
`reasonCode`, and its evidence source; a known collector gap instead carries `evaluated=false` and
its exact gap code. Absence means the predicate does not apply, not false. Published native verdicts remain visibly native: crafting
purchase uses `CraftingRecipeSO.CanBuyAt(GetStartingQuantity())`, and spell use uses the equipped
`Spell.CanCast()` reading. Structure and upgrade `CanPurchase()` are not published, so their predicate
is deliberately `evaluated=false` with `native_can_purchase_not_published`; the exact requirement,
availability, queue, cap, cost, and affordability evidence remains present for diagnosis without
assembling a rival purchase oracle. Consumable `CanFire()` is likewise a named collector gap,
`native_can_fire_not_published`, rather than a collection-time call or reconstructed verdict.

Discovery trees are explainable entities: their explanation carries the same decision row as
`world_get(discovery-trees)`. A UUID absent from the live identity registry returns `uuid_unknown`
and points to `entity_catalog`; a catalog-known UUID with no explainable row returns
`not_world_projected` and names the applicable read surface. The two remedies never share a code.

Per-level structure, upgrade, and Research requirements preserve the implicit container `AND`,
explicit native `AND`/`OR` nodes, authored order, and recursively expanded prerequisite-link tiers. Every leaf names
the requirement UUID and native type, comparison kind, exact published value selected by the native
evaluator (`purchased_level`, `total_level`, `purchased_quantity`, discovery, mastery, recipe,
advancement, reached, numeric, or link gate), current and required values, met verdict, and base,
scaled, and effective thresholds. Unsupported comparisons return a structured unevaluable result.

The collector also captures the safe parameterized
`Prerequisites.Container.Check(Requirements.ConditionInfo)` answer at the exact next-purchase level.
The worker compares its graph verdict with that same-generation native answer. Missing inputs,
unevaluable suite math, a different owner/level, or a disagreement makes the whole explanation
`unavailable`; a disagreement returns both verdicts and `native_verdict_mismatch`. The installed
v1.05 contract additionally pins that a structure quantity requirement reads purchased `quantity`,
not `selfBonusLevels` or an effective/total level.

Research explanations separate base, scaled, and native effective requirement thresholds and retain
every direct adjustment's UUID, source native type, modifier type, amount, order, and passive state,
including challenge sources. Their `levelPrerequisites` graph uses the native
`GetRequirementLevel()` as its check level. A Research prerequisite leaf selects the target's native
total level because `ResearchRequirement` dispatches the virtual `GetLevel()` accessor; completion
and the maximum-level cap instead use native base level, which includes purchased/base grants but
excludes bonus levels. `visible`, `complete`, `canDevelop`, range, leeway, and both cap predicates are
published native answers rather than MCP-owned reconstructions. Structure/upgrade purchase evidence uses only the published
`WorldExactCostMath.TryCombinedExactCost` lineage and reports base/effective/grouped cost, named
modifier sources, available amount, and affordability. `blockers` contains typed queue, cap,
leeway, recipe-discovery, bandwidth, and drain evidence only when an axis applies. Empty collections,
null domain properties, and inapplicable axes are omitted.

`game_navigate` is classified **UI-only, no gameplay/save mutation**. It is not read-only because
selecting a tab, subtab, or plot commits live UI state. Success returns `activeTab` and every
independent `subtabStrips[{active,labels}]` state. A tab/subtab match refusal returns the exact live label
candidates it compared. It carries no static mutation-scope label or counter ceremony. Navigation
never authorizes a gameplay or save mutation.

`suite_health` has no arguments or detail mode. It is compact text: one availability line names
scene, runtime, native-contract, and STOP state; following lines group feature and service names by
state/reason. Seven identical NotReady features therefore occupy one line, not seven objects. It
reads those owners only for the requested operation and reports no MCP queue internals.

An empty clean category means the save has no rows. A skipped native row normally makes exact
queries for the whole category unavailable. The deliberate exception is an unmodeled entity
requirement leaf: the collector publishes that leaf with its owner UUID, container, ordinal, and
runtime condition type. When those rows reconcile exactly with the skipped count, `world_get` and
`world_list` keep other owners authoritative, while `world_search` localizes the evidence only when
a returned stable entity owns the leaf. An entity get/list/search that touches the affected owner
returns `entity_data_incomplete`, `world_list_incomplete`, or `world_search_incomplete` with the
ordinary row marked partial and exact `implicatedSkippedRows`. A UUID found only in a composite row
remains outside search coverage and is diagnosed through `world_list`. If even one skipped read cannot
be tied to a published owner/leaf, the category-global refusal remains. Derived tables also require
every upstream collection report to be clean.

## Inline action results

There are no receipts, pending states, cursors, or polling tool. `action_receipt` does not exist.
Every action, configuration write, STOP transition, and gadget waits for Unity's next frame and
returns its terminal result in the same MCP tool call.

A successful read uses `available`; an unavailable domain read uses `unavailable`. A successful
mutation uses `committed`; a refused mutation uses `refused`; infrastructure or native divergence
uses `faulted`. Success carries the data requested or the live post-state and no restating `code`,
explanatory reason, counters, request echo, generation pair, or payment stanza. Refusals and faults
keep their reason, target identity, native outcome, lifecycle/configuration mismatch when real, and
decomposed action receipt when native evidence was captured. A preflight refusal without native
evidence has no zeroed receipt body. There is one canonical shape per tool and no verbosity option.

JSON tool data is emitted once in `structuredContent`; `content` appears only for actual inline media
such as screenshots, and success omits the false `isError` default. The server does not repeat the
structured payload as a text item or emit an empty media array, avoiding a second client-side parse
and text-channel truncation. Invalid arguments return all detected schema
shape errors together under `error.data.validationErrors`, with distinct `missing_required` and
`unexpected_field` codes.

A faulted or quarantined GameAction is still a completed MCP tool invocation: it omits `isError`
and its domain `status`, stable `reasonCode`, reason, native evidence, and receipt remain in
`structuredContent`. `isError=true` is reserved for infrastructure failures that happen before a
canonical action terminal exists. This distinction prevents clients from replacing an exact action
receipt with an opaque generic tool error.

The server waits up to 2,000 ms for Unity to claim a request. If the request is still pending, it is
atomically canceled as `request_canceled_before_claim` and can never execute. Once Unity has claimed
it, the operation owns execution and the worker waits for the real terminal result; a local timeout
cannot precede a hidden later mutation. There is no pending fallback.

Action schemas and results have no `worldGeneration`. Generations advance independently every
250 ms, so an action revalidates live identity and mutable facts at its GameAction boundary instead
of accepting an inevitably stale caller generation. Read tools retain their single
`worldGeneration` stamp because it identifies the immutable world that answered.

The server derives native type and action kind from the target UUID:

```sh
tools/game-mcp-client.py call game_purchase --arguments \
  '{"uuid":"STRUCTURE_OR_UPGRADE_UUID","count":1}'
tools/game-mcp-client.py call game_harvest --arguments \
  '{"plotNodeUuid":"PLOT_UUID"}'
tools/game-mcp-client.py call game_cast --arguments \
  '{"mode":"fire","slotIndex":0,"spellRecipeUuid":"SPELL_UUID"}'
tools/game-mcp-client.py call game_discovery_offer --arguments \
  '{"mode":"select","treeUuid":"TREE_UUID","offerUuid":"OFFER_UUID"}'
tools/game-mcp-client.py call game_spell_workbench --arguments \
  '{"mode":"select","spellRecipeUuid":"SPELL_RECIPE_UUID"}'
tools/game-mcp-client.py call game_spell_composition --arguments \
  '{"mode":"set_augments","spellInstanceUuid":"RUNTIME_SPELL_UUID","augmentGlyphs":[{"glyphUuid":"GLYPH_UUID","count":2}]}'
```

`game_discovery_offer` requires `offerUuid` for `select` and `confirm`, and rejects it for
`initiate` and `reroll`. Initiate and reroll verify the exact tree/type and immediate transition to
Crafting; select verifies the requested offered UUID became selected; confirm verifies that exact
UUID became discovered. Payment deltas, reroll values, counters, flags, timers, list cleanup, and
selection cleanup are evidence, never outcome gates. This matters when a cost is below the ULP of a
very large `BigDouble` amount: an unchanged amount cannot disprove a transition the game visibly
performed. On success, payment is presumed and completely omitted. Initiate/reroll wait for the
ordinary collector to publish the resulting Choice state and return its named ordered offers;
select returns the selected state; confirm returns Idle plus the next initiate costs. Failures
retain the full before/after and payment evidence when capture reached native evidence; a preflight
refusal omits the receipt.

`game_spell_workbench` requires one published `SpellRecipeSO` UUID. `select` replaces the native
core selection with that recipe's exact authored glyph order and clears augments; `discover`
revalidates that exact base selection and invokes `SpellManager.DiscoverSpell`; `create` revalidates
the selection, discovered state, slot room, and current native create-cost verdict before invoking
`SpellManager.CreateSpell`. Discovery success is the exact target becoming discovered. Creation
success is a new non-empty runtime spell identity referencing that target. Native payment and list
accounting never replace those identity/outcome gates.

`game_spell_composition` has two conditional shapes. `set_output_level` requires only a positive
`outputLevel`; the boundary then checks it against the live native maximum. `set_augments` requires
one equipped runtime `spellInstanceUuid` plus `augmentGlyphs`; each row requires `glyphUuid` and a
positive `count`, while an empty list means clear. The latter revalidates exact identities,
availability, augment classification, maximum usage, combined non-level compatibility, and recipe
mastery before its one native setter. Success is exact global value or exact target UUID plus exact
canonical glyph/count stack. A committed result is the newer spell composition and next-decision
economics; failure evidence is retained only when native execution was reached.

`game_spell_loadout` requires `mode` plus one equipped runtime `spellInstanceUuid`. `remove`
rejects `destinationSlot` and rechecks the game's live `Spell.CanRemove()` verdict. `move` requires
a zero-based `destinationSlot`, re-resolves the source slot, and invokes the same native
swap-plus-notify path as the spellbook. Success is exact target absence with survivor order
preserved, or the complete slot sequence with exactly source and destination exchanged. A
committed result is the complete newer named loadout; failure evidence is retained only after
native execution was reached.

`game_targeting` has three conditional shapes. `submit` requires one `targetUuid`; `randomize` and
`cancel` reject it. Submit re-resolves that UUID within the live native candidate list and reruns
the request's native target verdict immediately before mutation. Randomize invokes the game's own
random choice and immediately submits that result; it is not a candidate-only shuffle. Cancel uses
the current link's owning `EffectResultInfo`, because closing the targeting UI does not cancel
gameplay. Success is exact submitted-object identity plus retirement of the original request, or
the exact result becoming cancelled plus request retirement. A committed result includes the
complete newer target state; failures retain native outcome evidence only after mutation began.

`game_consumable` has five conditional shapes. `use` and `cancel` require only a
`consumableUuid`; `discard` also requires positive `amount`; `set_randomization` requires
`enabled`; and `move` requires `list` plus zero-based `destination`. Fields belonging to another
mode are rejected. The boundary re-resolves the exact `ConsumableSO`, all live verb predicates,
and the current list/source/destination on the Unity main thread, then captures the shared
ConsumableUse/MultiBuy permit last. Success is the requested queue, exact usage cancellation,
clamped holding removal, randomization flag, or complete same-list order. Payment and downstream
effect accounting do not gate success; a committed result is the full newer decision state.

`game_craft` requires one `recipeUuid`; `expectedNativeType`, when supplied, must be
`CraftingRecipeSO`. The boundary re-resolves the exact recipe, authored page/queue route, native
purchase amount, affordability, and room on Unity's main thread, then captures the shared crafting
permit last. Direct recipes invoke native `CraftingRecipeSO.Execute`; page recipes re-drive the
audited stack/new/instant `UICraftingPage.QueueCraft` sequence. Queue success is the exact recipe
quantity and instance outcome. Payment accounting never gates success. A committed result is the
newer full crafting decision row.

`game_discover` requires one `uuid`; optional `expectedNativeType` is an exact assertion, not a
selector. The boundary re-resolves the entity and repeats native visibility, already-discovered,
`CanDiscover`, exact cost, and affordability checks on Unity's main thread. It captures the shared
family permit last, then preserves the UI's `PerformCost`-before-`Discover` ordering. Success is the
exact target becoming discovered and returns its complete newer named world row inline. It carries
no receipt or payment stanza. A refusal occurs before payment; a fault after native work keeps the
named before/after evidence and quarantines only when the requested target outcome is absent.

`game_equipment` requires `mode` and one published equipment `uuid`; optional
`expectedNativeType`, when supplied, must be `EquipmentSO`. On Unity's main thread it re-resolves
the exact artifact and repeats creation, current stacks, global and primary-type slot room, live
multi-buy, maximum stacks, and native usage-affordability checks before taking the family permit.
Success is only the exact requested target-stack transition. It returns the complete newer named
equipment row with both next decisions and no receipt or payment/usage stanza. A missing transition
quarantines this family for the lifecycle; a throw after the exact transition commits.

CLI play commands therefore need no generation option:

```sh
tools/game-mcp-client.py purchase UUID
tools/game-mcp-client.py cast 0
tools/game-mcp-client.py harvest PLOT_UUID
tools/game-mcp-client.py concept-add UUID
tools/game-mcp-client.py spell-level UUID
```

Manual MCP actions do not require a worker policy to be enabled. They do use the same shared
GameAction as any feature consumer, a cooperative action-family lease, live validation, and
mutation proof.
STOP closes MCP native admission exactly as it closes automation. Resume still requires the host's
ordinary fresh-world gate.

`suite_configuration` returns `configurationGeneration` plus the startup-built
`writableSettings` catalog and its current serialized values. It never reflectively serializes the
runtime configuration record or exposes compiler metadata and internal nested policy objects.

`suite_config_set` requires the current `configurationGeneration` and commits through
`AutomataConfigurationStore`, the same single publication path as the in-game controls. BepInEx
parse/domain validation runs before publication. Compatibility acknowledgements, shortcuts, and
STOP are not generic writable settings.

## Screenshots and navigation

`game_screenshot` has no required parameters and returns an MCP `image` content block with
`mimeType: image/png`. `{"save":true}` additionally writes a server-generated, collision-resistant
name under the current trace folder. The caller supplies no basename, and there is no per-process
filename cap.

```sh
tools/game-mcp-client.py screenshot --output artifacts/current-screen.png
```

`game_continue` is deliberately separate from tab navigation. On `Start` it invokes the audited
native `SaveStateManager.StartGame` method for the save the player has already selected. It cannot
select, delete, reset, import, or rewrite a save, and it accepts no native type, method, or UI input
from the caller. Its success waits for the transition and returns the new `scene` and
`runtimeAvailable` state.

`game_screen_catalog` reads the live Main-scene UI. Top tabs retain native rail order. Current
subtabs are active `UIViewRadioButton` controls under the current native content area. Inactive
popup templates are excluded. The response is compact text: it marks the active tab and each
independent subtab strip's active label with `*`, grouping every strip beneath its owning active tab.
Unity paths and unstable numeric indexes are deliberately absent.

`game_navigate(tab, subtab?, plotNodeUuid?, capture?)` accepts exact labels only. Name matching is
ordinal and closed-world: zero or multiple matches reject with the exact candidate labels. Plot selection resolves
the supplied UUID as a published `PlotNodeSO` and invokes the one audited active
`UIPlotNodeList.OnNodeClick(PlotNodeSO)`. It is not a hardcoded Fruit Tree command.
For a compound request, the server selects the top tab, waits one Unity frame for its native
content hierarchy, then resolves and selects the requested subtab or plot. The whole operation
still returns one terminal tool result; callers never split it into a retry sequence.
Mods uses that identical catalog-indexed button path. Selecting Mods while it is already active is
an idempotent tab reselect and leaves its page open; the MCP does not carry a Mods-only toggle case.

```sh
tools/game-mcp-client.py catalog
tools/game-mcp-client.py navigate World --subtab Agromancy \
  --plot-node-uuid PLOT_UUID --capture artifacts/agromancy.png
```

When `capture` is true, the server waits until the destination frame has rendered and returns the
PNG inline in the same terminal response. Compound navigation captures exactly once, after the
final tab/subtab/plot selection; intermediate frames are never encoded. PNG size depends heavily on
the destination's visual entropy: the mostly dark Start screen compresses far smaller than the
dense Main HUD. MCP base64 then adds about one third to the PNG byte count, which explains why a
Main-scene navigation capture can be several megabytes even though it contains only one image.

## Tooltip explorer

The current game build makes the exploration loop feasible. Active `HoverTooltip` components carry
an `ITooltipable`, core name/type/description methods, and a private authored `subTooltips` list;
`OpenTooltip` renders the selected element. `game_tooltips` pages through current-screen elements by
an exact native hierarchy path whose sibling indices disambiguate repeated Unity clone rows.
`game_tooltip` requires one exact path and returns the native tooltip as compact typed
`TooltipNode` rows. Each row keeps native kind, evaluated text, children, linked tooltip, and
sub-tooltip documents. Per-node paths, ordinals, paint, icon ceremony, success stanzas, and
authored/evaluated duplicates are absent. Alternate nodes appear only when their semantic tree
differs from the primary tree. The
same response expands authored nested tooltips and currently open inspected
panels, with explicit cycle, depth, and node-count refusal rows. Unity rich-text markup is stripped
from every MCP string. Stable UUID identity is attached when the tooltipable is an
`IdScriptableObject`; names that cannot resolve uniquely are never guessed. Computed text
delegates run inline on the Unity thread; the reader never clicks a node or
renders a panel. The result labels its `unity_main_thread` source. Core tooltip identity and
description remain alongside the node tree,
and the tool can return an inline screenshot with the tooltip open.

```sh
tools/game-mcp-client.py tooltips --limit 25
tools/game-mcp-client.py tooltip 'EXACT/PATH/FROM/CATALOG'
tools/game-mcp-client.py tooltip 'EXACT/PATH/FROM/CATALOG' \
  --capture artifacts/tooltip.png
```

The audited manifest covers the native tooltip carrier/open/nesting shape, while the real-reference
build and the installed tooltip contract test verify the interface and `TooltipNode` shape against
the pinned game assemblies. The same audited `ITooltipable.GetDescription()` contract supplies
authored descriptions for `explain_entity` when the resolved entity implements that interface.

## Trace health and probes

`trace_health` answers operational questions that the strategist cannot answer from a world
snapshot: is the writer healthy, how many segments and records are retained, how many bytes are
being produced, and is retention or a writer fault active? It deliberately does not stream
individual automation decisions. Those belong to the trace folder and offline analysis, where
high-volume repeated decisions can be filtered without spending strategist context.

`game_probe` has exactly three names:

- `runtime`: current Unity scene/frame/time scale, lifecycle state, gameplay readiness, and Mods
  shell liveness;
- `action_queue_room`: live `ActionManager.GetRemainingRoom()`, the native boundary answer that a
  published occupancy snapshot cannot guarantee; and
- `navigation`: live tab and active-subtab counts, useful for diagnosing catalog availability.

To add a probe, add one fixed name to the router schema and closed-world policy, implement its
Unity-main-thread branch without accepting reflection/member input, declare every native type and
member in the schema-3 manifest, add installed-contract and portable behavior tests, and document
why the fact does not belong in published `WORLD`. Facts that workers or strategists generally need
belong in audited world collection instead.

## SDK decision

The server retains its small protocol layer. The official ASP.NET Core MCP HTTP transport targets
modern .NET, while the game plugin is Unity Mono `netstandard2.1`. The low-level package would still
leave the suite owning `HttpListener`, the Unity frame-operation inbox, and inline image/action
plumbing while introducing a transitive runtime dependency set that the locked installer does not
ship. The present layer is therefore narrower and is covered by protocol tests; replacing it is
appropriate only when an official transport supports this runtime without a new deployed
framework.

## Troubleshooting

The log startup marker is:

```text
Game MCP streamable HTTP server listening on http://127.0.0.1:19106/mcp
```

If `doctor` cannot connect, confirm the `perf-debug` install, Steam launch, active run, and absence
of a port collision. Never install a new suite while the game is running.
