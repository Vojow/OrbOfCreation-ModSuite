# Discovery Tree offer native pipeline

> **Evidence status: implemented and installed-contract verified against the pinned 1.0.5
> assembly; the MCP-pure list/decide/initiate/reroll/explain/select/confirm journey was supervised
> live through round 4; the complete twelve-item disposable-save promotion remains open.** Round 5
> changes response projection and documentation only; it did not start the game, load a save, or
> execute a live mutation.

[Back to reverse-engineering index](README.md) ·
[Player-verb inventory](player-verb-inventory.md) ·
[Game boundary doctrine](../runtime-architecture/game-boundary-doctrine.md)

## Capability and scope

`DiscoveryTreeOfferGameAction` is the one mutation definition for inventory verbs `V-DISC-02`
through `V-DISC-05`:

| Action kind | Player verb | Native data entry point |
|---|---|---|
| `Initiate` | buy/start the next discovery offer | `DiscoveryTreeSO.InitiateCraftingMode()` |
| `Select` | choose one currently offered item | `DiscoveryTreeSO.SelectItemId(Guid)` |
| `Confirm` | discover the selected offer | `DiscoveryTreeSO.DiscoverSelectedItem()` |
| `Reroll` | spend a reroll and generate another offer set | `DiscoveryTreeSO.RerollChoices()` plus `SelectItemId(Guid.Empty)` |

The four commands are one capability because they share one native state machine, one registry and
identity contract, one quarantine boundary, and one cooperative action-family lease. Features,
tests, and `game_discovery_offer` consume this definition; there is no MCP-only implementation.

The capability does not navigate the UI, advance the crafting timer, synthesize offers, poll, hold
a snapshot token, or touch a save. Installed IL proves that the synchronous Idle-to-Crafting
transition has no UI/render dependency. Offer materialization remains owned by the game's normal
main-thread increment loop.

## Static evidence

The pinned `Assembly-CSharp.dll` metadata and IL establish these exact native methods:

| Method | Token | Relevant effect |
|---|---:|---|
| `InitiateCraftingMode()` | `0x06000AA8` | refreshes the earned reroll when applicable, clears `usedRerollsLastDiscover`, fetches rarity levels, enters Crafting |
| private `EnterMode(DiscoveryTreeModes)` | `0x06000AA7` | writes `actionMode`, zeroes `actionTime`, increments the passive observable counter |
| private `EnterCraftingMode()` | `0x06000AA9` | calls `EnterMode(Crafting)` then plays the start sound |
| private `EnterChoiceMode()` | `0x06000AAA` | enters Choice and creates `currentChoiceIds`; resets to Idle if no choice can be made |
| `SelectItemId(Guid)` | `0x06000AAB` | assigns `selectedChoiceId` only |
| `DiscoverSelectedItem()` | `0x06000AAC` | resolves the selected UUID and calls `DiscoverItem` |
| `DiscoverItem(IDiscoverable)` | `0x06000AAD` | increments counts, removes rarity, resets mode/offers/selection, calls `Discover`, refreshes discovery flags |
| `RerollChoices()` | `0x06000AB1` | copies current offers to exclusions, clears offers, debits reroll, sets used-reroll, enters Crafting |
| `GetMaxRerolls()` | `0x06000ACB` | supplies the exact cap used by initiate's `Math.Min` reroll assignment |
| `GetNextItemCost()` | `0x06000AB8` | returns required-item cost or next main-pool item cost |
| `GetItemFromGuid(Guid)` | `0x06000ABF` | resolves an `IDiscoverable` in this tree |

`UIDiscoveryTreePage` proves UI reachability:

- `OnDiscoveryClick` (`0x0600232B`) checks Idle then calls `InitiateCraftingMode`;
- `OnDiscoveryItemClick` (`0x0600232C`) reaches `SelectItemGuid`, which calls `SelectItemId`;
- `OnConfirmClick` (`0x0600232D`) calls `DiscoverSelectedItem`;
- `OnRerollClick` (`0x0600232E`) calls `RerollChoices`.

`UICostButton.OnClick` (`0x06002204`) checks `ResourceCostList.HasEnough()`, invokes
`PerformCost()`, and only then invokes the discovery callback. `InitiateCraftingMode` does not pay.
The suite therefore re-drives the same ordering at the data layer: every suite-owned read and
decision, then the mutation permit, then `PerformCost`, then initiate. Installed tests pin the UI
edges and the affordability-before-payment ordering.

The iteration-1 token audit closes the suspected UI-context gap. `InitiateCraftingMode`
references, in IL order, `usedRerollsLastDiscover` (`0x04000646`), `rerollsLeft`
(`0x04000645`), `GetMaxRerolls` (`0x06000ACB`), `FetchRarityLevels` (`0x06000AB3`), and
`EnterCraftingMode` (`0x06000AA9`). `EnterCraftingMode` calls `EnterMode` (`0x06000AA7`), and
`EnterMode` writes `actionMode` (`0x04000643`) and `actionTime` (`0x04000644`) before calling
`PassiveObservable.UpdateObservable` (`0x06001DD9`). That observable only increments its
`observedId`; it does not invoke UI code. The later `IncrementCrafting` (`0x06000AA0`) calls
`EnterChoiceMode`, and an empty choice result may then reset to Idle. Screen rendering is therefore
neither a precondition nor a legitimate explanation for a normally returning initiate that is
already Idle during immediate verification.

## Complete lifecycle binding set

The action binds the complete set once per lifecycle, compiles strongly shaped delegates, and
retains no native object. Execution performs no field, method, overload, or type lookup. Every
entry below participates in a data-driven omission test; withholding any one makes the entire
capability `ContractUnavailable`.

| Contract group | Exact members |
|---|---|
| tree identity/registry | exact `DiscoveryTreeSO`; static `All : List<DiscoveryTreeSO>`; inherited `GetGuid() : Guid` |
| state | `actionMode`, `actionTime`, `rerollsLeft`, `usedRerollsLastDiscover`, `currentChoiceIds`, `nextExcludedIds`, `selectedChoiceId`, private `totalDiscoveredCount`, private `poolDiscoveredCount` |
| state gates | `IsVisible`, `IsInIdleMode`, `IsInCraftingMode`, `IsInChoiceMode`, `HasCurrentlyRemMainPoolDiscoveries`, `HasImmediateRequiredDiscover`, `GetMaxRerolls` |
| tree operations | `GetNextItemCost`, `GetItemFromGuid`, `InitiateCraftingMode`, `SelectItemId`, `DiscoverSelectedItem`, `RerollChoices` |
| offer identity/state | exact `IDiscoverable` and `IHasGuid`; `IHasGuid.GetGuid`; `IDiscoverable.IsDiscovered`; `IDiscoverable.IsDiscoverRequired` |
| wrapped UUID | exact `GuidContainer`; `get_guid() : Guid` |
| exact cost | exact `ResourceCostList`, `ResourceTuple`, `ResourceSO`; `GetEntries`, `HasEnough`, `PerformCost`, tuple `resource` and `GetValue`, resource `GetGuid` and `GetTrueQuantity` |

A live tree is resolved for every command from `DiscoveryTreeSO.All` by stable UUID plus exact
runtime type. Zero matches and duplicate matches both fail closed. Offer identity is similarly the
current offered UUID plus exact `IDiscoverable` resolution; names are never authority.

Scene, save-load, reset, and NG+ invalidation discard the delegate set's lifecycle state, clear
quarantine, rebuild all bindings, and still reject any action carrying the old lifecycle epoch.

## Pre-decision world publication

The action binding set above remains the mutation boundary. Separately, the ordinary shared world
collector publishes the minimum facts a strategist needs before invoking it. This is not an MCP
snapshot or request-time reflection path: the collector reads on Unity's main thread at the existing
250-millisecond world cadence, copies UUIDs/booleans/integers/`BigDouble` values into
`WorldDiscoveryTree`, and the immutable generation is later projected by MCP by reference.

| Published decision fact | Exact native source | Reader contract evidence |
|---|---|---|
| stable tree identity | inherited `DiscoveryTreeSO.GetGuid()` | existing id-scriptable reader contract |
| visible/admissible tree | `DiscoveryTreeSO.IsVisible()` | `0x06000AD5` |
| semantic mode and reroll evidence | fields `actionMode`, `rerollsLeft`, `usedRerollsLastDiscover` | existing Discovery tree state contracts |
| immediate-required path | `HasImmediateRequiredDiscover()` | `0x06000AC6` |
| ordered current offers | `currentChoiceIds` plus `GuidContainer.get_guid()` | `0x04000647`, `0x06001B44` |
| exact next-item cost object | `GetNextItemCost()` | `0x06000AB8` |
| native affordability | `ResourceCostList.HasEnough()` | `0x06001E0F` |
| exact component costs | `ResourceCostList.GetEntries()`, `ResourceTuple.resource`, `ResourceTuple.GetValue()` | `0x06001E50`, existing tuple-field contract, `0x06001F96` |
| cost resource identity/spendable amount | inherited `ResourceSO.GetGuid()`, `ResourceSO.GetTrueQuantity()` | existing resource-identity contract, `0x060012BE` |

Every member is resolved and compiled with the reader's complete lifecycle binding set. A missing
type, member, exact return shape, list element type, null cost/resource, empty offer UUID, or duplicate
offer UUID fails the Discovery collector category; there is no partial binding or execution-time
lookup. Lifecycle replacement rebuilds the entire reader set together with the rest of the world
collector.

Idle rows read the exact native cost only when a remaining or immediate-required discovery exists.
Choice rows copy every offer in native order. MCP resolves each copied offer UUID against the same
generation's explainable entity categories. Installed metadata proves the game's complete
`IDiscoverable` family is `AlchemyRecipeSO`, `EquipmentSO`, `GlyphSO`, `RitualSO`,
`SpellRecipeSO`, and `TimeRuneSO`. An absent or wrong-family UUID makes only that implicated tree
read incomplete and reports its tree UUID, offer UUID, and ordinal; unaffected entities and trees
remain authoritative.

The player-facing projection discards authoring/debug fields already captured for other world
diagnostics (`debugMode`, override variables, discovery bonus-level cost, and duplicate `treeId`).
Every entity reference carries UUID plus player-facing name, category where addressable, native
type, and internal name only when different. Idle exposes exact named cost lines as `cost` plus
canonical spendable `amount`; Choice exposes ordered named offers, optional `selectedOffer`, and
`rerollAvailable`. The published data supports a seven-call two-offer journey with no catalog name
joins and no post-mutation world reads.

## Admission order

Every mode checks, in order:

1. Unity main-thread identity;
2. lifecycle quarantine and complete binding availability;
3. action epoch equals the live lifecycle epoch;
4. exactly one exact-type tree UUID in the live registry;
5. native tree visibility;
6. mode-specific native state and identity gates;
7. complete before-state and, for initiate, exact-cost capture;
8. cooperative `DiscoveryTreeOfferLifecycle` mutation permit; and
9. the native call sequence, with no later policy or metadata decision.

Configuration generation and STOP are checked by the common MCP admission boundary before the
GameAction. The action has no independent feature policy or configuration. The native pipeline has
no action queue or capacity gate. No invented queue/cap check is reported.

## Mode contracts and outcome verification

### Initiate

Preflight requires Idle, `IsVisible`, and at least one of
`HasCurrentlyRemMainPoolDiscoveries` or `HasImmediateRequiredDiscover`. It binds the exact
`GetNextItemCost` object, requires `HasEnough`, and aggregates duplicate cost rows by stable
resource UUID. Failure receipts retain expected cost, before/after spendable amount, and observed
delta; success has no payment stanza.

After the final permit the action invokes:

```text
ResourceCostList.PerformCost()
DiscoveryTreeSO.InitiateCraftingMode()
```

The GameAction success proves only the same exact tree/type identity established by preflight and the immediate
native transition to Crafting. A normally returning call that stays Idle is still quarantined: the
requested transition did not happen. The MCP operation then waits for a newer ordinary world
publication whose tree is in Choice and returns that named ordered offer state in the same call; it
never fabricates offers or polls from the HTTP worker.

The round-2 live receipt had costs `4.4e3` and `8.9e6` against amounts `2.1e19` and `5.7e23`.
`BigDouble` could not represent either subtraction, so both amounts remained byte-for-byte
unchanged even though the same native call immediately changed mode from Idle to Crafting and the
UI later displayed three offers. This proves that ledger equality cannot verify the action.

| initiate observation | posture | why |
|---|---|---|
| tree UUID and exact `DiscoveryTreeSO` type | gate | stable target identity; ambiguity or wrong type can invoke the wrong object |
| immediate mode | gate | Crafting is the requested native transition identity |
| action time | evidence | timer initialization is bookkeeping, not whether Crafting began |
| current/pending offers | evidence | offers materialize later in the native increment/render lifecycle |
| current rerolls | evidence | native clamp bookkeeping may change independently of the mode transition |
| maximum rerolls | evidence | explains the clamp but is not the requested outcome |
| used-reroll flag | evidence | lifecycle accounting, not target or transition identity |
| total/pool discovered counts | evidence | diagnostic ledger; initiate requests no particular counter delta |
| costs and before/after amounts | failure evidence | native `BigDouble` subtraction can be sub-ULP at large amounts |
| payment-invoked/charged flags | failure evidence | records what could be observed after refusal/fault; success presumes native payment and omits the stanza |

### Select

Preflight requires Choice, exact membership in ordered `currentChoiceIds`, exact item resolution,
and `IsDiscovered=false`. It deliberately does **not** call `IDiscoverable.CanDiscover`.
`CollectDiscoveryChoices` can intentionally offer future choices whose ordinary prerequisite
verdict is false; native offer membership is the UI's authoritative eligibility gate. A portable
contract pins selectable `CanDiscover=false` future choices.

Success proves only that the requested offered UUID became native `selectedChoiceId`. MCP returns
the newer named Choice row with `selectedOffer`; action-local counters are not repeated.

### Confirm

Preflight requires Choice, current offer membership, exact item resolution, not already discovered,
and native `selectedChoiceId` equal to the requested offer UUID.

Success proves only that the exact requested target became discovered. MCP returns the newer Idle
tree row with discovered count and next initiate costs; payment and action-local counter stanzas are
omitted.

The native method changes counts and resets the tree before calling `IDiscoverable.Discover`.
Therefore an exception can leave a real partial commit. The receipt preserves every observed count,
mode, offer, selection, and target-discovered fact. If the requested target is observed discovered,
the action commits despite the exception; if it is not, the ambiguous partial transition
quarantines the capability.

### Reroll

Preflight requires Choice, a nonempty offer set, `rerollsLeft > 0`, and no immediate-required
discovery. The native UI does not expose reroll for the immediate-required path.

Native `RerollChoices` leaves the old `selectedChoiceId` in place while the tree spends time in
Crafting. The suite immediately calls the already-bound data-layer selector with `Guid.Empty`; it
does not call UI code. This additional cleanup prevents a stale old selection from surviving until
new offers appear.

Success proves only the immediate transition to Crafting for the exact preflighted tree. Timer,
reroll debit, used flag, current/excluded offer lists, selection cleanup, and discovery counters are
evidence. MCP returns the later named Choice publication in the same call. A selection-cleanup
exception after Crafting is evidence and still commits; an exception or mismatch that leaves the
tree outside Crafting quarantines.

## Partial commit and quarantine

After an exception, the verifier captures the strongest available after-state before classifying
the result. If the requested identity/outcome is present, the action commits and the exception is
receipt evidence. If the requested outcome is absent after a native mutation began, the terminal
result carries zero verified commits, the exact stage (`Payment`, `Initiate`, `Select`, `Confirm`,
`Reroll`, `ClearSelection`, or `Verification`), and an exact reason. Only that wrong/missing/ambiguous
outcome quarantines the family for the lifecycle.

Portable tests cover sub-ULP unchanged amounts, independent drift of every evidence-only axis,
partial first-row payment, initiate-before-transition failure, confirm failure after count/reset but
before discover, missing selection, reroll failure, and selection-cleanup failure after a successful
Crafting transition. Lifecycle invalidation is the only quarantine reset.

## MCP surface

`game_discovery_offer` accepts:

- `mode`: `initiate`, `select`, `confirm`, or `reroll`;
- `treeUuid`: a published Discovery Tree UUID;
- `offerUuid`: required only for `select` and `confirm`;
- optional exact `expectedNativeType=DiscoveryTreeSO` assertion.

The HTTP worker copies identities and scalars only. Unity's main thread runs the shared GameAction.
The terminal response is inline. A commit contains `status=committed` plus the newer named tree
state: initiate/reroll return Choice and ordered offers, select returns `selectedOffer`, and confirm
returns Idle plus next costs. It has no world generation, success code/reason, request echo,
payment stanza, or action-local counters. A refusal or fault retains `reasonCode`, reason, named
target, and decomposed preflight/stage/outcome, quarantine, before/after tree, cost, and payment
evidence when capture reached native execution. There is no receipt ID, pending state, polling
endpoint, verbosity option, or retained native reference.

## Disposable-save promotion checklist (not executed)

The following checks require Marvin/orchestrator participation and a disposable save. Each check
must record save identity, game build hash, tree UUID, before/after screenshots, MCP request/result,
and visible-UI corroboration of the returned post-state.

Before each check, start from a separately copied disposable slot whose source copy is closed and
untouched. Obtain every tree/offer identity, cost, affordability, selection, reroll decision, and
target state from `world_list`/`world_get`/`explain_entity`; visible UI and MCP screenshots are
corroboration, never decision input. Record the complete pre-state: tree mode, offers and selection,
discovery counts, rerolls, exact displayed resource quantities/cost, and target discovery state.
Invoke exactly one listed MCP mode once; do not combine checklist items in one observation window.

1. On an Idle tree whose published `initiate.available=true`, record MCP cost rows and current
   rerolls, initiate once, and verify the returned Choice state and named offer set. Record displayed resource deltas and
   maximum-reroll clamp as corroborating evidence. If the receipt itself reports Idle, stop:
   pinned IL says that is contract divergence, not missing screen context.
2. Repeat initiate on an immediate-required discovery and verify required cost selection, no reroll
   UI, and the required offer behavior.
3. Attempt unaffordable initiate and verify zero resource/mode/reroll/count change.
4. Resolve and explain each published offer in one generation, then select each offered item in a
   multi-choice set, including a future-choice offer if one appears;
   verify only the highlight/selected UUID changes.
5. Submit a UUID not in the current offer set and verify zero mutation and `offer_unavailable`.
6. Confirm a main-pool offer; verify the exact requested UUID became discovered, and record total,
   pool, Idle reset, reveal, and effect side effects as evidence.
7. Confirm a required offer; verify the exact requested UUID became discovered, and record total,
   pool, and Idle-reset evidence.
8. Reroll a multi-offer set with a selected item; verify the returned replacement Choice offer set,
   and record reroll debit, old-offer exclusions, and selection cleanup as evidence.
9. Attempt reroll with zero rerolls and on an immediate-required offer; verify zero mutation.
10. Engage STOP between read and execution and verify common admission reports zero native calls.
11. Force a lifecycle replacement between read and execution and verify the stale action refuses;
    then repeat with a fresh lifecycle command.
12. After every successful mode, save and reload only through ordinary player controls, then verify
    the requested selection/discovery/mode outcome persisted where applicable and record displayed
    resources, counters, and rerolls as ledger evidence.

Abort immediately on an unrecognized game hash, ambiguous tree/offer identity, a pre-state that
does not match the checklist item, wrong target/type, a missing requested transition, missing
receipt evidence, quarantine, or UI/native disagreement about the requested outcome. Preserve any
ledger discrepancy or exception as evidence; neither can downgrade an already observed requested
outcome. Do not retry, continue to another item, or attempt an automated rollback after possible
commit.

After an item passes, exit and save only through the ordinary player controls required by that
item. After evidence review, dispose of the tested slot and create the next test slot anew from the
untouched source copy. If an item aborts, retain its slot for diagnosis until Marvin/orchestrator
authorizes disposal; never promote or copy that state into an active save.

No checklist item was executed during implementation. Live validation is the promotion gate, not
evidence silently implied by portable or installed-contract tests.
