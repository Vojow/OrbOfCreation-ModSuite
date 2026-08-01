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

HTTP workers never read Unity objects. MCP reads the suite's immutable, generation-stamped
published `WORLD`, not the game directly. New strategist visibility therefore means extending the
schema-3 audited world collector and its native-contract manifest; it does not mean adding an
ad-hoc MCP reflection read. A missing or partially collected fact returns `not_available` with an
exact code and reason.

Every world result includes:

- `worldGeneration`, naming the immutable publication;
- `structuralEpoch` and `collectedEpoch`, naming its native lifecycle;
- `collectedAtUtc`, captured beside native collection; and
- `respondedAtUtc`, when the server answered.

Gameplay commands cross a bounded primitive-only mailbox to Unity's main thread. The owning feature
adapter re-resolves the UUID, rechecks lifecycle, configuration, emergency stop, action-family
ownership, native availability, queue room, affordability, and identity immediately before
mutation. Save deletion, save import/export, run reset, arbitrary clicks, arbitrary keys,
caller-supplied reflection, native method invocation, filesystem access, and progression unlocking
are absent.

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
| `world_get` | Read one stable UUID; optional native-type assertion |
| `world_search` | Search exact projected values and UUIDs |
| `suite_health` | Compact runtime, feature, service, STOP, and mailbox health; optional exact service detail |
| `suite_configuration` | Read the single committed configuration and writable setting catalog |
| `trace_health` | Read trace-writer health, segment, record, and byte counters |
| `chronicle_status` | Read the Chronicle clock, archive/comparison, major splits, and first-visible feature-resource KPI subsections |
| `game_purchase` | Buy a structure or upgrade derived from its UUID |
| `game_cast` | Fire or release one equipped spell |
| `game_concept` | Add, remove, or rotate one concept assignment |
| `game_harvest` | Harvest an audited pair derived from a plot UUID |
| `game_spell_level` | Buy one spell mastery level or invoke level-all |
| `suite_config_set` | Commit one allowlisted setting through the configuration store |
| `suite_emergency_stop` | Engage or resume the suite's shared emergency stop |
| `game_screenshot` | Return the framebuffer as inline MCP image content |
| `game_continue` | Continue the already-selected save from the Start scene |
| `game_screen_catalog` | Discover live top tabs and current subtabs by name and index |
| `game_navigate` | Navigate a catalog tab/subtab and optional published plot UUID |
| `game_tooltips` | Page through active tooltip-bearing elements by exact indexed path |
| `game_tooltip` | Read core tooltip text and its nested tooltip links |
| `game_probe` | Read one fixed native fact not carried by `WORLD` |
| `chronicle_start` | Start a run from the latest lifecycle-valid world observation |
| `chronicle_pause` | Pause the active Chronicle clock |
| `chronicle_resume` | Resume only on the run's original lifecycle |
| `chronicle_abandon` | Abandon active timing without changing or resetting the game |
| `chronicle_select_comparison` | Select `PersonalBest`, `Previous`, or an exact compatible archived `runId` |

`chronicle_status` is also available as `orb://chronicle/status`. Chronicle commands use the same
bounded main-thread mailbox and inline terminal-result contract as the other commands, but they
make zero native calls and mutations. Starting on a progressed save marks already-satisfied splits
`Preexisting`; it never invents historical times. `World restored` finishes only when the saved
`PersistenceHasCompletedWorld` flag is observed changing from false to true during that run. Status
includes exact `elapsedTicks`, display-friendly `elapsedSeconds`, the milestone schema ID, and the
`gameplay-active-monotonic-v1` clock ID so future comparisons can reject incompatible runs.
`resourceSections` contains Magic through Restoration feature-domain groups. Each section uses
`captureMode: first-visible`, names its producer/usage `relationship`, and reports pending,
captured, preexisting, and missing row counts.
Rows capture independently when that exact resource first becomes visible, so later upgrades can
discover Arcanum under Magic or Ore under World without waiting for or rewriting a major split.
Captured resource rows expose
visibility, quantity, true quantity, true net rate, and capacity/fill facts; `resourceSchemaId`
identifies the curated catalog required for future KPI comparisons.

```sh
tools/game-mcp-client.py chronicle-status
tools/game-mcp-client.py chronicle-start
tools/game-mcp-client.py chronicle-pause
tools/game-mcp-client.py chronicle-resume
tools/game-mcp-client.py chronicle-abandon
tools/game-mcp-client.py chronicle-select-comparison PersonalBest
tools/game-mcp-client.py chronicle-select-comparison Selected --run-id <run-id>
```

`world_overview` deliberately contains only facts a strategist normally wants before choosing a
detailed read: collection completeness, unavailable categories, resource-row count, unlocked
structure count, purchasable-upgrade count, purchase-cost count, discovered/mastery-ready recipe
counts, available views, visible plots, and current action/spell/concept/plot occupancy. Exact rows
remain in list/get/search.

`world_categories` is the authoritative inventory. It reports row type, derived native type,
identity mode, row count, and exact availability. `world_get` requires category and UUID only.
`expectedNativeType`, when supplied, is a strict assertion and rejects a mismatch. Composite tables
cannot be addressed by an arbitrary related UUID; use `world_list`.

`world_list` answers “which row should I inspect?” Each category has a deliberate scan projection:
stable identity plus the small set of availability, level, quantity, occupancy, readiness, or
progress fields useful for comparing rows. Expensive modifier graphs, raw capture inputs, and
secondary calculations stay out of list/search results. `world_get` answers “tell me everything
about this exact UUID” and returns the complete projected record.

`suite_health` without arguments is likewise situational: lifecycle/configuration generations,
STOP, runtime/audit state, feature ID/state/reason code, service or collector ID/phase/fault state,
and mailbox occupancy/capacity. Each summary returns its exact stable selector. Pass
`{"detail":"EXACT_FEATURE_OR_SERVICE_ID"}` only when investigating that one feature or runner; the
response then contains its complete published status or scheduling, cycle, wake, native-evidence,
and fault record. The compact call never embeds those records.

An empty clean category means the save has no rows. A skipped native row makes exact queries for the
whole category unavailable. Searches return `world_search_incomplete` instead of presenting partial
matches as authoritative. Derived tables also require every upstream collection report to be clean.

## Inline action results

There are no receipts, pending states, cursors, or polling tool. `action_receipt` does not exist.
Every action, configuration write, STOP transition, and gadget waits for Unity's next frame and
returns its terminal result in the same MCP tool call.

Every terminal result includes:

- `status` and `disposition`;
- exact numeric `resultCode` plus `resultCodeName` for feature actions, or the exact gadget/admin
  code for non-feature commands;
- `reason`, populated on every rejection or fault;
- `nativeCallsAttempted`, `mutationAttempts`, `mutationsCommitted`, and `verifiedMutations`;
- submitted and processed UTC timestamps; and
- observed world, lifecycle, and configuration generations.

The server wait budget is 2,000 ms. Normal execution is one frame, usually under 50 ms. Exceeding
the budget is a server defect, returned loudly as `terminal_wait_timeout` with the command sequence
and kind; there is no pending fallback.

`worldGeneration` is optional decision-audit metadata. When supplied, it is echoed as
`decisionWorldGeneration`, even if minutes and many publications passed before execution. Mere age
or a future-looking value is never a rejection reason. Native revalidation is the safety boundary.

The server derives native type and action kind from the target UUID:

```sh
tools/game-mcp-client.py call game_purchase --arguments \
  '{"uuid":"STRUCTURE_OR_UPGRADE_UUID","count":1}'
tools/game-mcp-client.py call game_harvest --arguments \
  '{"plotNodeUuid":"PLOT_UUID"}'
tools/game-mcp-client.py call game_cast --arguments \
  '{"mode":"fire","slotIndex":0,"spellRecipeUuid":"SPELL_UUID"}'
```

Use `--audit-generation` with the CLI play commands to copy the motivating read generation into the
action. The default omits it, proving that strategic thinking is not time-sensitive:

```sh
tools/game-mcp-client.py purchase UUID
tools/game-mcp-client.py cast 0
tools/game-mcp-client.py harvest PLOT_UUID
tools/game-mcp-client.py concept-add UUID
tools/game-mcp-client.py spell-level UUID
```

Manual MCP actions do not require the feature's worker policy to be enabled. They do use the same
feature-owned native adapter, cooperative action-family lease, live validation, and mutation proof.
STOP closes MCP native admission exactly as it closes automation. Resume still requires the host's
ordinary fresh-world gate.

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
from the caller.

`game_screen_catalog` reads the live Main-scene UI. Top tabs retain native rail order. Current
subtabs are active `UIViewRadioButton` controls under the current native content area, ordered by
exact indexed hierarchy path. Inactive popup templates are excluded. Each entry reports its stable
label, zero-based index, and path.

`game_navigate(tab, subtab?, plotNodeUuid?, capture?)` accepts an exact label or an integer index.
Name matching is ordinal and closed-world: zero or multiple matches reject. Plot selection resolves
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
`game_tooltip` requires one exact path, returns core authored text and
the linked child tooltips structurally, and can return an inline screenshot with the tooltip open.

```sh
tools/game-mcp-client.py tooltips --limit 25
tools/game-mcp-client.py tooltip 'EXACT/PATH/FROM/CATALOG'
tools/game-mcp-client.py tooltip 'EXACT/PATH/FROM/CATALOG' \
  --capture artifacts/tooltip.png
```

This first prototype intentionally stops at structural depth one. The game's rendered
`TooltipNode` rows, which often carry computed numeric values, are not yet projected as text; the
result says so explicitly. The audited manifest covers the native tooltip carrier/open/nesting
shape, while `TooltipExplorerNativeShape_IsStructurallyReachable` verifies the interface methods
against the installed assembly.

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
leave the suite owning `HttpListener`, the Unity main-thread mailbox, and inline image/action
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
