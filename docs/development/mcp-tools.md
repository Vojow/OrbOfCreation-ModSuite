# Game MCP tooling

An engineering surface, not a player feature. The suite serves a localhost MCP endpoint from inside
Orb Of Creation when, and only when, it was built with the `perf-debug` profile. Ordinary and
release builds compile the server and every MCP gadget out, so nothing described here is reachable
from a published release.

## Connect

Install a `perf-debug` build with the game closed (see [development setup](setup.md)), then launch
the game through Steam:

```sh
./script/install perf-debug
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

World reads pin one immutable publication for the whole answer and return status/refusal evidence
plus the requested data. They do not expose world/lifecycle/configuration generations, request
fields, capture/respond timestamps, or mailbox internals. Gameplay
operations re-resolve UUID/native type and invoke the same canonical GameAction as features/tests,
including current lifecycle/configuration/emergency/ownership/native admission and one observable
outcome sentinel. Save deletion, save import/export, run reset, arbitrary clicks, arbitrary keys,
caller-supplied reflection/native invocation, and progression unlocking are absent. The complete
frame and data-lifetime contract is in
[Game MCP frame operations](../runtime-architecture/game-mcp-frame-operations.md).

The verb surface is also bounded by what the player can actually click in the pinned build, not by
what compiles. Verbs the shipped UI does not expose are absent even when a reachable native entry
point exists: `game_concept` offers only `add` and `remove_owned`, because rotating one assignment
out for another is automation policy rather than a control; `game_targeting` offers no cancel,
because the visible Close button only dismisses presentation; there is no in-place augment editor
and no way to select a discovery output by UUID; and `game_spell_level` requires
`uuid` for `single` while rejecting it for `all`, because the native Level All button
takes no target.

Every game-domain `BigDouble` is one JSON string produced by the shared MCP number formatter, never
a JSON number or a text/mantissa/exponent object. Zero is `"0"`. The formatter follows the screen:
ordinary player-scale values are plain with at most two decimals (`"26"`, `"2.2"`), while large or
small magnitudes use a normalized mantissa and lowercase `e` exponent without a plus sign
(`"1.66e8"`, `"1.23e-3"`). There is one formatter and no precision or verbosity option.

The game aggressively caches some derived values until their screen has been viewed. That upstream
behavior is not silently worked around here. If a stale cache prevents a native action, the
terminal rejection must name the stale cached fact and the screen-view condition; the MCP server
does not refresh it by hidden navigation.

## Tool surface

The registry is exactly 41 tools. It is built once per lifecycle and never changes mid-session, so
there is no `tools/list_changed` notification. The rows below are in `tools/list` order.

| Tool | Purpose |
|---|---|
| `world_overview` | Compact collection, economy, progression, and running-state summary |
| `world_categories` | Discover every published table and exact collection availability |
| `world_list` | Page compact identity-plus-scan rows in one category |
| `world_get` | Read an ordered 1–200 UUID list from one pinned immutable publication; optional native-type assertion |
| `entity_catalog` | Search every live-registry identity and available player-facing name, including loaded entities hidden by progression |
| `explain_entity` | Evaluate one UUID's gates, requirement graph, exact costs, and blockers from one pinned immutable publication |
| `world_search` | Search stable-UUID entity categories; composite diagnostic rows are excluded |
| `suite_health` | One compact runtime, feature, service, STOP, scene, and contract-health shape |
| `suite_configuration` | Read the single committed configuration and writable setting catalog |
| `trace_health` | Read trace-writer health, segment, record, and byte counters |
| `game_purchase` | Buy an Attribute (`StructureSO`) or Upgrade derived from its UUID |
| `game_cast` | Fire, release charge, or turn off one equipped toggle spell |
| `game_concept` | Add or remove one owned concept assignment |
| `game_agromancy` | Use the Agromancy screen's plot actions, harvest elements, and processing slots |
| `game_structure` | Enable or disable one available attribute |
| `game_spell_level` | Buy one spell mastery level or invoke level-all |
| `game_casting_dial` | Set the global Output Level or Reserve Level shown on the Casting screen |
| `game_spell_loadout` | Read staged Spellcraft glyphs; preview/add an explicit layout; or remove/move one equipped runtime spell |
| `game_targeting` | Submit one exact eligible target or let the native request choose one |
| `game_consumable` | Use, cancel, discard, randomize, or reorder one published consumable |
| `game_craft` | Craft a recipe or control its manual/automated instance |
| `game_discover` | Preview or confirm one composed discovery on seven surfaces, or drive one Discovery Tree offer lifecycle |
| `game_equipment` | Equip/increase or unequip/decrease an explicit amount of one created artifact |
| `game_alchemy` | Add, remove, or reorder one ordinary Alchemy recipe through its visible list |
| `game_ritual` | Select a Ritual, set its starting level, activate or end its battle, or cancel its duration reward |
| `game_level` | Buy an explicit amount of paid or bonus levels from an ordinary level-list control |
| `game_loadout` | Switch or edit the active player loadout, or save/load/clear an Equipment or Alchemy snapshot slot |
| `game_challenge` | Select, activate, abandon, or fetch the Time/prestige challenge offers |
| `game_prestige` | Confirm and perform the irreversible persistent reset |
| `game_research` | Develop/queue an explicit amount, pause, resume, cancel, or apply a free research bonus level |
| `suite_config_set` | Commit one allowlisted setting through the configuration store |
| `suite_emergency_stop` | Engage or resume the suite's shared emergency stop |
| `game_screenshot` | Return the framebuffer as inline MCP image content |
| `game_continue` | Continue the already-selected save from the Start scene |
| `game_return_to_menu` | Raise the native manual-save event and return from play to the Start scene |
| `game_modal` | Dismiss the one unambiguous open native modal through its close control |
| `game_screen_catalog` | Read compact named tabs with the active tab/subtab marked and grouped |
| `game_navigate` | Navigate a catalog tab/subtab and optional published plot UUID |
| `game_tooltips` | Page through active tooltip-bearing elements by exact indexed path |
| `game_tooltip` | Read compact plain screen text, including nested/computed and inspected content |
| `game_probe` | Read one fixed native fact not carried by `WORLD` |

`world_overview` deliberately contains only facts a strategist normally wants before choosing a
detailed read: collection completeness with total successfully read and skipped row counts,
unavailable categories, resource-row count, unlocked
structure count, purchasable-upgrade count, discovered/mastery-ready recipe
counts, available views, visible plots, current action/spell/concept/plot occupancy, and the two
global casting dials with their purchased maximums. Exact rows remain in list/get/search.

`world_categories` is the authoritative inventory. Each row reports `category`, native type,
identity mode, row count, and exact availability. Internal world-property and row-type names are
not protocol data. `world_get` always requires `category` plus either a `uuids` list or the singular
`uuid` alias; supplying both is a `mutually_exclusive` validation failure, and either form returns
the same list shape.
Composite tables cannot be addressed by an arbitrary related UUID; use `world_list`.

Supply `uuids` with 1–200 canonical UUIDs. Results preserve input order without repeating an index or UUID on
successful rows. A typed not-found or invalid result repeats the implicated UUID because that is
failure evidence. Array reads have no aggregate status: each result owns its availability, while a
category or schema failure that prevents the call from running remains a top-level refusal. Every
row comes from the same pinned publication; the server does not issue a
generation or retain a snapshot token across calls.
Localized collection gaps mark only the implicated list/search/get row unavailable and attach the
partial row plus exact evidence there; unaffected rows in the same call remain ordinary results.

Paged list and catalog reads use one pagination vocabulary: `total`, `nextOffset` when more rows
remain, and `truncated:true` when a byte bound delivered fewer rows than requested. `nextOffset`
always equals the input offset plus the rows actually delivered. There is no redundant `returned`
or `hasMore`; the collection itself is always present, including when it is empty. Search has no
cursor and therefore says `truncated:true` whenever its exact total exceeds the returned matches.

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

`world_list` and `world_get` use the same deliberate player-relevant row projection. Every entity
row leads with its primary `uuid` and `name`; composite rows promote their actionable primary
identity while retaining separately named secondary references. Rows then carry only the small set
of availability, unambiguous paid/bonus/total level, quantity, occupancy, readiness, or progress
fields useful for comparing rows. Raw capture inputs, cached implementation fields,
resource traits, rate inputs, and modifier structs stay out of world rows. `explain_entity` owns
deeper evaluated evidence. Purchase-cost rows are composite and therefore remain a `world_list`
surface.

`purchase-costs` is the only modifier-adjusted live cost category. Spell and alchemy cost rows are
immediate/drain observations and are not mislabeled as purchase prices. Every displayed cost is in
the screen's spend units: ordinary nominal costs are divided by the resource quality percent through
the audited `GetTrueSpend` formula, while bandwidth costs remain nominal. Each structure/upgrade cost row exposes
the screen's `cost`, the matching `spendableAmount`, and the resource identity needed for the
next decision. Every cost row uses `cost` for the screen price and `spendableAmount` for the
same-publication player pool, with `affordable` and a reason only when the decision was evaluated.
Ordinary resources compare their raw on-screen pool against the quality-adjusted spend; bandwidth
resources compare nominal cost against headroom using the game's integer-snapped comparison. The
resource counter's `amount` independently mirrors `GetDisplayQuantity`: inverted resources display
headroom and all others display stored quantity. Cost rows use `spendableAmount` for the native
admission operand, so independent inverted/bandwidth flags never overload one field with two
meanings. These fields use the same exact combiner as Auto Buy and do not
include Auto Buy's configurable reserve or excess policy.

A `resources` row is deliberately only named identity, the counter's on-screen `amount`,
`netRatePerSecond`, and, when the resource is capped, `capacity` plus `atCapacity`. A negative native
capacity is the game's uncapped sentinel and is never serialized as a magnitude. Inverted counters
publish their displayed headroom; all other counters publish their raw quantity. `atCapacity`
always answers whether the underlying holdings reached the ceiling, so an inverted counter can
display `amount: 0` while truthfully reporting `atCapacity: true`. Detailed
factor math will belong to a future Details-panel tool; it is not leaked through world rows.

Research rows distinguish the native evaluator's base and effective requirement levels. Their
`requirementLevelAdjustment` is the difference between those two native results, not a suite-owned
recalculation. `requirementAdjustments` carries each directly authored passive or active modifier,
including its amount and source UUID/type. This matters for challenge effects: the game installs
them as persistent/passive modifiers, so an active-only modifier count can be zero even while both
the research tooltip and native availability evaluator apply the challenge adjustment. Type-wide
research modifiers are included in the native effective result but are not misattributed as direct
members of the research row.

The same detailed row is the complete pre-decision surface for `game_research`. Uncapped research
omits `maximumLevel` rather than serializing the game's zero sentinel. It names the immediate or
queue route, live multi-buy maximum, exact number of levels the native cumulative loop will accept,
and ordered named costs paired with each resource's canonical `spendableAmount`. While development
is active it includes elapsed/required/remaining progress and per-resource investment; associated
research types carry their remaining free bonus levels and investment caps. Only currently
UI-reachable next verbs appear: `develop`, `pause`, `resume`, `cancel`, and `bonus`. A committed
mutation returns the changed level or state; read detail remains in `world_get`.

`crafting-recipe-types` describes the game's crafting families; `crafting-recipes` contains the
actual recipes and is the pre-decision surface for `game_craft`. A recipe row leads with `visible`,
`startingAmount`, `craftTimeSeconds`, and `canStart`; when false, `blockers` names the failed native
visibility, purchase, queue, page-relation, or output-capacity axis. `execution` identifies the
direct, existing-stack, or new-instance route. Page routes include current `queuedAmount` and the
named queue's used/maximum slots. `purchaseAmount` and named `nextCosts` carry the exact next cost,
canonical `spendableAmount`, and affordability before any mutation. Direct costs use the
same `recipeCost.Multiply(purchaseAmount)` lineage as native `Execute`; page costs use the same
`GetTotalCost(previous,purchaseAmount)` lineage as native `QueueCraft`. Named `types`, `inputs`,
`outputs`, and `consumableOutputs` preserve authored order. An input
uses `cost` for the recipe requirement and canonical `spendableAmount` for what is spendable now (bandwidth
headroom for a bandwidth resource); outputs use `yield`. Only failed engagement-drain evidence is
emitted in `drainBlockers`. The category is unavailable unless both recipe and resource collectors
are clean.

`crafting-queue-entries` is the ordered live contents of every loaded manual and automation queue.
Each lean row names the queue and recipe, reports its zero-based slot, current amount, and whether
the instance is automatic; only automatic entries carry their repetition count. The same
lifecycle-bound crafting reader supplies these rows and recipe decisions, so a malformed instance,
queue-role contradiction, or unstable page roster makes the category unavailable rather than
publishing a partial queue.

`world_search` deliberately indexes only categories whose identity mode is
`stable_entity_uuid`. It does not pretend that an owner/resource UUID uniquely identifies a
composite diagnostic row. If an owner UUID exists only in `entity-requirements`, search returns an
authoritative empty entity result; `world_list(entity-requirements)` retains the exact localized
owner, ordinal, and runtime type evidence. If a searchable entity row itself is returned and that
entity owns an unmodeled leaf, the search result is explicitly incomplete for that entity. Its
`total` counts only stable-identity matches that the response can actually return. The tool accepts
no offset, so a bounded result carries `truncated:true` instead of advertising an unusable cursor.

### Discovery decision loop

`discovery-trees` is the pre-decision surface for `game_discover`'s `offer_*` modes; attempting an
action is never the way to learn its cost or choices. Every row names the tree UUID/type, semantic
mode, rerolls left, discovered count, and whether discoveries remain. Authoring/debug members and
duplicate identity (`treeId`, overrides, debug mode, and bonus-level cost) are intentionally absent.
The Discovery Tree is a transient in-game event rather than a standing page, which is why its
lifecycle lives inside the one discovery tool instead of a permanent tool of its own.

In Idle mode, `initiate` reports `available`, a stable false `reasonCode` when needed, and each exact
cost line as a named `resource` plus `cost`, canonical `spendableAmount`, and `affordable`. In
Choice mode, `offers` contains named UUID/category/native-type references in native order.
`selectedOfferUuid` appears only after selection. `rerollAvailable` appears only in Choice mode. An
empty offer set omits `offers`.

These values are copied during the shared 250-millisecond world capture from lifecycle-bound
delegates for native visibility, immediate-required state, current choices, exact next cost,
affordability, and resource true quantity. The MCP worker only projects the immutable row. A choice
UUID must resolve in the same generation as an alchemy recipe, equipment, glyph, ritual, spell
recipe, or time rune. If it does not, only the implicated tree read returns
`discovery_offer_read_incomplete` with `implicatedOffers`; the UUID is never silently omitted.
Current offers are also resolvable through `world_get` and `explain_entity`, including authored
metadata and applicable discovery predicates.

### Generic discovery decisions

Every `alchemy-recipes`, `equipment`, `rituals`, `spell-recipes`, and `time-runes` row, plus each
discoverable `glyphs` row, has one `discover` decision from the native `IDiscoverable` evaluator.
Pool-unlocker glyphs instead expose prerequisite-backed `available` without advertising a discovery
action their screen does not offer. A discovery decision names whether the entity
is visible, already discovered, required for downstream play, currently discoverable, and
affordable. Its ordered `costs` pair each named resource's screen-formatted `cost` with the same
canonical `spendableAmount` used everywhere else. Failed decision axes carry a stable reason;
attempting a mutation is never required to learn affordability.

`game_discover` is the sole discovery namespace, and it is deliberately component-first. The game's
compose pages let the player select components and then resolve exactly one output; the MCP
reproduces that direction and never accepts the desired output UUID as the decision.
`mode:"preview"` and `mode:"confirm"` take one `surface` from
`spellcraft|glyphcraft|devote|runecraft|alchemy|artifacts|concepts` plus ordered `components` of
`{uuid,count}`. The server derives the target and its native type from the live resolver; there is
no target argument to select with.
Zero or multiple resolutions refuse (`discovery_recipe_unresolved`, `discovery_recipe_ambiguous`)
instead of guessing, and a component that is neither an available glyph nor a published resource,
or that asks for more uses than the glyph permits, refuses as `component_unavailable`. This is why a
partial component write can never claim a target it did not resolve.

Spellcraft resolves core glyphs through the audited spell resolver; the other six surfaces use the
installed `UIDiscoverablePage` count-plus-membership semantics against exactly one published
category — Glyphcraft→`glyphs`, Devote→`rituals`, Runecraft→`time-runes`, Alchemy→`alchemy-recipes`,
Artifacts→`equipment`, Concepts→`alchemy-recipes`. When `surface` is omitted from `preview`, all
seven resolvers are tried and a unique match reports its surface. `preview` is classified read-only and never mutates; `confirm` repeats the
whole resolution live at the action boundary before permit, payment, or discovery. The
`offer_initiate`, `offer_select`, `offer_confirm`, and `offer_reroll` modes take the tree `uuid` instead,
and are the only modes that accept a UUID choice, because the transient offer UI really does show
and select those exact entities.

The `equipment` category is also the artifact-loadout pre-decision surface. Each row names the
artifact and its primary equipment type, current/maximum stacks, global and type-slot occupancy,
usage-cost resources with current holdings, and the next equip/unequip admission or refusal. Call
`game_equipment` with `mode:"equip"` or `mode:"unequip"` and an explicit positive `amount`; the
tool never reads or mirrors the UI multi-buy strip. A committed call returns the stack
count before and after. Usage reservations, effects, and
attunement are post-state evidence, never payment-verification gates.

### Ordinary Alchemy loadout loop

An `alchemy-recipes` detail row carries `alchemyLoadout` only for the six ordinary Alchemy families.
It reports `activeCount`, the ordered slot when active, and the next visible add, remove,
and move decisions. An available add includes the live click-sized maximum and named per-use resource
costs with current spendable holdings; an unavailable add carries only its binding reason. Concept
recipes remain on `game_concept`, composed Alchemy discovery remains on `game_discover`, and recipe
leveling belongs to the unified level surface rather than this list lifecycle.

`game_alchemy(mode="add"|"remove", uuid=..., amount=...)` applies the caller's explicit positive
amount through the list's native counted mutation after revalidating live usage capacity.
`mode="move"` instead requires the zero-based
`destination` exposed by the row. Success returns only the settled `activeCount` before and after, or
the ordered slot before and after for a move. The action boundary revalidates exact recipe identity,
ordinary-family classification, discovery, and capacity before invoking the explicit-count core
used by the UI wrappers, or the same list-swap route as the UI. The global multi-buy strip is never
read or changed.

### Ritual lifecycle

Ritual discovery remains `game_discover(surface="devote")`. Once discovered, a `rituals` detail
row reports the selected Ritual, current/reached/maximum starting level, battle state, and active
duration-reward state. Only the selected row carries activation and completion prices in the same
player-facing units as the Ritual panel and the eventual resource spend;
unselected rows do not publish a speculative ledger. `setLevel`, `activate`, and
`cancelDuration` each carry only the binding availability or refusal reason that affects the next
decision.

`game_ritual(mode="select"|"deselect"|"activate"|"end"|"cancel_duration", uuid=...)` reproduces the
corresponding visible Ritual control. `mode="set_level"` also requires the zero-based `level`
shown by the Ritual screen. The old runestone-selection manager methods are empty/null-returning in
v1.0.5 and are deliberately absent. Activation revalidates the selected Ritual and the screen's
native price before payment; success is the settled battle transition. `cancel_duration` ends an
already-running duration reward and does not claim to cancel a battle. Selection, level, battle,
and duration activity each use one game-written outcome sentinel and never a resource ledger.

### Unified level controls

The `equipment-types`, `glyphs`, `resource-types`, and `time-runes` detail rows are the complete
pre-decision surface for their ordinary level-list buttons. Each row distinguishes paid, bonus,
and total levels and carries a `purchase` decision. Equipment types, glyphs, and resource types
also carry `bonus`; time runes do not implement that native control. Available decisions include
the exact named native usage cost and current spendable amount. Inapplicable or unavailable
controls do not publish priced ledgers.

Call `game_level(mode="purchase"|"bonus", uuid=..., amount=...)`. The tool derives the exact native type from
the published category, repeats the visible button's live admission on Unity's main thread, and
returns only the settled paid- or bonus-level change plus the resulting total. The paid route
checks the game's persistent usage cost but does not perform a one-time payment; the concrete
native level callback applies its own usage/effects. Research development and spell mastery stay
on `game_research` and `game_spell_level`, respectively.

### Agromancy

Every `agromancy-elements` detail row joins the exact active-element count, the next visible
add/remove decision, its stored output and rate, and the element's offered harvest actions. An available element add includes
its named standing usage costs and current spendable amounts. Each offered action reports its
active/maximum count, add/remove availability, and the named resource drain for the **next**
instance. An unavailable control carries only the reason that binds the next decision; no priced
ledger is computed for an action the screen cannot run.

Call `game_agromancy(mode="add_element"|"remove_element", uuid=..., amount=...)` for one exact
`HarvestElementSO`. The `add_element_action` and `remove_element_action` modes additionally require
`actionUuid` naming an action actually
offered by that element. Every mode requires an explicit positive `amount`; no mode depends on
hidden selector state.

The action boundary revalidates the concrete element/action pair, visibility, active-list room,
standing usage capacity, and mastery-derived action maximum on Unity's main thread. Success returns
only the active count before and after plus the settled next decision for the affected element or
pair. The one mutation sentinel is that game-written active count moving in the requested
direction; resource reservations and drain math are planning facts, never postcondition ledgers.

The `plot-nodes` category is the tile catalog. `agromancy-plot-actions` enumerates every
`PlotNodeSO` / `PlotNodeActionSO` pair authored by the game, not only Auto Harvest's fruit and
treasure collect pairs. Each row names both handles,
shows the active quantity, and carries add/remove decisions. An available add includes the plot
quantity consumed by one instance and the current maximum additional count. An unevaluated
prerequisite latch omits `available` and reports `requiresLiveCheck:true`; the action boundary
performs the exact native check instead of a read mutating the latch.

Call `game_agromancy(mode="add_plot_action"|"remove_plot_action", uuid=..., actionUuid=...,
amount=...)`. Every call requires an explicit positive `amount`.
Add uses the same active plot-action list control as `UIPlotNodeActionList.OnActionClick`.
Remove decrements an existing quantity; at the native minimum it uses that UI handler's distinct
`Cancel()` path, so crossing from several instances through the last one requires two calls.
Success returns the observed active quantity change and the settled next decision. The only
postcondition is the exact pair's game-written active quantity moving in the requested direction;
refund behavior on cancellation is neither recomputed nor verified.

`agromancy-processing` is the screen's top processing strip in screen order. Each row reports its
slot, whether it is empty, the strip capacity and occupancy, and—when occupied—the named plot,
named action, amount, and whether it is processing. The former helper categories for harvest
controls/resources, plot instances, and raw action-queue internals are not public MCP categories;
their facts are joined into these three player-facing surfaces.

### Structure enable and disable

Every `structures` detail row reports the attribute's current `enabled` state and the one next
toggle the player can take. An unavailable structure carries only `not_available`; the MCP does
not expose the native callback until the same availability fact the screen uses is true.

Call `game_structure(mode="enable"|"disable", uuid=...)` with a published structure UUID.
The boundary revalidates exact `StructureSO` identity, availability, and current state on Unity's
main thread, then invokes the screen's `ToggleDisabled()` route. Success returns only the settled
`enabled` value before and after. The single postcondition is the game-written `disabled` flag
reaching the requested state; `ApplyEffects` and `RemoveEffects` remain native consequences of
that callback and are not independently replayed or audited by the suite.

### Alchemy screen ownership

Alchemy's Learn side uses `game_discover(surface="alchemy")`. Its Loadout side uses
`game_alchemy` with the published recipe pool, ordered capacity-bounded slots, six type-capacity
counters, and the same type identity the screen filter displays. Recipe mastery and Alchemy-type
levels are game-driven progression displays, not direct purchase buttons on this screen.

There is deliberately no Brewing Station tool or category. The v1.0.5 data contains one unnamed
legacy `CraftingStructureSO` asset, but its runtime instance list is authored empty and the entire
data graph has no unlock/effect edge that creates one. The assembly still contains the unused
`UIBrewingStation` renderer, but no player-facing label or live screen owns it. Publishing its
native selectors as a verb would expose developer-era machinery the shipped UI does not offer.

### Player loadouts and snapshots

`player-loadouts` lists the named, stable player loadout UUIDs. A detail row reports whether the
loadout is selected, whether its Equipment and Alchemy sections are enabled, the current icon and
color indexes, whether the native manager can switch now, and the named saved spell, Equipment,
and Alchemy entries. `game_loadout(mode="select", uuid=...)` invokes the manager's whole
save/deactivate/load/reactivate transaction after revalidating every stored reference's identity,
native type, role, and whole-loadout capacity. Current glyph ownership is deliberately not a
selection precondition: the native screen accepts authored saved layouts whose construction
choices are no longer available. Success returns the observed selection change and the settled
selected loadout.

The selected player row also owns the three controls visible in the editor:
`set_section` requires `section:"equipment"|"alchemy"` plus `enabled`; `rename` requires a name
of at most 24 characters; `next_icon` and `next_color` advance one step through the same native
lists as the UI. Arbitrary icon/color indexes and a free-standing save verb are absent because the
screen exposes neither.

`snapshot-loadouts` identifies both the Alchemy and Equipment snapshot-list owners; each detail
row exposes its kind, visible zero-based slots, populated state, and named saved entries.
Snapshot rows themselves have no UUID, so `snapshot_save`, `snapshot_load`, and `snapshot_clear`
take the owning list `uuid` plus `slot`. Save accepts only an empty slot, load/clear only a
populated slot, and no overwrite mode exists. Optional native type must match the owning
`AlchemySnapshotListVariable` or `EquipmentSnapshotListVariable`. Success returns the observed
slot or active-section change from the settled world; usage capacity is admission only and no
resource ledger is returned.

### Challenge decision loop

The `challenges` category is both the per-entity read and the pre-decision surface for
`game_challenge`. Every row carries the native idle/queued/active/passed/failed state, current and
maximum level, native next difficulty/reward, availability/completion verdicts, selection and offer
membership, and explicit `select`, `activate`, and, when active, `abandon` decisions. Challenge
selection has no resource price, so a row does not invent empty costs or affordability.

Every detailed challenge get/search response also carries one same-publication `challengeState`: ordered
fully named `selected`, `timeOffers`, and `prestigeOffers`; selection capacity; first-fetch state;
rerolls; and explicit `fetchTime`/`fetchPrestige` availability. This shared state is captured once
on Unity's main thread with the ordinary world and projected by reference.

`challengeState.prestige` is the persistent-reset pre-decision surface. It reports the current,
projected, and previous persistence values, reset count, the fully named persistent resource with
its current spendable amount and real capacity semantics, queued prestige challenges, queued
rewards, and the exact `reset.available` decision. No attempt/refusal is needed to learn whether a
reset can run.

The MCP-only sequence is:

1. Page `challenges`; compare next difficulty/reward and the named ordered offers from
   `challengeState`.
2. Call `game_challenge(mode="select", uuid=...)`; its terminal response returns the changed target
   state.
3. Call `activate` to toggle an offered target's activation state, or `abandon` for an active
   target.
4. Call `fetch_time` or `fetch_prestige` without a UUID. The terminal response returns the complete
   replacement named offer lists and remaining next decisions; no read-back is required.
5. When the prestige decision is available, call `game_prestige(confirm=true)`. Success waits for a
   newer world after the native scene reload and returns the new scene, `prestigeState`, and
   `challengeState` inline. The explicit boolean prevents an empty or accidental call from
   triggering the irreversible reset.

The MCP-only offer sequence is seven calls when two offers need explanations:

1. `world_list(category="discovery-trees")` and choose a named Idle row whose
   `initiate.available` is true.
2. Call `game_discover(mode="offer_initiate", uuid=...)`; its terminal response waits for and
   returns the named ordered Choice offers.
3. Call `offer_reroll` when `rerollAvailable=true`; its terminal response returns the replacement
   offers.
4. Call `explain_entity` for the candidates that require comparison. No catalog name joins are
   needed because every reference already carries its name.
5. Call `offer_select` with that `offerUuid`; its terminal response includes the selected offer.
6. Call `offer_confirm` with the same UUID; its terminal response is the Idle tree plus the next
   initiate costs. There are no post-mutation `world_get` calls, snapshot tokens, or receipt polls.

### Spell discovery and loadout-add loop

`spell-recipes` is the pre-decision surface for both Spellcraft discovery and loadout add. Each
named row contains the authored ordered `coreGlyphs` with current owned and bonus levels, every
equipped runtime instance of that recipe, and the shared `loadBudget` of used/maximum slots plus
`fitsAnotherSpell`. An undiscovered recipe exposes `discover`, including the `surface` and the
ordered `components` to submit; a discovered recipe exposes `loadoutAdd`. Discovery carries its
named exact costs, spendable amounts, affordability, and stable false reason. Loadout add truthfully
reports only structural admission (`loadout_full` or `core_glyphs_unavailable`) plus
`requiresGlyphLayout:true`: its price depends on the explicit augments that have not yet been
chosen. There is no selection step and no target-first `create`: the game exposes neither.

The MCP-only base-recipe sequence is:

1. Page or search `spell-recipes`; compare names, core-glyph holdings, discovery costs, and
   affordability.
2. For an undiscovered recipe, call
   `game_discover(mode="preview", surface="spellcraft", components=[...])` with that row's
   components and check the resolved output, then repeat the call with `mode:"confirm"`. The
   response reports the resolved target and discovery transition.
3. If an equipped instance is wanted, call
   `game_spell_loadout(mode="preview", uuid=..., glyphs=[...])`. This read resolves and prices the
   submitted layout through the same native manager methods used by add, without touching the
   player's staged UI selection. It returns the named resolved recipe, named per-resource costs,
   overall affordability, and the named short resource when unaffordable.
4. Call `game_spell_loadout(mode="add", uuid=..., glyphs=[...])` with that same layout. Adding is
   the only mutation where the layout is chosen; it is baked into the created runtime spell.

Every referenced entity is named inline. No catalog join, world-generation argument, payment
stanza, receipt poll, or post-mutation `world_get` is required.

### Casting dial loop

Output Level and Reserve Level are the two sibling global steppers on the Casting screen, not
per-spell settings. `world_overview` carries them as `casting.output` and `casting.reserve`, each
with `current` and the purchased `maximum`; the block is absent until the Output maximum is nonzero.
Raising a cap is an ordinary `game_purchase` against the corresponding upgrade UUID, so the dial
tool only moves the value inside the live native range.

`game_casting_dial(dial="output"|"reserve", value=N)` is the whole surface. `value` is a positive
integer checked against the live native maximum at the action boundary, which reads the exact global
`IntVariable` for that dial and verifies the requested value became observable. A committed result
returns the changed dial as `before` and `after` plus its maximum.

There is deliberately no in-place augment editor. The visible game has none: glyph layout is chosen
on the library candidate before add, and changing it is remove → relayout → re-add. A discovered
recipe's `loadoutAdd.augmentOptions` names owned spell-augment glyphs only where choosing them is the
next decision.

### Spell loadout loop

`spell-slots` is the pre-decision surface for `game_spell_loadout`. Each occupied detail row contains
the named runtime spell and recipe, exact slot, active cast/ready/attune state when applicable, the
game's current remove verdict, and that spell's move destinations. Augment choices appear only on a
discovered recipe's `loadoutAdd` decision. `loadBudget` — `used`, `maximum`, and
`fitsAnotherSpell` — rides on every detailed `spell-recipes` row, so capacity is known before add.

The MCP-only loadout sequence is:

1. Call `game_spell_loadout(mode="staged")` when the current Spellcraft core/augment selection is
   relevant. The request-scoped read returns ordered named `core` and `augments` stacks and does
   not acquire mutation ownership or change the UI selection.
2. Read `world_list(category="spell-slots")` and choose one exact runtime `spellInstance.uuid`, or
   read a discovered `spell-recipes` row's `loadoutAdd` decision to add a new one.
3. Call `game_spell_loadout(mode="preview", uuid=..., glyphs=[{uuid,count}, ...])` to resolve and
   price that exact layout without changing the staged UI selection. `[]` is a valid intentional
   empty augment layout, not "reuse whatever the UI last selected".
4. Call `game_spell_loadout(mode="add", uuid=..., glyphs=[...])` with the previewed layout.
5. Call `game_spell_loadout(mode="move", uuid=..., destination=...)`; success returns the slot
   change.
6. Call `game_spell_loadout(mode="remove", uuid=...)` only when that row's `remove.available` is
   true; success returns the removed spell's former slot.

`staged` accepts no other field. The `uuid` means a recipe for `preview`/`add` and a runtime spell
instance for `remove`/`move`.
`glyphs` belongs to `preview`/`add` only; `destination` belongs to `move` only. Anything else is a
named `unexpected_for_mode` validation failure rather than a silently ignored field.

Add reproduces the library button's own admission order: it creates the native candidate, applies
the recipe's selected level and the requested glyphs, then requires recipe usage requirements,
computed usage-cost affordability, unique-spell compatibility, loadout capacity, per-glyph usable
counts, and non-level glyph requirements before payment, which is taken last. Remove and move
re-resolve the runtime UUID and the native remove verdict or slot range on the Unity main thread.
Every mode acquires the family permit last and verifies only requested identity/outcome. Weight,
glyph usage, drain, and resource accounting are observations, not gates. There is no generation,
payment, receipt, request echo, catalog join, or post-mutation read-back.

### Targeting decision loop

`targeting` is the pre-decision surface for `game_targeting`. It is empty while no target request
is pending. Its active row names the requesting effect, identifies the native selection kind,
reports the game's own `cancelAvailable` flag, and carries every eligible structure in native order.
Each candidate is fully named and includes current committed/effective level, availability, and
work-in-flight state. Costs and affordability are absent because targeting spends no resource.

The MCP-only targeting sequence is:

1. Read `world_list(category="targeting")` and compare its named ordered candidates.
2. Call `game_targeting(mode="submit", uuid=...)` to submit one exact candidate, or
   `game_targeting(mode="randomize")` to let the native request choose and immediately submit.

There are only those two modes. The visible Close button dismisses the targeting presentation and
does not cancel the gameplay request, so MCP exposes no cancel verb rather than reaching past the UI
into the owning effect result. Submit and randomize success return the named submitted structure.

### Consumable decision loop

`consumables` is the pre-decision surface for `game_consumable`. Each row contains its named
identity, amount and queued amount, level holdings, family types, immediate and held costs with
current resource amounts, native affordability/use admission, pending usages, current inventory and
hotbar placements, and every same-list destination. The row's `use`, `cancel`, `discard`, and
optional `randomization` objects are the next decisions; no trial action is needed to learn them.

The MCP-only consumable sequence is:

1. Read `world_list(category="consumables")` and choose from named costs, holdings, usages, and
   action verdicts.
2. Call `game_consumable(mode="use", uuid=...)` or
   `game_consumable(mode="cancel", uuid=...)`.
3. Call `game_consumable(mode="discard", uuid=..., amount=...)` for a positive amount,
   `game_consumable(mode="set_randomization", uuid=..., enabled=...)`, or
   `game_consumable(mode="move", uuid=..., list="inventory|hotbar",
   destination=...)` for a zero-based same-list position.

Every committed mode returns the changed amount, flag, or slot. There is no
payment stanza, receipt, world-generation argument, catalog join, or post-mutation read-back.

### One-shot crafting decision loop

`crafting-recipes` carries everything needed to choose a one-shot craft: player-facing identity,
native route, purchase amount, exact named costs and current holdings, affordability, queue
identity/room/current quantity, outputs, and blockers. The MCP-only sequence is:

1. Read `world_list(category="crafting-recipes")` or batch exact recipes through `world_get`.
2. Choose a row whose `canStart` is true after comparing `nextCosts`, outputs, and queue state.
3. Call `game_craft(uuid=...)`.

Success returns the changed recipe quantity or queue fact.
It has no receipt, payment stanza, world-generation argument, or read-back requirement. A timed
recipe without one stable loaded authored page refuses rather than guessing a queue; failure after
native work names the one missing direct, instant-stock, or queued-recipe outcome. Auto Scribe calls the same GameAction with
its own existing planner, so MCP crafting does not create a second Scribe implementation.

### Entity explanation

`explain_entity` accepts one canonical `uuid` and pins the latest immutable world publication before
it resolves or evaluates anything. Its entity row, predicates, requirements, costs, and blockers
all come from that one publication;
the tool neither retains a snapshot token nor follows a newer publication during the call.
Named identity appears once. When the resolved native entity implements the audited `ITooltipable`
contract, its authored `GetDescription()` text leads the explanation after identity; no description
is invented when that source is absent. The `state` row uses the same curated player surface as
world reads rather than serializing the collector's complete internal struct.

Only applicable predicate slots are present: `visible`, `available`, `canDevelop`, `canPurchase`,
`canDiscover`, and `canUse`. Presence means applicable. Each slot carries `value` and a stable
`reasonCode`; absence means the predicate does not apply, not false. Crafting purchase uses the
published `CraftingRecipeSO.CanBuyAt(GetStartingQuantity())` verdict, spell use uses the equipped
`Spell.CanCast()` reading, and structure/upgrade purchase combines published native availability
with the one exact-cost affordability lineage. No predicate emits implementation provenance or a
permanent never-evaluated apology.

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
The worker compares its graph verdict with that same-publication native answer. Missing inputs,
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

`suite_health` has no arguments or detail mode. It is compact text: scene, runtime availability,
native-contract availability, the direct-craft plus crafting-instance binding health claimed by
`game_craft`, modal-dismiss binding health, emergency STOP, then feature and service names grouped
by state and reason code. Seven identical NotReady features therefore occupy one line, not seven objects. It
returns no structured payload because none of those labels is a handle for another call. It reads
those owners only for the requested operation and reports no MCP
queue internals.

An empty clean category means the save has no rows. A skipped native row normally makes exact
queries for the whole category unavailable. The deliberate exception is an unmodeled entity
requirement leaf: the collector publishes that leaf with its owner UUID, container, ordinal, and
runtime condition type. When those rows reconcile exactly with the skipped count, `world_get` and
`world_list` keep other owners authoritative, while `world_search` localizes the evidence only when
a returned stable entity owns the leaf. An entity get/list/search that touches the affected owner
returns that row with `status: unavailable`, `reasonCode: entity_data_incomplete`, and exact
`implicatedSkippedRows`; unaffected rows remain ordinary available results. A UUID found only in a composite row
remains outside search coverage and is diagnosed through `world_list`. If even one skipped read cannot
be tied to a published owner/leaf, the category-global refusal remains. Derived tables also require
every upstream collection report to be clean.

## Inline action results

There are no receipts, pending states, cursors, or polling tool. `action_receipt` does not exist.
Every action, configuration write, STOP transition, and gadget waits for Unity's next frame and
returns its terminal result in the same MCP tool call.

A committed gameplay mutation then goes through one shared settlement rather than a per-tool sleep
or poll: it waits up to one second for a world captured after the action completed and projects the
changed fact from exactly that immutable world. A prompt publication returns immediately, and there
is no routine lag field on the ordinary path. If no such world arrives in time, the mutation stays
committed and the response carries the single exceptional
`postStateUnavailable / post_state_timeout` fact instead of an empty success or the pre-mutation
world. Reaching that path repeatedly in live play means a missing publication trigger to diagnose,
not a timeout to lengthen.

A successful read uses `available`; an unavailable domain read uses `unavailable`. A successful
mutation uses `committed`; a refused mutation uses `refused`; infrastructure or native divergence
uses `faulted`. Success adds only the settled delta and omits a code that would restate
`committed`. Refusals and faults add a stable `code`, one actionable `reason`, and only the identity,
admission, or missing-outcome facts that
made it true. Counters, request echoes, generations, payment stanzas, and decomposed receipts are
absent. There is one canonical shape per tool and no verbosity option.

JSON tool data is emitted once in `structuredContent`; `content` appears only for actual inline media
such as screenshots, and success omits the false `isError` default. The server does not repeat the
structured payload as a text item or emit an empty media array, avoiding a second client-side parse
and text-channel truncation. Invalid arguments return all detected schema
shape errors together under `error.data.validationErrors`, with distinct `missing_required` and
`unexpected_field` codes.

A faulted GameAction is still a completed MCP tool invocation: it omits `isError`, and its domain
`status`, stable `code`, actionable reason, and one relevant fact remain in `structuredContent`.
`isError=true` is reserved for infrastructure failures that happen before a canonical action
terminal exists. This distinction prevents clients from replacing the domain result with an opaque
generic tool error.

The server waits up to 2,000 ms for Unity to claim a request. If the request is still pending, it is
atomically canceled as `request_canceled_before_claim` and can never execute. Once Unity has claimed
it, the operation owns execution and the worker waits for the real terminal result; a local timeout
cannot precede a hidden later mutation. There is no pending fallback.

No schema accepts `worldGeneration`. An operation pins its current world internally, actions
revalidate live identity and mutable facts at the GameAction boundary, and the generation counter
never becomes caller ceremony.

Where a target UUID is supplied, the server derives its native type and action kind from that UUID;
where components are supplied, it derives the target from the live resolver instead:

```sh
tools/game-mcp-client.py call game_purchase --arguments \
  '{"uuid":"ATTRIBUTE_OR_UPGRADE_UUID","amount":1}'
tools/game-mcp-client.py call game_agromancy --arguments \
  '{"mode":"add_plot_action","uuid":"PLOT_UUID","actionUuid":"PLOT_ACTION_UUID","amount":1}'
tools/game-mcp-client.py call game_agromancy --arguments \
  '{"mode":"add_element_action","uuid":"HARVEST_ELEMENT_UUID","actionUuid":"HARVEST_ACTION_UUID","amount":1}'
tools/game-mcp-client.py call game_cast --arguments \
  '{"mode":"fire","slotIndex":0,"uuid":"SPELL_UUID"}'
tools/game-mcp-client.py call game_cast --arguments \
  '{"mode":"toggle_off","slotIndex":0,"uuid":"SPELL_UUID"}'
tools/game-mcp-client.py call game_discover --arguments \
  '{"mode":"preview","surface":"spellcraft","components":[{"uuid":"GLYPH_UUID","count":2}]}'
tools/game-mcp-client.py call game_discover --arguments \
  '{"mode":"offer_select","uuid":"TREE_UUID","offerUuid":"OFFER_UUID"}'
tools/game-mcp-client.py call game_casting_dial --arguments \
  '{"dial":"output","value":4}'
tools/game-mcp-client.py call game_spell_loadout --arguments \
  '{"mode":"staged"}'
tools/game-mcp-client.py call game_spell_loadout --arguments \
  '{"mode":"preview","uuid":"SPELL_RECIPE_UUID","glyphs":[{"uuid":"GLYPH_UUID","count":2}]}'
tools/game-mcp-client.py call game_spell_loadout --arguments \
  '{"mode":"add","uuid":"SPELL_RECIPE_UUID","glyphs":[{"uuid":"GLYPH_UUID","count":2}]}'
tools/game-mcp-client.py call game_challenge --arguments \
  '{"mode":"select","uuid":"CHALLENGE_UUID"}'
```

`game_discover`'s offer modes require the tree `uuid`, require `offerUuid` for `offer_select` and
`offer_confirm`, and reject it for `offer_initiate` and `offer_reroll`; `surface` and `components`
are rejected for every offer mode, and `uuid`/`offerUuid` are rejected for `preview` and
`confirm`. Initiate and reroll verify the exact tree/type and immediate transition to Crafting;
select verifies the requested offered UUID became selected; confirm verifies that exact UUID became
discovered. Payment deltas, reroll values, counters, flags, timers, list cleanup, and selection
cleanup are neither outcome gates nor response data. This matters when a cost is below the ULP of a
very large `BigDouble` amount: an unchanged amount cannot disprove a transition the game visibly
performed. On success, payment is presumed and completely omitted. Initiate/reroll wait for the
ordinary collector to publish the resulting Choice state and return its named ordered offers;
select returns the selected state; confirm returns Idle plus the next initiate costs. Failures
name only the failed admission or missing transition and the fact that explains it.

`game_cast` uses the visible spell button's native route. `fire` starts a ready spell, `release`
lets go of the suite's charge hold, and `toggle_off` presses an already-active toggle spell again.
The last mode requires the slot still to contain the exact recipe UUID and native `Spell`, the spell
still to be a currently casting toggle, the visible cast button to remain available, and the
player's Cancellable Spells setting to allow the press. Its one outcome sentinel is the native
casting state changing from active to inactive. The settled response is only the named recipe,
slot, and observed `active` before/after change; a refusal names the binding setting or live spell
state. Detailed `spell-slots` rows expose `toggleOff.available` so the setting never has to be
learned by attempting the action.

`game_casting_dial` requires `dial` plus a positive `value` and takes no UUID at all, because both
Output Level and Reserve Level are single global variables. The boundary reads the exact global
variable and its purchased maximum on the Unity main thread, rejects a value outside that live
range, and verifies that the requested value became observable. Success is the exact requested
global value; a committed result is the dial's `before` and `after` value plus its maximum.

`game_spell_loadout` requires `mode`. `staged` is a request-scoped main-thread read with no other
arguments; it reports the exact current core/augment selection and never mutates it. For `preview`
and `add`, `uuid` is a spell-recipe
identity and an explicit `glyphs` array is required. Preview combines the recipe's authored core
with those explicit augments and prices the resulting layout through
`SpellManager.GetSpellCreateCost` without changing the staged UI
selection or acquiring mutation ownership. For `remove` and `move`, `uuid` is a runtime
spell-instance identity and `glyphs` is rejected; `move` additionally requires a zero-based
`destination`, which no other mode accepts. Add builds the native candidate, applies the selected level, bakes the glyph layout
with `Spell.SetAugmentGlyphs` before the manager add route, pays last, and verifies the exact
requested loadout outcome. Remove rechecks the game's live `Spell.CanRemove()` verdict; move
re-resolves the source slot and invokes the same native swap-plus-notify path as the spellbook.
Success is the exact added instance, exact target absence, or the exact target at its destination.
A committed result returns only the recipe identity and slot change; a failure names only the unmet
admission or missing outcome.

`game_targeting` has two conditional shapes. `submit` requires one target `uuid`; `randomize` rejects
it. Submit re-resolves that UUID within the live native candidate list and reruns the request's
native target verdict immediately before mutation. Randomize invokes the game's own random choice
and immediately submits that result; it is not a candidate-only shuffle. Success is exact
submitted-object identity plus retirement of the original request. A committed result names the
submitted target; a failure names only the rejected target or missing submission.

`game_consumable` has five conditional shapes. `use` and `cancel` require only a
consumable `uuid`; `discard` also requires positive `amount`; `set_randomization` requires
`enabled`; and `move` requires `list` plus zero-based `destination`. Fields belonging to another
mode are rejected. The boundary re-resolves the exact `ConsumableSO`, all live verb predicates,
and the current list/source/destination on the Unity main thread, then captures the shared
ConsumableUse/MultiBuy permit last. Success is the requested queue, exact usage cancellation,
clamped holding removal, randomization flag, or exact destination. Payment and downstream effect
accounting do not gate success; a committed result returns only the changed amount, flag, or slot.

`game_craft` requires one recipe `uuid`. Its optional mode defaults to `craft` for compatibility and may be
`craft`, `automate`, `cancel_manual`, or `cancel_automation`. `craft` re-resolves the exact
recipe, authored page/queue route, native purchase amount, affordability, and room on Unity's main
thread, then captures the shared crafting permit last. Direct recipes invoke native
`CraftingRecipeSO.Execute`; page recipes re-drive the audited stack/new/instant
`UICraftingPage.QueueCraft` sequence.

The other modes use the same authored page relation and exact recipe identity.
`automate` repeats the UI's native multi-buy and automation-quantity calculation before calling
`CraftingInstanceListVariable.AutomateCraft`; the two cancel modes call the exact manual or
automated instance route shown by the UI. The recipe row publishes manual queued amount and the
automated quantity/capacity needed for the next decision. Success is one settled quantity change;
refund accounting is neither computed nor used as a gate.

`game_discover`'s composition modes require `surface` plus `components` and accept no target UUID.
`preview` resolves the
component multiset against the published roster for that one surface and returns the single named
output, its costs, holdings, affordability, and blockers; it is a read and mutates nothing.
`confirm` derives the target the same way from the admitted immutable world, then rereads both
native recipes and every exact live component before repeating native visibility,
already-discovered, `CanDiscover`, exact cost, and affordability checks on Unity's main thread. It
captures the shared family permit last, then preserves the UI's `PerformCost`-before-`Discover`
ordering. Success is the exact resolved target becoming discovered and returns that named target
plus its discovered transition and surface. It carries no receipt or payment stanza. A refusal occurs before payment and
restores any temporary UI selection staged for native resolution. A fault names the single missing
discovery outcome. A composition that resolves differently than the caller expected is
preview or refusal evidence, never a wrong-target mutation.

`game_equipment` requires `mode` and one published equipment `uuid`. On Unity's main thread it re-resolves
the exact artifact and repeats creation, current stacks, global and primary-type slot room, maximum
stacks, and native usage-affordability checks before taking the family permit. The caller's explicit
`amount` must fit that live maximum exactly; an oversized equip or unequip refuses with the maximum
the current state permits and never clamps to a different mutation.
Success is only the exact requested target-stack transition. It returns the target's stack count
before and after, with no receipt or payment/usage stanza. A missing transition
faults that attempt; a throw after the exact transition commits.

`game_challenge` requires one of `select`, `activate`, `abandon`, `fetch_time`, or
`fetch_prestige`. The three target modes require a published `ChallengeSO` `uuid`; both fetch modes
reject it. `activate` is the player-facing name for the native queue toggle, which is why no `queue`
mode exists on the wire. The boundary rereads the exact manager/list graph and target state on
Unity's main thread, checks offer membership, selection room/restrictions, active/queued state,
world-cycle completion, and rerolls, then captures the `ChallengeLifecycle` permit last. Select
verifies exact membership inversion; activate verifies the exact idle/queued toggle; abandon
verifies the exact
target becomes failed. The first fetch verifies the game's fetched flag; later fetches verify that
the reroll count decreased. Offer contents, rewards, effects, and other accounting are neither
success gates nor response data. Fetch returns the new named offer state because it is the next
decision; target modes return the changed challenge state. No success receipt or follow-up read is
required.

`game_prestige` requires `confirm:true`. The boundary rereads the reset manager's world-cycle
completion and challenge-fetch flags plus the persistent reset count on Unity's main
thread, then captures `PrestigeLifecycle` ownership last. The public native method merely schedules
the operation behind a screen fade, so MCP invokes the exact audited private transaction directly;
that transaction performs persistent-state preservation/reset, activates queued rewards and
prestige challenges, updates the persistent resource, and reloads the scene. Success is gated only
by the exact lifecycle replacement. Resource and counter movements are not ledger gates. A native
throw or a returned transaction without lifecycle replacement faults that attempt.
After success, the response waits for the newer post-reset world and carries its scene plus complete
prestige and challenge next-decision state, with no receipt, payment stanza, or read-back call.

`game_research` requires `mode` plus one published `ResearchSO` `uuid`. Modes are `develop`,
`pause`, `resume`, `cancel`, and `bonus`. The boundary re-resolves that exact identity and rereads
queue mode, multi-buy, level/cap/range evaluators, exact cumulative costs, current state,
investment/progress, and free bonus capacity on Unity's main thread before capturing the
`ResearchLifecycle` permit last. Develop calls the same native `PurchaseLevel` dispatch as the UI;
pause, resume, cancel, and bonus call their exact native methods. Success is only the requested
identity/outcome: active development or increased total queued levels, paused/resumed state, idle
with an empty queue, or one added self-bonus level. Cost, investment, resource, type-counter, and
progress-clock movements never gate success. A missing transition faults that attempt; a throw
after the exact outcome commits. Every committed mode returns the changed level or state with no
receipt, payment stanza, or read-back.

No MCP fault installs a persistent family quarantine. A faulted or refused request leaves nothing
behind: the next call returns to the same action boundary and revalidates current identity, native
type, availability, affordability, and mutable state from scratch. Automation's own quarantine
semantics are unchanged and are not shared with these player-driven wrappers. For the same reason,
MCP gameplay tools are registered from runtime capability admission rather than automation
configuration, so disabling Auto Scribe or any other feature cannot make the corresponding manual
verb disappear.

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

`suite_configuration` returns the startup-built `writableSettings` catalog and its current
serialized values. It never reflectively serializes the
runtime configuration record or exposes compiler metadata and internal nested policy objects.

`suite_config_set` commits through `AutomataConfigurationStore`, the same single publication path
as the in-game controls. BepInEx
parse/domain validation runs before publication. Compatibility acknowledgements, shortcuts, and
STOP are not generic writable settings.

## Screenshots and navigation

`game_screenshot` has no required parameters and returns an MCP `image` content block with
`mimeType: image/png`. `maxWidth` bounds the encoded image between 320 and 4,096 pixels and defaults
to 1,280, which is the control for response size. The response reports the encoded `width`,
`height`, and `scene`, and echoes nothing else.
`{"save":true}` additionally writes a server-generated, collision-resistant name under the current
trace folder. The caller supplies no basename, and there is no per-process filename cap.

```sh
tools/game-mcp-client.py screenshot --output artifacts/current-screen.png
```

`game_continue` is deliberately separate from tab navigation. On `Start` it invokes the audited
native `SaveStateManager.StartGame` method for the save the player has already selected. It cannot
select, delete, reset, import, or rewrite a save, and it accepts no native type, method, or UI input
from the caller. Its success waits for the transition and returns the new `scene` and
`runtimeAvailable` state.

`game_return_to_menu` is the opposite lifecycle boundary. On `Main` it invokes the visible
`UIBackToMenuButton.BackToMenu` callback, which raises the game's authored manual-save event before
requesting the literal `Start` destination. The response is completed as soon as the native screen
fade becomes active, before scene teardown can invalidate the HTTP operation. Its compact success
is `status: committed, scene: Start`; the scene transition then clears every lifecycle-retained
world, identity, binding, and lease through the ordinary lifecycle observer. The tool cannot choose
a save, suppress the save event, select another scene, or run while another transition is active.

There is deliberately no process-exit tool. Both installed quit entry points call
`UnityEngine.Application.Quit` directly and expose no game-written state that can be verified while
the process remains able to deliver an MCP response. See
`docs/reverse-engineering/clean-exit-boundary.md` for the audited drop.

`game_modal(mode="dismiss")` drives the visible close control on the one open native `UIModal`.
It refuses when there is no modal, when more than one modal makes the target ambiguous, or while
the native grace period still disables closing. The action invokes `UIModal.CloseModal()`, verifies
the game-owned closing flag, then watches that exact modal for up to the shared one-second
settlement bound. A completed close returns `open: false`; timeout remains committed and says the
post-state is unavailable because the verified close already began. It does not click modal-specific confirm, purchase, reset, or
destructive buttons.

`game_screen_catalog` reads the live Main-scene UI. Top tabs retain native rail order. Current
subtabs are active `UIViewRadioButton` controls under the current native content area. Inactive
popup templates are excluded. The response is a structured `scene` plus ordered `tabs`, each with
its `label` and `active` flag; the active tab additionally carries `subtabStrips`, where every
independent strip names its `active` label and its ordered `labels`. Unity hierarchy paths and
unstable numeric indexes are deliberately absent. Inactive tab content is not instantiated, and the
audited v1.0.5 data and scene assets do not carry an authoritative tab-to-subtab roster. The catalog
therefore omits inactive subtabs rather than navigating speculatively or guessing labels.

`game_navigate(screen, subtab?, uuid?, capture?, maxWidth?)` accepts exact labels only. Name matching is
ordinal and closed-world: zero or multiple matches reject with the exact candidate labels. Plot selection resolves
the supplied UUID as a published `PlotNodeSO` and invokes the one audited active
`UIPlotNodeList.OnNodeClick(PlotNodeSO)`. It is not a hardcoded Fruit Tree command.
For a compound request, the server selects the top tab, resolves and selects the requested subtab
or plot, then waits up to one second for the active tab and complete live strip set to remain stable
across frames. A timeout stays committed but returns only `postStateUnavailable`; it never labels a
mid-transition strip set or capture as settled. The whole operation
still returns one terminal tool result; callers never split it into a retry sequence.
Mods uses that identical catalog-indexed button path. Selecting Mods while it is already active is
an idempotent tab reselect and leaves its page open; the MCP does not carry a Mods-only toggle case.

```sh
tools/game-mcp-client.py catalog
tools/game-mcp-client.py navigate World --subtab Agromancy \
  --uuid PLOT_UUID --capture artifacts/agromancy.png
```

When `capture` is true, the server waits until the destination has settled and returns the
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
`game_tooltip` requires one exact path and returns the tooltip as compact plain screen text. The
catalog includes the owning UUID when the assigned tooltip item is itself an identity-bearing game
entity; control-only rows retain the volatile current-screen path and name. The
reader walks the native node, linked-tooltip, nested-tooltip, and currently inspected-panel graph
on Unity's main thread, but its node structure, repeated paint, empty arrays, duplicate authored
text, and identical alternate tree are wire-internal ceremony and never ship. A cycle or hard
depth/node bound is rendered as one explanatory line rather than recursively expanding forever.
Unity rich-text markup is stripped. Computed text delegates run inline; the reader never clicks a
node, renders a panel, or captures the framebuffer.

```sh
tools/game-mcp-client.py tooltips --limit 25
tools/game-mcp-client.py tooltip 'EXACT/PATH/FROM/CATALOG'
```

The audited manifest covers the native tooltip carrier/open/nesting shape, while the real-reference
build and installed contracts verify the source node graph that the prose renderer consumes. The
same audited `ITooltipable.GetDescription()` contract supplies authored descriptions for
`explain_entity` when the resolved entity implements that interface.

## Trace health and probes

`trace_health` answers operational questions that the strategist cannot answer from a world
snapshot: is the writer healthy, how many segments and records are retained, how many bytes are
being produced, and is retention or a writer fault active? It deliberately does not stream
individual automation decisions. The answer is compact text because it exposes no follow-up
handle. Individual decisions belong to the trace folder and offline analysis, where
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
