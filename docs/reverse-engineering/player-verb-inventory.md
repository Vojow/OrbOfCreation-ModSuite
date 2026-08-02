# Player verb inventory (Orb of Creation 1.0.5-2)

This dossier is the static map for driving a complete playthrough through the
suite. It describes what the audited game exposes; it is not permission to
invoke any pipeline. An entry marked `none` or `partial` remains unavailable to
automation until its ordered build item is implemented, gated, and later
promoted on a disposable save.

## Scope, sources, and repeatable sweep

The native source of truth was the SHA-pinned contract fixture at the absolute
path
`/Users/marvin/repos/OrbOfCreation-ModSuite/artifacts/game-v105/Orb Of Creation_Data/Managed/Assembly-CSharp.dll`.
The paired
`/Users/marvin/repos/OrbOfCreation-ModSuite/artifacts/game-v105/Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll`
was also enumerated and contained zero types whose full name starts with `UI`;
it remains relevant for `BigDouble` shape, not player commands. The sweep used
Mono.Cecil, never loaded Unity, and never opened a save. Candidate UI command
methods were enumerated with the following repeatable predicate:

```text
types: full name starts with UI
methods: On.*Click|Click.*|HandleClick|FireActionButton|OnDrop|OnEndDrag|
OnBeginDrag|Open|Close|Toggle.*|Select.*|Set.*|Save.*|Load.*|Clear.*|
Import.*|Export.*|Delete.*|Back.*|Quit.*|Manual.*|Cancel.*|Discard.*|
Purchase.*|Develop.*|Resume.*|Pause.*|Add.*|Remove.*|Queue.*|Discover.*|
Create.*|Fetch.*|Submit.*|Randomize.*
```

For each candidate the method body, called members, fields written, and all
callers of the terminal domain method were inspected. Metadata tokens below are
from that DLL. The authored screen universe was independently enumerated from
all 97 `ViewSO` rows and all 52 global `KeyBindingVariable` rows in
`data/entity-display-names.tsv`. Generated binding expectations were
cross-checked in `data/native-contracts.json` and
`tests/OrbModding.GameContractTests`.

Existing reverse-engineering dossiers searched in full were:

- `docs/reverse-engineering/README.md`
- `docs/reverse-engineering/alchemy-domain-classification.md`
- `docs/reverse-engineering/architecture.md`
- `docs/reverse-engineering/audit.md`
- `docs/reverse-engineering/auto-buy-native-pipeline.md`
- `docs/reverse-engineering/auto-buy-queue-and-completion.md`
- `docs/reverse-engineering/auto-buy-raw-fact-inputs.md`
- `docs/reverse-engineering/auto-buy-stage-profiles.md`
- `docs/reverse-engineering/auto-items-native-pipeline.md`
- `docs/reverse-engineering/auto-scribe-native-pipeline.md`
- `docs/reverse-engineering/economy-mechanics.md`
- `docs/reverse-engineering/entity-catalog.md`
- `docs/reverse-engineering/entity-correlations.md`
- `docs/reverse-engineering/evidence-strength.md`
- `docs/reverse-engineering/identity-and-registries.md`
- `docs/reverse-engineering/in-game-vocabulary.md`
- `docs/reverse-engineering/modding-hooks.md`
- `docs/reverse-engineering/resources-and-bigdouble.md`
- `docs/reverse-engineering/save-system.md`

Action coverage was checked against `GameMcpProtocolRouter`,
`GameMcpCommandBus`, every `*GameAction` and `*NativeAdapter` under `src/`, and
the portable/profile/installed-contract action tests. `full` means the suite
already exposes one shared, fail-closed mutation capability through MCP;
feature-only actions without MCP are deliberately `partial`.

### Evidence limits

Mono.Cecil proves IL calls and writes, not serialized UnityEvent wiring or live
presentation. `EG-01` marks a UI event whose serialized listener cannot be
proved from `Assembly-CSharp.dll`; the named terminal method is real, but the
connection needs prefab inspection or disposable-save observation. `EG-02`
marks refund/rollback details whose called native method was identified but
whose resource delta must be observed. `EG-03` marks selection observables that
may be saved even though their inspected IL only changes UI selection. `EG-04`
marks save-slot/menu availability whose scene/prefab wiring is not present in
the DLL. `EG-05` marks an operation whose native ordering commits state before a
later cost call. These labels are unresolved gaps, not confirmed contracts.

## Normalized audit vocabulary

Risk is damage-oriented: `R0` is ephemeral UI state; `R1` is reversible
presentation, order, or preference state; `R2` is reversible gameplay
allocation; `R3` spends resources or queues progression; `R4` can partially
commit durable progression/loadout state; `R5` resets, imports, overwrites, or
deletes durable save state.

Every disposable-save checklist begins with `C0`: use a disposable copy; record
save UUID, lifecycle/config/world generation, target UUID+native type, full
resource/list/queue state, and trace sequence; invoke exactly once on the Unity
main thread; compare the stated success/refusal receipts; reload that disposable
copy and compare durable state; repeat every refusal without mutation. The row
then requires one or more exact extensions:

- `C-UI`: prove only the named selected view/modal/order/preference changed and
  gameplay resources, action queues, progression, and save checksum did not.
- `C-PAY`: test exact funds, one-unit-short, modifier-active, multi-buy, and
  queue-full cases; reconcile every resource delta and queued/completed level.
- `C-DISC`: test hidden, visible-but-ineligible, affordable, unaffordable,
  already-discovered, reroll-empty, and offer-reload cases; reconcile offer IDs,
  rerolls, discovered/created state, costs, and unlocked dependents.
- `C-LIST`: test empty, one, maximum, duplicate/stack, multi-buy, reorder, and
  removal; reconcile ordered stable UUIDs, stack counts, capacity/usage/drain,
  effects, and exact resource deltas.
- `C-QUEUE`: test room/no-room, cancellation before/after progress, pause/resume,
  lifecycle invalidation, and completion; reconcile identity, quantity,
  investment/refund, progress, effects, and terminal queue state.
- `C-COMBAT`: test no target, invalid/dead target, valid target, insufficient
  cost, cooldown, queued release, and scene invalidation; reconcile target,
  cost, cooldown/queue, damage/effects, and refusal reason.
- `C-RESET`: record the entire disposable save before/after; test confirmation,
  no eligible challenge, selected challenges, reroll, reset, reload, and
  post-reset unlocks/resources; no real save may be used.
- `C-SAVE`: use an isolated disposable slot; test malformed/foreign payload,
  occupied/empty slot, cancellation, reload, and failure; byte-compare all
  non-target slots and prove no write on refusal.

Receipts in the tables are the minimum observable evidence an action must
return. `refusal` always includes stable target identity, attempted quantity or
choice, the failed native gate, zero resource/list/queue delta, and no commit.
`partial` means the before/after evidence identifies exactly which native step
committed before a later divergence; it is never reported as success.

## Player verb catalog

### UI, navigation, preferences, and process

| Verb | UI route and native pipeline | Preconditions/gating | Ordered effects and exact receipts | Existing coverage and named gap | Risk and disposable-save check | Dependencies; playthrough priority |
|---|---|---|---|---|---|---|
| `V-UI-01` navigate screen/tab/subview/plot | `UIViewList.HandleClick` `0x06002962` -> `CoreViewManager.ChangeCoreView` `0x060004E7`; subviews -> `ChangeSubView` `0x060004E8`; `UIPlotNodeList.OnNodeClick` `0x060025A5` selects plot | authored view visible/unlocked; requested stable `ViewSO` UUID; plot visible for plot route | selected core/subview/plot and render activation change; receipt: requested/resolved view UUID, before/after selected view and plot UUID, `committedUiState=true`, zero gameplay/save mutation | **full**: `game_screen_catalog` + `game_navigate`; classification is **UI-only, no gameplay/save mutation**, not read-only; existing mutation-audit fields remain required | `R0`; `C0+C-UI`, including invalid/hidden UUID and idempotent navigation | none; P0 because every later visible UI route depends on it |
| `V-UI-02` open/close modal, context menu, sidebar, or thought card | `UIModalActivator.ToggleModal` `0x0600254C`; `UIContextMenu.OnButtonClick/Close` `0x060022A6/A4`; `UIContentArea.ToggleLeftSidebar/ToggleRightSidebar` `0x0600244F/52`; `UIRasteredThought.Open/Close` `0x060025DA/DB` | control instantiated; modal/context action available; thought nonempty | active/paused/expanded UI flags and selected context action change; receipt: surface ID, before/after visibility/paused state; context actions additionally defer to their mapped gameplay verb | **none** as a generic UI command; context action mutations must use their mapped GameAction, never this UI capability | `R0`, or mapped verb's risk; `C0+C-UI` and prove closing cannot trigger context action | `V-UI-01`; P3 convenience |
| `V-UI-03` search/filter/sort/page a list | `UIBasicFilter.SetSearchText/SetFilterType` `0x06002373/72`; `UIGlobalSearch.SetSearchText` `0x060024DA`; generic list sort/paging controls | owning view open; filter value/type valid | filter/search/page/sort observable changes and list re-renders; receipt: prior/new query, sort/filter/page and ordered visible UUIDs; no domain collection mutation | **none** generic MCP command; `world_search` is read-only data search, not UI list state | `R0`; `C0+C-UI`, hidden and zero-result queries | `V-UI-01`; P3 diagnostics/convenience |
| `V-UI-04` inspect/follow a tooltip node | `UITooltipContainer.OnTooltipableClick` `0x060027B7`; `UITooltipNode.ClickNode` `0x060027D4`; inspect/show global bindings | tooltipable visible or an inspected nested node exists | tooltip stack/inspection panel and highlights change; receipt: root/selected UUID, typed node path, before/after tooltip depth; no gameplay delta | **partial**: `game_tooltip_catalog/read` reads current shallow UI state; no complete nested/computed inspected-node model and no UI inspect command | `R0`; `C0+C-UI`, nested dependent/requirement and closed-tooltip cases | `V-UI-01`; P2 diagnosis; M2 owns read-depth gap, M3 only UI-state control |
| `V-UI-05` pin/unpin/reorder a pinned object | `UIPinnedObjectList.OnPinnedClick/OnDrop/OnEndDrag` `0x06002582/7F/80` -> pinned list set/swap/remove | tooltipable pinnable; slot room for pin; valid drag indices | ordered pinned UUIDs and slot values change; receipt: before/after ordered UUIDs, target/index; no gameplay resources/effects | **none** | `R1`; `C0+C-UI+C-LIST`; verify reload to determine persistence (`EG-03`) | `V-UI-04`; P3 |
| `V-UI-06` change multi-buy/value selector | global `InputManager` shift-multibuy binding; `UIValueSelectButton.SetValue` `0x06002230`; `UINumberToggleButton.SelectValue` `0x0600277D` | value within native clamp; owning action supports quantity | global/request-local selected quantity changes; receipt: before/after value and clamp; no gameplay change until a later verb | **partial**: MCP action requests accept explicit amounts for existing actions; no generic selector command | `R0`; `C0+C-UI`, min/max/out-of-range and later action isolation | none; P1 dependency for batched actions |
| `V-UI-07` rebind/clear a key | `UIKeyBindList.OnKeyBindClick/ClearAll/ClearActive/ClearConflicts` `0x060024E4-E7` -> `KeyBindingVariable` assignment | settings modal open; key valid; conflict policy accepted | binding/conflicts and saved settings change; receipt: binding UUID, before/after key, cleared conflict UUIDs, settings-save result | **none** | `R1`; `C0+C-UI`, conflict/cancel/reload and all 52 authored bindings | `V-UI-01`; P4 |
| `V-UI-08` change game/graphics settings | `UISettingsModal.Select*` `0x06002516-20` -> settings variables plus `Screen`/`QualitySettings`/`Application`; close saves settings | authored option; resolution/display option supported | setting and possibly display state change, then settings persist; receipt: key, before/after authored value, native application verdict, persistence verdict | **none** | `R1`; `C0+C-UI`, unsupported resolution, cancel, reload; platform application is `EG-04` | `V-UI-01`; P4 |
| `V-UI-09` select/pin/reorder a resource row | `UIResourceDisplayList.ClickItem/OnDrop` `0x06002639/36`; list variable selection/pin/order | resource visible; valid index/slot | selected resource and/or display order/pin state changes; receipt: resource UUID and before/after ordered UUIDs/selection; no quantity delta | **none** | `R1`; `C0+C-UI+C-LIST`; persistence is `EG-03` | `V-UI-01`; P3 |
| `V-UI-10` toggle music-track shuffle | `UIMusicTrack.ToggleShuffleItem` `0x0600255A` -> music shuffle collection | music UI/control available | membership/order for shuffled track changes; receipt: track identity and before/after membership; no gameplay delta | **none** | `R1`; `C0+C-UI`, reload persistence (`EG-03`) | `V-UI-01`; P4 |
| `V-UI-11` edit loadout label/icon/color | loadout editor controls -> `PlayerLoadout` name/icon/color fields; exact prefab listener is `EG-01` | loadout selected; value/icon/color valid | metadata fields and loadout presentation change; receipt: loadout stable identity and before/after metadata, persistence verdict | **none** | `R1`; `C0+C-UI`, invalid/duplicate label and reload | `V-LOAD-01`; P3 |
| `V-PASS-01` mute/unmute a passive ability | `UIPassiveAbilityList.OnPassiveClick` `0x06002575` -> `PassiveAbility.ToggleMuted` `0x06000E9A` writes `PassiveAbilitySO.muted` | passive present in rendered list | muted flag toggles; receipt: passive UUID/type and before/after muted; IL shows no effect removal/resource mutation | **none** | `R1`; `C0+C-UI`, active/inactive passive and reload persistence (`EG-03`) | `V-UI-01`; P4 |
| `V-PROC-01` continue/load selected save | main-menu Continue binding -> `SaveStateManager.LoadSaveState`; suite path `game_continue` invokes audited selected-save continuation | main menu, selected existing compatible save, no load in progress (`EG-04` for scene wiring) | scene/lifecycle and loaded save state change; receipt: selected save identity, accepted/rejected, lifecycle before/after; never expose save contents | **full** for selected-save `game_continue`; selecting a different slot is `V-PROC-03` | `R4`; `C0+C-SAVE`, corrupted/missing/current-version cases; game launch prohibited in this mission | none; P0 playthrough entry |
| `V-PROC-02` start a new game | new-game UI event -> `SaveStateManager.StartGame`; serialized listener `EG-01/04` | main menu; target slot/new-game options valid | allocates/initializes save and changes scene/lifecycle; receipt must name slot identity and initialization/load result | **none** | `R5`; `C0+C-SAVE`; never target an occupied or real slot | none; P2; initial playthrough setup remains manual until promoted |
| `V-PROC-03` select a save slot | save-slot UI -> selected-slot variable; serialized listener `EG-01/04` | slot widget rendered; slot identity valid | selected slot changes without loading/writing it; receipt: before/after selected slot identity and byte-identical slot files | **none** | `R1`; `C0+C-SAVE` | none; P3 |
| `V-PROC-04` manually save | manual-save UI event -> `SaveStateManager.SaveGameState`; serialized listener `EG-01` | loaded game; save manager not busy; native save allowed | serializes current game state to active slot; receipt: slot identity, start/completion/failure, durable revision/checksum if native exposes it | **none** | `R4`; `C0+C-SAVE` | `V-PROC-01`; P3 |
| `V-PROC-05` export a save | `UIExportButton.ExportSave` `0x06002209` -> `SaveStateManager.CopySaveToClipboard` | exportable selected/active save; clipboard available | clipboard changes only; receipt: slot identity, payload length/hash, clipboard success; do not return payload in audit | **none** | `R2` privacy/clipboard; `C0+C-SAVE`, clipboard failure and prove save unchanged | `V-PROC-01`; P4 |
| `V-PROC-06` import a save | `UIImportButton.ImportSave` `0x0600220F` / `UIImportSaveModal.ImportSave` `0x060024D4` -> `SaveStateManager.ImportSaveFromClipboard` -> `ImportSave` -> `SaveGameStateAs` | explicit confirmation; parse/version/slot validation; target disposable slot (`EG-04`) | parses payload then writes target slot; receipt: target slot, validation result, before/after hash/existence, and partial write evidence on failure | **none** | `R5`; `C0+C-SAVE`; malformed and occupied-slot tests mandatory | none; P5; destructive boundary below automation stop line |
| `V-PROC-07` back to main menu | `UIBackToMenuButton.BackToMenu` `0x0600280D` -> `SaveStateManager.BackToMainMenu`; save-before-transition listener detail `EG-01` | loaded game; transition not active | may save, invalidates lifecycle, changes scene; receipt: save attempt/result, lifecycle generation, target scene, terminal state | **none** | `R4`; `C0+C-SAVE` | `V-PROC-01`; P4 |
| `V-PROC-08` quit application | `UIQuitButton.QuitApplication` `0x06002507` -> `Application.Quit`; save-before-quit wiring `EG-01` | quit control available and confirmed where applicable | may save then terminates process; receipt cannot be delivered after termination, so pre-quit audit plus next-launch persistence is required | **none** | `R5`; `C0+C-SAVE`; inherently unsuitable for inline MCP completion | none; P5; permanent non-action candidate |
| `V-PROC-09` delete a save slot | save-slot delete/confirmation -> `SaveStateManager.DeleteGameSave`; serialized listener/confirmation `EG-01/04` | target slot exists, is not active, and explicit confirmation names it | target slot data is removed; receipt: target identity, before/after existence/hash, confirmation, and byte-identical proof for every non-target slot | **none** | `R5`; `C0+C-SAVE` | `V-PROC-03`; P5; destructive delete is below automation stop line |

### Economy, research, leveling, and discovery

| Verb | UI route and native pipeline | Preconditions/gating | Ordered effects and exact receipts | Existing coverage and named gap | Risk and disposable-save check | Dependencies; playthrough priority |
|---|---|---|---|---|---|---|
| `V-ECO-01` buy attributes/structures | `UIStructureList.PurchaseStructure` `0x06002763` -> `StructureSO.Purchase` `0x06001784`: build-time/quantity/queue-room clamp, next exact cost, `HasEnough`, `PerformCost`, `QueueBuild` | structure visible/available; valid UUID+`StructureSO`; positive quantity; native max/multi-buy/queue room; exact funds | cost is committed, build quantity queued, reactions/passives/audio may fire; success receipt: target, requested/accepted quantity, exact resource deltas, before/after owned+queued, queue IDs/room; partial receipt names payment without expected queue; refusal has zero deltas | **full**: `game_purchase`/`AutoBuyPurchaseGameAction` for `AttributeSO`/`StructureSO`; MCP shares the action | `R3`; `C0+C-PAY+C-QUEUE`, including instant/non-instant builds | `V-UI-06`; P0 core economy |
| `V-ECO-02` buy an upgrade | `UIUpgradeButton.ClickUpgradeButton` `0x06002944` -> `UpgradeSO.CanPurchase` -> `UpgradeSO.Purchase` `0x060018A6`: quantity/cost/requirements, `PerformCost`, `QueueAction` | visible/available `UpgradeSO`; requirements and max; queue room; exact funds | exact cost then upgrade action queued/completed; receipt: UUID/type, quantity, exact resource delta, before/after level+queued, action IDs; partial identifies payment/queue divergence | **full**: `game_purchase`/`AutoBuyPurchaseGameAction` for `UpgradeSO` | `R3`; `C0+C-PAY+C-QUEUE` | `V-ECO-01`,`V-UI-06`; P0 |
| `V-ECO-03` disable or enable a structure | `UIStructureList.ToggleDisableStructure` `0x06002765` -> `StructureSO.ToggleDisabled`; `DisableStructure` sets disabled then `RemoveEffects`; `EnableStructure` clears it then `ApplyEffects` | owned/active structure; toggle control rendered; identity/type current | disabled flag and native effects change; receipt: target, requested state, before/after disabled, affected production/effect snapshots; partial identifies flag/effect disagreement | **none** | `R2`; `C0+C-LIST`, reload plus production/effect proof | `V-ECO-01`; P2 resource-control blocker |
| `V-RES-01` start/queue research development | `UIResearchItem.DevelopResearch` `0x060025F0` -> `ResearchSO.PurchaseLevel` `0x060011B4` -> setting-dependent `Develop` `0x060011B5` or `QueueDevelopment` | research visible, `CanDevelop`, below max; usage/leeway/queue capacity and required resources; queue-mode setting | active/developing/queued level and usage cost/investment change; type/drain recalculation and audio may fire; receipt: research UUID, route mode, before/after active+queued+progress+investment+usage/drain and queue identity | **none** | `R3`; `C0+C-PAY+C-QUEUE`, both immediate/queued modes | `V-UI-08`; P1 progression |
| `V-RES-02` pause or resume research | `UIResearchItem.PauseResearch/ResumeResearch` `0x060025F1/F2` -> `ResearchSO.PauseResearch/ResumeResearch` | target actively developing/paused; native queue identity current; resume has usage/queue room | pause state and progress advancement behavior change; receipt: target, before/after state/progress/investment/queue position, zero resource delta | **none** | `R2`; `C0+C-QUEUE`, lifecycle invalidation | `V-RES-01`; P2 |
| `V-RES-03` cancel research development | `UIResearchItem.CancelDevelopment` `0x060025F7` -> `ResearchSO.CancelDevelopment`; clears development investment; refund details are `EG-02` | target queued/developing; cancellation allowed | queue/active development removed and investment cleared; receipt: before/after state/progress/investment, exact resource/refund delta and queue removal; partial names removal/refund mismatch | **none** | `R3`; `C0+C-QUEUE+C-PAY` at zero/partial/near-complete progress | `V-RES-01`; P2 |
| `V-RES-04` apply a research bonus level | `UIResearchItem.AddBonusLevel` `0x060025F8` -> `ResearchSO.CanApplyBonusLevels` -> `SubmitBonusLevel` | eligible research; bonus level currency/count available; cap/requirements met | bonus resource/count consumed and research bonus/effective level/effects change; receipt: before/after purchased, bonus, total/effective levels and exact bonus-source delta | **none** | `R4`; `C0+C-PAY`, explicitly distinguish purchased from bonus | `V-RES-01`; P2 |
| `V-LVL-01` buy a generic level | `UILevelableItem.PurchaseLevel` `0x06002467` -> `ILevelable.PurchaseLevel`; concrete audited implementers include `GlyphSO`, `EquipmentTypeSO` `0x06000B66`, `ResourceTypeSO`, `TimeRuneSO` `0x06001847`; `UIAlchemyRecipe.ForceRenderActionButton` `0x06002171` separately calls `AlchemyRecipeSO.IncreaseMaxLevel` | concrete UUID+expected type; `CanLevel`; below max; requirements and exact `GetLevelCost().HasEnough` | `UICostButton.OnClick` `0x06002204` pays then concrete level/effects apply; receipt: concrete type, before/after purchased/base/bonus/total level, exact cost and effect deltas; `EG-05` if callback diverges after UI payment | **partial**: `game_spell_level` covers spell mastery only; no shared action for these implementers | `R3/R4`; `C0+C-PAY`, each concrete native type and free-vs-paid distinction | `V-UI-06`; P1 for glyph/time-rune/equipment progression |
| `V-LVL-02` consume a free generic level | `UILevelableItem.PurchaseFreeLevel` `0x06002468` -> `ILevelableHasFree.PurchaseFreeLevel` | concrete type supports free levels; free count positive; can level/below cap | free count decreases and purchased/base/effective level/effects change without normal cost; receipt: type, before/after free count and all level kinds, zero normal-cost delta | **none** | `R4`; `C0+C-PAY`, zero-free and cap refusal | `V-LVL-01`; P2 |
| `V-DISC-01` discover a generic discoverable | `UIDiscoverablePage.HandleClick` `0x0600231C` -> selected `IDiscoverable.Discover`; concrete members include `TimeRuneSO.Discover` `0x06001858` and `EquipmentSO.Discover` `0x06000B10`; `UICostButton.OnClick` pays before callback | selected concrete UUID/type; visible, not discovered, `CanDiscover`; exact discovery cost | native payment then exact concrete discovery; success returns the complete newer named category row and next discovery decision; faults retain payment-before-callback evidence without making accounting a success gate | **full**: `game_discover` / `GenericDiscoveryGameAction` owns alchemy recipe, equipment, glyph, ritual, and time-rune discovery; spell recipes stay in `game_spell_workbench`, with the exact family split documented in the [pipeline dossier](generic-discoverable-native-pipeline.md) | `R4`; `C0+C-DISC+C-PAY`, one test per concrete type | `V-UI-01`; P1 unlock path |
| `V-DISC-02` **buy/initiate a discovery offer set** | `UIDiscoveryTreePage.OnDiscoveryClick` `0x0600232B` -> `DiscoveryTreeSO.InitiateCraftingMode`; `UICostButton` pays the tree's exact cost | tree visible, idle, can initiate; exact funds; offer pool/nonempty constraints | cost paid, idle becomes choice/crafting mode, offered stable UUIDs materialize; receipt: tree UUID/type, exact cost/deltas, mode before/after, offer UUIDs+types and rerolls; partial identifies payment without choice mode | **none**; this is the highest-priority missing spell-shop purchase capability | `R4`; `C0+C-DISC+C-PAY`, empty/exhausted pool and reload | `V-ECO-01`,`V-UI-01`; **P0 HIGH: buy new spell/offer transaction** |
| `V-DISC-03` **select/pick an offered option** | `UIDiscoveryTreePage.OnDiscoveryItemClick` `0x0600232C` -> `SelectItemGuid` `0x0600232F` | tree in choice mode; candidate UUID is exactly one current offered item and remains eligible | selected offer UUID changes, with no discovery/cost yet; receipt: tree, offered set, before/after selected UUID, zero progression/resource delta | **none**; do not substitute UI clicking or raw selection-field writes | `R2`; `C0+C-DISC`, non-offer/stale/hidden UUID refusal | `V-DISC-02`; **P0 HIGH: choose offered option** |
| `V-DISC-04` **confirm/discover the selected offer** | `UIDiscoveryTreePage.OnConfirmClick` `0x0600232D` -> `DiscoveryTreeSO.DiscoverSelectedItem`, then UI clears selection | tree in choice mode; current selected UUID belongs to current offers; selected discoverable still eligible | selected item becomes discovered/created, tree exits/advances mode, selection clears, effects/unlocks apply; receipt: offer/tree identity, before/after discovery/mode/selection, unlocked UUIDs/effects and any cost delta; partial identifies discovered-but-mode/selection divergence | **none** | `R4`; `C0+C-DISC`, stale selection and already-discovered race | `V-DISC-02`,`V-DISC-03`; **P0 HIGH: complete offer purchase** |
| `V-DISC-05` **reroll current offers** | `UIDiscoveryTreePage.OnRerollClick` `0x0600232E` -> `DiscoveryTreeSO.HasRerolls` -> `RerollChoices` | tree in choice mode; reroll count positive; offer pool can produce choices | reroll count decreases, offer UUID set and selection change; receipt: before/after rerolls, ordered old/new offers, selection before/after, no unrelated discovery/cost | **none**; highest-priority missing spell-shop verb | `R4`; `C0+C-DISC`, zero rerolls, exhausted pool, selected offer, reload | `V-DISC-02`; **P0 HIGH: reroll offers** |
| `V-ART-01` create/discover an artifact | artifact-create view uses generic discoverable selection; terminal `EquipmentSO.Discover` `0x06000B10` -> `Create` `0x06000B11`; serialized route is `EG-01` | artifact visible, not created, `CanDiscover`/`CanCreate`, exact discovery/create cost and requirements | exact equipment identity becomes created; success returns the complete newer named equipment row, including its now-available loadout decisions and no automatic equip claim | **full**: `game_discover` / `GenericDiscoveryGameAction`; equipment post-state is enriched by the B-009 loadout reader, with the family split documented in the [equipment dossier](equipment-loadout-native-pipeline.md) | `R4`; `C0+C-DISC+C-LIST`, no auto-equip unless observed | `V-DISC-01`; P1 artifact progression |

### Spells and targeting

| Verb | UI route and native pipeline | Preconditions/gating | Ordered effects and exact receipts | Existing coverage and named gap | Risk and disposable-save check | Dependencies; playthrough priority |
|---|---|---|---|---|---|---|
| `V-SPELL-01` select glyphs for spell discovery | `UIGlyphList.SelectGlyph` `0x060023F5` -> discovery selection list/event; `SpellManager` holds selected glyph recipe | glyph available; stack/max-usage rules; selection count forms a valid/unknown recipe | ordered/stacked glyph selection changes; receipt: before/after glyph UUID+count list and resolved candidate recipe UUID if any; zero resource/discovery delta | **full for authored base recipes**: `game_spell_workbench(mode=select)` / `SpellWorkbenchGameAction` applies the exact ordered `GetGlyphRecipe()` cores and clears augments; B-003 owns augment composition; see [pipeline dossier](spell-manager-discovery-create-native-pipeline.md) | `R1`; `C0+C-UI+C-LIST`, duplicate/max-use/unknown combo | `V-LVL-01`; P0 prerequisite for buying spells |
| `V-SPELL-02` **buy/discover a selected spell recipe** | `UIDiscoverSpellButton.HandleClick` `0x06002300` event -> `SpellManager.DiscoverSpell` `0x06000741`: find/create empty recipe, `SpellRecipeSO.Discover` `0x06001432`, then `ResourceCostList.PerformCost`; direct recipe button callback -> `SpellManager.DiscoverRecipe` | selected glyph combination resolves; recipe not discovered; `CanDiscover`; exact discovery cost; event wiring partly `EG-01` | native combo path may set discovered **before** payment (`EG-05`), then cost/unlocks; receipt must include selected glyphs, recipe UUID, before/after discovered, exact cost, empty-spell/list effect and discovered-without-payment partial evidence | **full**: `game_spell_workbench(mode=discover)` / `SpellWorkbenchGameAction`; costs and affordability are pre-published, native discovery is revalidated, and target discovery is the outcome gate | `R4`; `C0+C-DISC+C-PAY`, deliberately force post-discover cost divergence in stubs only | `V-SPELL-01`; **P0 HIGH: buy new spells** |
| `V-SPELL-03` create/equip a spell instance | `UICreateSpellButton.HandleClick` `0x060022F9` event -> `SpellManager.CreateSpell` `0x0600073F` -> `CreateRecipe` `0x06000740`; undiscovered path pays/discovers, then creates/adds if slot/usage room via `AddSpell` `0x0600074B` | glyph recipe valid; discovery/payment if needed; spell slot, glyph usage, bandwidth/drain and other requirements | optional discovery cost, new stable spell identity with recipe/glyphs/level, list add, weight/usage/drain and attunement change; receipt: all identities, exact cost, slot/list before/after, usage/drain/attunement and partial step | **full after explicit discovery**: `game_spell_workbench(mode=create)` / `SpellWorkbenchGameAction`; exact recipe selection, native cost/room, and a new non-empty runtime instance of the requested recipe are revalidated | `R4`; `C0+C-DISC+C-LIST+C-PAY`, full slots and undiscovered recipe | `V-SPELL-01`,`V-SPELL-02`; P0 equip for casting |
| `V-SPELL-04` remove an equipped spell | spellbook/loadout context -> `Spell.CanRemove` `0x06001038`, then `SpellManager.RemoveSpell` `0x0600074C` | exact runtime spell exists in the current list; native `CanRemove`; identity and lifecycle current | exact runtime identity disappears; survivor order is preserved while hole-vs-compaction is observed; newer loadout reports named slots, capacity, and next decisions; weight/glyph/drain/resource changes are evidence only | **full**: `game_spell_loadout(mode=remove)` / `SpellLoadoutGameAction`; live native gate, exact target-absence and survivor-order outcome, B-001-shaped complete newer post-state; see [pipeline dossier](spell-loadout-native-pipeline.md) | `R4`; `C0+C-LIST+C-COMBAT`, casting/queued and hole-vs-compaction cases | `V-SPELL-03`; P1 loadout repair |
| `V-SPELL-05` reorder equipped spell slots | `UISpellList.OnDrop` `0x06002701` -> `AbstractListVariable<Spell>.SwapPositions` + `UpdateObservable` | exact runtime spell resolves to a current source; destination is in range and distinct | complete raw slot-identity sequence has exactly source/destination exchanged; committed MCP result returns all named occupied/empty slots and next decisions | **full**: `game_spell_loadout(mode=move)` / `SpellLoadoutGameAction`; exact sequence-swap outcome with B-001 quarantine posture and newer post-state | `R1`; `C0+C-LIST`, occupied/empty, invalid/same-slot, duplicate-recipe instance and hotkey mapping | `V-SPELL-03`; P1 hotbar control |
| `V-SPELL-06` edit spell output level/augment glyph composition | `UISpellRecipeButton.AttachSpell` `0x0600270D` -> `Spell.SetAugmentGlyphs` `0x06000FAC`; `UISpellInformation.SetSpellLevel` `0x06002535` -> `Spell.SetLevel`, whose level comes from global `Player.GetSpellOutputLevel` `0x06000690`; `UIGlyphList.SelectGlyph` clamps through `GlyphSO.GetMaxUsages` `0x06000BCE` | output 1..live max; exact equipped runtime spell identity; each glyph exact/available/augment; combined max-use, compatibility, and mastery predicates pass | global output changes or the exact requested spell receives the exact UUID/count augment stack; native setter reloads derived recipe state and recomputes costs; committed MCP result returns newer output/equipped composition, choices, holdings, cast/drain costs, and affordability with no receipt/payment stanza | **full**: `game_spell_composition(mode=set_output_level|set_augments)` / `SpellCompositionGameAction`; one complete lifecycle binding set, live main-thread revalidation, identity/outcome verification, and B-001-shaped newer post-state; see [pipeline dossier](spell-composition-native-pipeline.md) | `R4`; `C0+C-LIST`, cap/duplicate/unavailable/incompatible/mastery refusal plus exact-target divergence quarantine | `V-SPELL-03`,`V-LVL-01`; P1 spell customization |
| `V-SPELL-07` cast or release a spell | hotbar/click `UISpellList.OnSpellFire` `0x06002707` -> `Spell.CanFire/Fire`; suite `GameCastGameAction` also supports release | spell identity current, equipped, available, cooldown/cost/queue/target gates; native revalidation at commit | exact resource cost, cast/release queue and cooldown/effects/target change; receipt includes stable spell+target identity, mode, exact resource delta, queue/cooldown before/after, execution/refusal | **full**: `game_cast`/`GameCastGameAction`; Auto Cast shares audited family semantics | `R3`; `C0+C-COMBAT+C-PAY` | `V-SPELL-03`,`V-TGT-01`; P0 |
| `V-SPELL-08` purchase spell mastery level | `UISpellRecipeItem.ForceRenderActionButton` `0x06002728` -> `SpellRecipeSO.PurchaseLevel`; suite `SpellLevelGameAction` | discovered recipe, mastery ready, usage requirements, below cap, exact cost | exact cost and mastery/upgrade level/effects change; receipt: recipe UUID, before/after mastery XP/readiness/level, exact resource and type-XP/effect deltas | **full**: `game_spell_level`/`SpellLevelGameAction` | `R3`; `C0+C-PAY` | `V-SPELL-02`; P1 |
| `V-SPELL-09` level all eligible spells | authored `LevelAllSpellsButton` event (`EG-01`) -> `SpellManager.TryLevelAllSpells` `0x06000745`, iterating available recipes and repeatedly paying then `PurchaseLevel` while discovered/can-level/affordable | at least one available recipe is discovered, `CanLevel`, and affordable; iteration order is the native list order | for each recipe, repeated exact costs and levels commit before advancing; receipt: ordered per-recipe attempts with every before/after level and exact cost, terminal skipped reasons and aggregate deltas; any later failure is honest multi-commit partial evidence | **partial**: repeated `game_spell_level` can reproduce individual decisions, but no one-call native batch capability/aggregate receipt exists | `R4`; `C0+C-PAY`, mixed eligible/ineligible/affordable recipes and mid-list exhaustion | `V-SPELL-08`; P2 convenience after single-level action |
| `V-TGT-01` select and submit a specific target | `TargetingManager.RequestTarget` `0x0600076E`; UI click -> `TargetingManager.SubmitTarget` `0x06000775`; submit assigns the exact target before removing the queue head | one active exact `TargetLink`; UUID resolves exactly once within `GetAllTargets`; `CheckTarget` remains true at commit | exact target assigned; original request retires and may release its requester; success returns named submitted structure plus complete next request/candidates | **full**: `game_targeting(mode=submit)` and `TargetingGameAction`; `game_cast` retains the same primitives for its atomic cast transaction | `R3`; `C0+C-COMBAT`, invalid/dead/stale candidate | requesting verb, chiefly `V-SPELL-07`; P1 |
| `V-TGT-02` randomize and submit a pending target | `UITargetingInterface.Randomize` `0x0600276E` -> `TargetLink.GetRandom` -> `TargetingManager.SubmitTarget`; it is not a candidate-only shuffle | one active exact link; pool nonempty; random result is exact `StructureSO` and passes `CheckTarget` | RNG chooses and immediately submits; exact target assigned and original request retires; success returns named target plus complete next request/candidates | **full**: `game_targeting(mode=randomize)` and the same `TargetingGameAction` | `R3`; `C0+C-COMBAT`, empty/single/multiple pools and RNG divergence | `V-TGT-01`; P2 |
| `V-TGT-03` cancel a pending target request | `UITargetingInterface.Close` `0x0600276C` only closes presentation; gameplay cancel is link `resultInfo` `0x04001A96` -> `EffectResultInfo.Cancel` `0x06001BFC` -> `TargetingManager.RemoveRequest` | one active exact link with non-null owning `EffectResultInfo`; not already cancelled | owning result is cancelled and its target links retire; success returns complete next request or `pending:false` | **full**: `game_targeting(mode=cancel)` and the same `TargetingGameAction` | `R3`; `C0+C-COMBAT`, no-request, ownerless-link, multi-link requester | `V-TGT-01`; P1 |

### Alchemy and rituals

| Verb | UI route and native pipeline | Preconditions/gating | Ordered effects and exact receipts | Existing coverage and named gap | Risk and disposable-save check | Dependencies; playthrough priority |
|---|---|---|---|---|---|---|
| `V-ALCH-01` select alchemy discovery ingredients | glyph/resource discovery lists -> `AlchemyManager.SelectGlyph1/SelectGlyph2/SelectResource1/SelectResource2` `0x06000466-6B` | candidate visible/available; selection slot compatible; combination permitted | four selection slots and resolved recipe candidate change; receipt: before/after typed ingredient UUIDs and candidate recipe UUID, zero discovery/cost delta | **none** | `R1`; `C0+C-UI`, duplicates, clear/toggle, hidden candidate | `V-DISC-01`; P1 |
| `V-ALCH-02` discover an alchemy recipe | `UIAlchemyDiscoverButton.HandleClick` `0x06002153` event -> `AlchemyManager.DiscoverAlchemy` `0x0600046C`: capture combo, clear selections, `PerformCost`, `AlchemyRecipeSO.Discover` | valid selected combo; recipe undiscovered and discoverable; exact funds; listener `EG-01` | selection clears, exact cost commits, recipe discovered/effects/unlocks apply; receipt: ingredient+recipe UUIDs, selection before/after, exact delta, discovered state and partial payment-without-discovery evidence | **none** | `R4`; `C0+C-DISC+C-PAY` | `V-ALCH-01`; P1 |
| `V-ALCH-03` engage/increase an alchemy recipe | `UIAlchemyRecipeList.ClickItem` `0x0600217B` -> `AlchemyInstanceListVariable.EngageAlchemy` `0x06001604` -> `AddAlchemyInstances` `0x06001605` | recipe discovered/available; slot/stack/max/multi-buy room; usage/bandwidth/drain capacity | create/init instance if absent, add quantity, consume usage/bandwidth/drain and apply effects; receipt: recipe/instance identities, requested/accepted quantity, before/after ordered stacks, capacity/usage/drain/effects | **partial**: `game_concept`/`AutoConcept` fully handles concept-classified recipes only; ordinary alchemy has no action | `R3`; `C0+C-LIST`, ordinary and concept types, duplicate/max/room | `V-ALCH-02`,`V-UI-06`; P1 |
| `V-ALCH-04` disengage/decrease an alchemy recipe | `UIAlchemyInstanceList.ClickItem` `0x06002167` -> `AlchemyInstanceListVariable.DisengageAlchemy` `0x06001606` -> `RemoveAlchemyInstances` | active instance and positive quantity; removal amount clamped by multi-buy | quantity/list decrease, usage/bandwidth/drain released, effects removed/recomputed; receipt: identities, requested/removed, before/after stacks/order/capacity/effects | **partial**: `game_concept` covers concept-classified removal only | `R3`; `C0+C-LIST`, remove some/all and last instance | `V-ALCH-03`; P1 |
| `V-ALCH-05` reorder active alchemy | `UIAlchemyInstanceList.OnDrop` `0x06002168` -> active instance list swap/reorder | valid same-list distinct indices | ordered instance identities change; receipt: before/after order and indices; no quantity/resource/effect delta | **none** | `R1`; `C0+C-LIST` | `V-ALCH-03`; P2 |
| `V-ALCH-06` purchase alchemy recipe max level | `UIAlchemyRecipe.ForceRenderActionButton` `0x06002171` configures `UICostButton` -> `AlchemyRecipeSO.IncreaseMaxLevel` | discovered recipe; `CanLevel`; below maximum; exact leveling cost/requirements | UI cost pays then max/selected level and effects/capacity change; receipt: recipe UUID, before/after max/selected/mastery level, exact delta/effects; callback divergence is `EG-05` | **none**; concept automation changes active quantity, not recipe max level | `R3`; `C0+C-PAY` | `V-ALCH-02`; P1 |
| `V-RIT-01` select runestones for ritual discovery | `UIRuneStoneList.ClickRuneStone` `0x06002670` -> `RitualManager.SelectRuneStone` `0x060006BC` | runestone visible/available; bounded selection; combination valid/unknown | selected ordered runestones and candidate ritual change; receipt: before/after UUIDs and resolved candidate UUID, zero cost/discovery delta | **none** | `R1`; `C0+C-UI+C-LIST` | `V-DISC-01`; P2 |
| `V-RIT-02` discover a ritual | `UIDiscoverRitualButton.Discover` `0x06002646` event -> `RitualManager.DiscoverRitual` `0x060006BE` -> `RitualSO.Discover`; `UICostButton` pays; listener/order detail `EG-01/05` | selected runestones resolve; ritual can discover; exact funds | exact cost and discovered ritual/effects/unlocks change; receipt: runestones+ritual UUIDs, exact delta, before/after discovered and selection; partial step named | **none** | `R4`; `C0+C-DISC+C-PAY` | `V-RIT-01`; P2 |
| `V-RIT-03` select a ritual and starting level | `UIRitualList.ClickRitual` `0x06002658`; level control -> `RitualSO.ChangeStartingLevel` `0x06001367` | ritual discovered/available; starting level within native clamp; no activation commit yet | selected ritual/level and projected completion-cost usage change; receipt: before/after selected UUID+level and projected exact cost/drain, zero active battle/queue delta | **none** | `R2`; `C0+C-UI+C-PAY`, min/max/out-of-range | `V-RIT-02`; P2 |
| `V-RIT-04` start the selected ritual | activation -> `RitualManager.ActivateSelectedRitual` `0x060006BF` -> `StartRitual` -> `BattleManager.StartRitual`; `RitualSO.Initiate` applies completion-cost drain | discovered ritual and level selected; usage/completion cost/drain and battle/queue room; target if required | active ritual/battle identity, cost/drain/queue/effects change; receipt: ritual+target identities, level, before/after active/battle/queue/drain and exact resource delta | **none** | `R4`; `C0+C-COMBAT+C-PAY+C-QUEUE` | `V-RIT-03`,`V-TGT-01`; P2 |
| `V-RIT-05` cancel an active ritual | `UIRitual.CancelRitual` `0x06002653` -> `RitualSO.Cancel` | ritual active and cancellable | battle/ritual activity ends; completion cost/drain/effects and queue are released/removed; refund detail `EG-02`; receipt: before/after active/battle/drain/effects, exact resource/refund delta | **none** | `R4`; `C0+C-COMBAT+C-QUEUE+C-PAY` | `V-RIT-04`; P2 |

### Challenges and prestige

| Verb | UI route and native pipeline | Preconditions/gating | Ordered effects and exact receipts | Existing coverage and named gap | Risk and disposable-save check | Dependencies; playthrough priority |
|---|---|---|---|---|---|---|
| `V-CHAL-01` select/unselect an offered challenge | `UIChallengeItem.ToggleSelection` `0x0600224E` -> selected challenge list `Toggle` | challenge is in current offered/active selection surface; selection cap/compatibility | ordered selected challenge UUIDs change; receipt: before/after selected and offered UUIDs, zero challenge activation/reward/reset delta | **none** | `R2`; `C0+C-RESET`, incompatible/cap/stale offer | `V-CHAL-04`; P1 before prestige |
| `V-CHAL-02` queue/unqueue challenge activation | `UIChallengeItem.FireActionButton` `0x0600224B` inactive branch -> `ToggleActivate` `0x0600224C` -> `ChallengeSO.ToggleQueueActivation` | visible challenge; activation requirements; queue state/cap; not already active | queued-activation membership and projected modifiers change; receipt: UUID, before/after queued/active, exact projected challenge adjustments and blockers; no reward until native activation | **none** | `R3`; `C0+C-QUEUE+C-RESET`, queue/unqueue and reload | `V-CHAL-01`; P1 |
| `V-CHAL-03` abandon an active challenge | `UIChallengeItem.FireActionButton` active branch -> `AbandonActivation` `0x0600224D` -> `ChallengeSO.AbandonChallenge`/fail/remove effects | challenge active; abandon allowed/confirmed | active/queued state and challenge effects change; failure/reward/progress disposition must be observed (`EG-02`); receipt: before/after active/progress/reward/effects plus exact lost/retained evidence | **none** | `R4`; `C0+C-RESET`, no-progress/partial/complete cases | `V-CHAL-02`; P2 |
| `V-CHAL-04` reroll/fetch challenge offers | `UITimeScreenManager.FetchNewChallenges` `0x06002444` or `UIPersistentResetModal.FetchNewChallenges` `0x060024EE` decrements rerolls then calls `ChallengeManager.LoadNewActiveChallenges` or `PersistentResetManager.FetchNewChallenges` | rerolls positive; relevant time/reset view; offer pool available | reroll count decreases, old challenges cycle out, new ordered offer UUIDs instantiate, selection may clear; receipt: before/after rerolls/offers/selection and exact cycle-out evidence | **none** | `R4`; `C0+C-RESET`, zero/exhausted/selected offer | `V-UI-01`; P1 |
| `V-PREST-01` perform a persistent reset/prestige | reset-confirm UI -> `PersistentResetManager.PersistentReset` `0x06000651` -> `PersistentResetLogic` `0x06000652`: setup, `GameManager.PersistentResetGameState`, challenge activation/rewards, persistent-resource set, cleanup | native reset available; explicit confirmation; chosen challenges valid; lifecycle/save current | broad game-state reset, lifecycle/cache invalidation, challenge reward/activation, persistent resource and unlock/save changes; receipt: reset generation, selected challenges, complete before/after progression/resource summaries and terminal save result; any divergence is quarantine-worthy partial commit | **none** | `R5`; `C0+C-RESET+C-SAVE`; never live-tested by this mission | `V-CHAL-01`,`V-CHAL-02`; P1 required for full loop, but promotion is high ceremony |

### Crafting, consumables, equipment, and loadouts

| Verb | UI route and native pipeline | Preconditions/gating | Ordered effects and exact receipts | Existing coverage and named gap | Risk and disposable-save check | Dependencies; playthrough priority |
|---|---|---|---|---|---|---|
| `V-CRAFT-01` execute a one-shot manual recipe | `UICraftingRecipeList.ClickCraft` `0x060022E1` -> optional page callback -> `CraftingRecipeSO.Execute`; manual `UICraftingPage.QueueCraft` `0x060022F0` may create/init/queue an instance | exact concrete recipe identity/type; visible; native direct/page affordability; exact authored page/main-type/mode; quantity/level; queue/instant rules | exact direct/stack/new/instant route and requested recipe/queue outcome; committed result is the complete newer named recipe decision with exact next costs, holdings, affordability, queue state, and blockers; faults retain partial stage/state, while payment is never a success gate | **full**: shared `AutoScribeOneShotCraftGameAction` / `game_craft`; player overload widens the existing automation boundary to every concrete recipe, with exact native `Execute` or `QueueCraft` re-drive and [pipeline dossier](one-shot-crafting-native-pipeline.md) | `R3/R4`; `C0+C-PAY+C-QUEUE`, direct/instant/stack/new, full queue, missing/duplicate page, quantity-as-level, Craft Sigils and each Scribe recipe | `V-LVL-01`,`V-UI-06`; P1, especially Craft Sigils/Scribe |
| `V-CRAFT-02` add/increase automated crafting | `UICraftingPage.ContextRecipeClick` automate branch -> `CraftingInstanceListVariable.AutomateCraft`; `QueueCraft` adds quantity or new `CraftingInstance.Initiate` | recipe permits automation; visible/available; capacity/inputs/level/quantity valid | automated instance created or quantity grows, inputs/usage/drain/progress change; receipt: recipe+instance identities, requested/accepted quantity, before/after stacks/queue/cost/drain/progress | **none** | `R3`; `C0+C-LIST+C-QUEUE+C-PAY` | `V-CRAFT-01`; P2 |
| `V-CRAFT-03` cancel/decrease crafting | `UICraftingInstanceList.OnClickInstance` `0x060022CC`: automated -> `RemoveAutomation`; manual -> `CraftingInstance.CancelCraft` then list remove | current instance; positive removable quantity; cancellation allowed | quantity/instance/queue decreases, effects/usage/drain removed; refund/partial-output detail `EG-02`; receipt: exact before/after stack/queue/progress/input/output/refund/effects | **none** | `R3`; `C0+C-LIST+C-QUEUE+C-PAY`, zero/partial/near-complete | `V-CRAFT-02` or `V-CRAFT-01`; P2 |
| `V-CRAFT-04` configure/toggle a crafting station | `UIBrewingStation.SetIngredient/SetOutput` `0x0600267B/7C`, level selector, `ToggleBrewing` `0x06002682` -> `CraftingStructureSO.SetActive` | station owned/visible; ingredient/output compatible; level/capacity/input requirements; activation allowed | station recipe/level/active state, resource consumption/output/drain/effects change; receipt: station+typed ingredient/output identities, before/after config+active+progress, exact deltas | **none** | `R3`; `C0+C-LIST+C-QUEUE+C-PAY` | `V-ECO-01`,`V-CRAFT-01`; P2 |
| `V-CONS-01` use a consumable | `UIConsumableRefList.ClickConsumable` `0x0600229B` event -> `ConsumableSO.SelectAndFire` -> quantity/multi-buy queue; `Fire` starts usage and cost | inventory quantity; visible/available; queue/capacity/target/cooldown gates | exact consumable queue transition; newer holding, costs, usages, placements, complete named lists, and targeting returned inline | **full**: shared `AutoItemsConsumableUseGameAction` / `game_consumable(mode=use)` with fixed-one multi-buy, live player predicates, exact queue-outcome gate, and B-001 post-state | `R3`; `C0+C-LIST+C-QUEUE+C-COMBAT` | `V-TGT-01`,`V-UI-06`; P1 |
| `V-CONS-02` cancel queued consumable use | `UIConsumableRefList.CancelConsumable` `0x0600229D` -> `ConsumableSO.CancelUsage` | exact pending usage with stable UUID/result owner | exact usage cancelled and removed, queue advances; complete newer holding/lists/next decisions inline | **full**: shared `AutoItemsConsumableUseGameAction` / `game_consumable(mode=cancel)` verifies selected usage identity, cancellation, removal, and queue outcome | `R3`; `C0+C-QUEUE+C-PAY` | `V-CONS-01`; P2 |
| `V-CONS-03` discard consumables | `UIConsumableRefList.DiscardConsumable` `0x0600229E` -> multi-buy `ConsumableSO.Discard` | positive owned amount; requested amount clamped live | exact clamped holding removal; complete newer holding/lists/next decisions inline | **full**: shared `AutoItemsConsumableUseGameAction` / `game_consumable(mode=discard,amount=...)`; no refund/payment fiction | `R4`; `C0+C-LIST`, one/all/zero and confirmation | `V-UI-06`; P4 |
| `V-CONS-04` toggle consumable randomization | `UIConsumableRefItem.TurnRandomizationOn/Off` `0x06002296/97` -> `ConsumableSO.SetRandomization` | exact consumable supports randomization and differs from requested state | exact flag outcome; complete newer holding/lists/next decisions inline | **full**: shared `AutoItemsConsumableUseGameAction` / `game_consumable(mode=set_randomization,enabled=...)` | `R2`; `C0+C-UI`, unsupported and reload (`EG-03`) | `V-CONS-01`; P3 |
| `V-CONS-05` reorder consumables | `UIConsumableRefList.OnDrop` `0x0600229A` -> same-list swap, observable update, hotbar rule tail | exact source occurs once; same live list; valid distinct destination | complete same-list UUID sequence exactly swaps source/destination; complete newer named lists/next decisions inline | **full**: shared `AutoItemsConsumableUseGameAction` / `game_consumable(mode=move,list=...,destination=...)` | `R1`; `C0+C-LIST` | none; P3 |
| `V-EQ-01` equip/increase an artifact stack | equipment list click event (`UIEquipmentList.SelectEquipment` `0x060023CE`, `EG-01`) -> `EquipmentManager.ToggleItem/EquipItem` `0x06000514-17`: cost/slot/type/max/multi-buy clamp, list stack, `EquipmentSO.Equip` | exact created equipment UUID/type; live usage affordability; UI global/type slot room or already stacked; below max; positive global multi-buy | exact target stack increases by the native clamped amount; success returns the complete newer named equipment row with holdings, slot counts, usage costs and next equip/unequip decisions; usage/effects/attunement are observations, never accounting gates | **full**: `game_equipment(mode=equip)` / `EquipmentLoadoutGameAction`, with one lifecycle binding set and [audited dossier](equipment-loadout-native-pipeline.md) | `R3/R4`; `C0+C-LIST+C-PAY`, new stack/increase/full slots/attuning | `V-ART-01`,`V-LVL-01`; P1 |
| `V-EQ-02` unequip/decrease an artifact stack | equipment click -> `EquipmentManager.UnEquipItem` `0x06000518/19`: clamp multi-buy, unstack, refresh, call `EquipmentSO.Equip(remaining)` | exact created equipment UUID/type; positive current stack and multi-buy | exact target stack decreases by the native clamped amount; success returns remaining stacks, released slot capacity, and both next decisions inline; usage/effect release is observation only | **full**: `game_equipment(mode=unequip)` / the same `EquipmentLoadoutGameAction` and dossier | `R3`; `C0+C-LIST`, some/all and last/type-slot effects | `V-EQ-01`; P1 |
| `V-LOAD-01` apply/select a player loadout | `UILoadoutList.OnLoadoutClick` `0x0600248B` event -> `LoadoutManager.SetLoadout/LoadLoadout`; listener detail `EG-01` | loadout exists; referenced spells/equipment/alchemy valid and available; capacities/costs permit native application | selected loadout and optionally spells/equipment/alchemy lists/effects/capacity change; receipt: loadout UUID, all before/after ordered typed identities/stacks, skipped/refused entries and exact capacity/effect deltas | **none** | `R4`; `C0+C-LIST`, stale/missing/cap-blocked entries and rollback evidence | component creation/equip verbs; P1 |
| `V-LOAD-02` save current state into a loadout | `LoadoutManager.SaveLoadout`; `UILoadoutPage.ToggleSaveEquipment/ToggleSaveAlchemy` `0x06002502/03`; `UILoadoutSection.ToggleSaved` `0x06002493` | target loadout selected; section flags valid | stored spell/equipment/alchemy snapshots and flags/metadata change; live gameplay lists should not; receipt: loadout identity, flags, before/after stored ordered identities/stacks, live-state no-delta proof | **none** | `R4`; `C0+C-LIST`, section flags, overwrite and reload | `V-SPELL-03`,`V-EQ-01`,`V-ALCH-03`; P2 |
| `V-LOAD-03` save a snapshot loadout | `UISnapshotLoadout.SaveLoadout` `0x060026BD`; `UISnapshotLoadoutList.SaveAlchemySnapshot/SaveEquipmentSnapshot` copies current stacks | snapshot slot and source list current | snapshot records are overwritten from live stacks; receipt: snapshot identity/type, before/after stored ordered UUID+stack records, and byte-for-byte unchanged live list/effects | **none** | `R2`; `C0+C-LIST`, empty/full source and overwrite | `V-EQ-01` or `V-ALCH-03`; P2 |
| `V-LOAD-04` load a snapshot loadout | `UISnapshotLoadout.LoadLoadout` `0x060026BE`; `UISnapshotLoadoutList.LoadAlchemySnapshot/LoadEquipmentSnapshot` applies stored stacks | snapshot exists; every target entry revalidated for availability/capacity | live stack list/effects/capacity change; receipt: snapshot identity/type, before/after live ordered stacks, exact skipped/refused entries and effect/capacity deltas; snapshot remains unchanged | **none** | `R4`; `C0+C-LIST`, stale/missing/cap-blocked entries | `V-LOAD-03`,`V-EQ-01` or `V-ALCH-03`; P2 |
| `V-LOAD-05` clear a snapshot loadout | `UISnapshotLoadout.ClearLoadout` `0x060026BC` | snapshot slot exists and confirmation/presentation allows clear | stored snapshot records clear; receipt: snapshot identity/type, before/after records and unchanged live list/effects | **none** | `R2`; `C0+C-LIST`, empty/idempotent and reload | `V-LOAD-03`; P3 |

### Harvesting and plot actions

| Verb | UI route and native pipeline | Preconditions/gating | Ordered effects and exact receipts | Existing coverage and named gap | Risk and disposable-save check | Dependencies; playthrough priority |
|---|---|---|---|---|---|---|
| `V-HARV-01` add/increase a harvest element | `UIHarvestList.OnHarvestClick` `0x0600242C` absent branch -> harvest instance list add, clamped by max/multi-buy | element/harvest type visible/available; slot/max/capacity; positive amount | instance created/stack grows, production/usage/effects change; receipt: element/type/instance identities, requested/accepted, before/after stacks+production+usage | **none** | `R3`; `C0+C-LIST` | `V-ECO-01`,`V-UI-06`; P2 |
| `V-HARV-02` remove/decrease a harvest element | same `OnHarvestClick` present branch -> harvest instance list remove | active instance and positive stack | stack/list decreases and production/usage/effects release; receipt: requested/removed, before/after stacks/order/production/usage | **none** | `R3`; `C0+C-LIST` | `V-HARV-01`; P2 |
| `V-HARV-03` add/increase a harvest action | `UIHarvestActionList.OnActionClick` `0x0600241F` absent branch -> `HarvestActionInstance` add/stack | action available for selected harvest; cap/inputs/multi-buy | action instance/quantity and harvest effects/cost/drain change; receipt: stable action/harvest identities, before/after stack/effects/exact deltas | **none** | `R3`; `C0+C-LIST+C-PAY` | `V-HARV-01`; P2 |
| `V-HARV-04` remove/decrease a harvest action | same `OnActionClick` present branch -> action instance remove | active action; positive stack | action quantity/list/effects/cost/drain decrease; receipt: identities, requested/removed, exact before/after list/effects/deltas | **none** | `R3`; `C0+C-LIST+C-PAY` | `V-HARV-03`; P2 |
| `V-PLOT-01` add/increase a plot-node action | `UIPlotNodeActionList.OnActionClick` `0x06002596` absent branch -> `PlotNodeActionInstanceListVariable.AddInstance` `0x060016B7`; current suite `GameHarvestGameAction` uses one audited plot/action pair | selected plot/action stable UUID+type; available; quantity/cap/usage/input/multi-buy gates | instance/quantity, plot action progress, cost/drain/effects change; receipt: plot+action+instance identities, requested/accepted, exact before/after quantities/resources/progress/effects | **partial**: `game_harvest` is full only for the currently audited pair; no generic action catalog/type coverage | `R3`; `C0+C-LIST+C-PAY`, each action type and generic identity refusal | `V-UI-01`,`V-UI-06`; P1 world progression |
| `V-PLOT-02` cancel/decrease a plot-node action | same `OnActionClick` present branch -> cancel/`PlotNodeActionInstanceListVariable.RemoveInstance` `0x060016B8` | current instance; cancellable; positive quantity | instance/quantity/progress/effects removed or reduced; refund detail `EG-02`; receipt: exact before/after quantity/progress/resource/refund/effects | **partial**: current `game_harvest` pair supports its audited remove shape only; generic family absent | `R3`; `C0+C-LIST+C-QUEUE+C-PAY` | `V-PLOT-01`; P1 |

<!-- SURFACE-MAP-START -->
## Closed surface enumeration

This section is the other half of the coverage proof. A row with verb IDs is a
mapped command surface. `EX-READ` means the authored view only renders facts,
progress, archive, tips, statistics, or details; inspection remains `V-UI-04`
but no additional mutation candidate was found in the matching UI methods.
`EX-GATE` means a `ViewSO` is a conditional/container visibility flag rather
than an interactable command. `EX-AUTO` means the assembly exposes only
automatic state/presentation and no player command. These exclusions were
tested against the candidate-method query above; they are not name-based
guesses.

### All authored `ViewSO` assets

Every row's native evidence is `ViewSO` with the listed UUID/internal name, from
`data/entity-display-names.tsv`; its discovery source is `TSV ViewSO + audited
Assembly-CSharp.dll UI candidate sweep`.

| Surface | Authored UUID | Internal / display name | Disposition |
|---|---|---|---|
| `S-VIEW-001` | `00be3942-b91b-4998-9479-68acee369ce6` | `TimeChallengesActive` / Active | `V-UI-01`,`V-UI-04`,`V-CHAL-02`,`V-CHAL-03` |
| `S-VIEW-002` | `05895bef-ee78-4ca1-9e6a-34696ef5dabd` | `MagicCasting` / Casting | `V-UI-01`,`V-SPELL-07`,`V-TGT-01`,`V-TGT-02`,`V-TGT-03` |
| `S-VIEW-003` | `05e1f545-5daa-4bd0-9cbb-8770f4cf6a13` | `WorkshopArtifactCreate` / Create | `V-UI-01`,`V-ART-01` |
| `S-VIEW-004` | `07dfae7e-76b9-4b38-bf81-38abc40b9ed7` | `MasteriesEnabled` / authored blank | `EX-GATE` (conditional mastery visibility; no command method) |
| `S-VIEW-005` | `0dff4114-15a2-4e1f-a953-3f4b1fef3172` | `RitualsActivate` / Rituals | `V-UI-01`,`V-RIT-03`,`V-RIT-04`,`V-RIT-05` |
| `S-VIEW-006` | `0fbee815-f10b-44be-a47e-2a780d3915ff` | `WorkshopArtifactUpgrade` / Upgrade | `V-UI-01`,`V-LVL-01`,`V-LVL-02` |
| `S-VIEW-007` | `129f4d68-0ba9-4274-8078-7e6931f20e77` | `ScholarResearch` / Research | `V-UI-01`,`V-RES-01`,`V-RES-02`,`V-RES-03`,`V-RES-04` |
| `S-VIEW-008` | `12d82cd9-708e-4275-bd65-a4ef0b8c2fb3` | `ScholarResearchInnovation` / Innovation | `V-UI-01`,`V-RES-01`,`V-RES-02`,`V-RES-03`,`V-RES-04` |
| `S-VIEW-009` | `157ac4a3-ac79-465a-9ded-bd65be4ccb8e` | `PlayerResources` / Resources | `V-UI-01`,`V-UI-04`,`V-UI-09` |
| `S-VIEW-010` | `166cde99-0d90-4e03-9832-f04dbf37691a` | `WorldAgromancyDruidryLvs` / authored blank | `EX-READ` (level summary) |
| `S-VIEW-011` | `167f94b8-baf9-4829-98c1-4f2d3af79b86` | `ScholarTableVisible` / authored blank | `EX-GATE` |
| `S-VIEW-012` | `1f4ebfce-6571-4563-a018-23f009a290a4` | `PlayerStatsAttributes` / Statistics | `EX-READ` |
| `S-VIEW-013` | `1f863363-985e-4b71-869c-7d02281a8ede` | `TimeTimeRuneUpgrade` / Upgrade | `V-UI-01`,`V-LVL-01`,`V-LVL-02` |
| `S-VIEW-014` | `241d97fb-dc77-493b-a3f1-3abdb1596819` | `MagicWizardy` / Wizardry | `V-UI-01`,`V-ECO-01`,`V-ECO-02`,`V-ECO-03` |
| `S-VIEW-015` | `2754f10f-d88c-4b15-946b-e9a97c731300` | `MagicGlyphsUpgrade` / Upgrade | `V-UI-01`,`V-LVL-01`,`V-LVL-02` |
| `S-VIEW-016` | `27fdcb79-accc-460e-95ad-9e3ea41e7391` | `ScholarScribe` / Scribe | `V-UI-01`,`V-CRAFT-01`,`V-CRAFT-02`,`V-CRAFT-03` |
| `S-VIEW-017` | `2e0d3cad-c5f2-4460-9ec7-16d8b5eeea70` | `PersistentResetInfo` / Information | `EX-READ` |
| `S-VIEW-018` | `312ad7da-07a6-4296-9f91-f4eda43e4fe4` | `AlchemistsLab` / authored blank | `EX-GATE` (container visibility) |
| `S-VIEW-019` | `34624830-d04e-44f6-9410-6098bd349399` | `AlchAlchemyLevel` / Alch Alchemy Level | `V-UI-01`,`V-ALCH-06` |
| `S-VIEW-020` | `35141301-f428-450f-93b8-ce28f506d704` | `MagicGlyphsDiscover` / Glyphcraft | `V-UI-01`,`V-DISC-01` |
| `S-VIEW-021` | `3a368304-adfc-4a45-b533-04963a792a03` | `WorldDimensional` / Dimensional | `V-UI-01`,`V-PLOT-01`,`V-PLOT-02` |
| `S-VIEW-022` | `3ae45ec0-4449-4903-b3d0-b5182e03dca3` | `ScreenAlchemy` / Alchemy | `V-UI-01` (core-view container) |
| `S-VIEW-023` | `3e1c6e59-d2e1-460b-a7f9-d782e54ba57b` | `ThoughtsView` / authored blank | `V-UI-02`,`V-UI-04` |
| `S-VIEW-024` | `4139e87e-0619-4cda-88a4-0e4d50a4d90e` | `WorkshopArtifactLoadout` / Loadout | `V-UI-01`,`V-EQ-01`,`V-EQ-02`,`V-LOAD-03`,`V-LOAD-04`,`V-LOAD-05` |
| `S-VIEW-025` | `4263c888-2034-498d-acf8-a82617e850b9` | `MagicReserveLevel` / Magic Reserve Level | `V-UI-01`,`V-LVL-01`,`V-LVL-02` |
| `S-VIEW-026` | `430da1f6-b02b-4ca2-8948-2c00929138a3` | `ScholarConceptLoadout` / Loadout | `V-UI-01`,`V-ALCH-03`,`V-ALCH-04`,`V-ALCH-05`,`V-LOAD-03`,`V-LOAD-04`,`V-LOAD-05` |
| `S-VIEW-027` | `4360ab1a-b682-4db2-9a24-d4758d915fa3` | `PlayerStatsAchievements` / Achievements | `EX-READ` |
| `S-VIEW-028` | `443bc384-3df6-49ac-aa52-28abe080595c` | `RitualsDiscover` / Discover | `V-UI-01`,`V-RIT-01`,`V-RIT-02` |
| `S-VIEW-029` | `4912da46-bf64-4e34-b267-54e466bcf506` | `WorkshopBench` / authored blank | `EX-GATE` (container visibility) |
| `S-VIEW-030` | `4cd39f4d-9052-43e8-aa8d-17ab7074e4d0` | `ScholarScholarism` / Scholar | `V-UI-01` (core-view container) |
| `S-VIEW-031` | `4e21a048-e1b0-4e46-b673-680a081d9998` | `InventoryDiscardMode` / Inventory Discard Mode | `V-UI-01`,`V-CONS-03` |
| `S-VIEW-032` | `512c9ca7-87d1-4ede-8117-fba7f19f494e` | `IsInCombat` / Is In Combat | `EX-GATE` (combat-state visibility) |
| `S-VIEW-033` | `58fad89f-0627-471c-b24d-f7ae3537bba4` | `ConsumablesView` / Inventory | `V-CONS-01`,`V-CONS-02`,`V-CONS-03`,`V-CONS-04`,`V-CONS-05` |
| `S-VIEW-034` | `5abe3613-adb7-491c-b647-97b4a7901397` | `MagicSpellbookSpellTypes` / Spell Types | `V-UI-01`,`V-UI-04`,`V-SPELL-08`,`V-SPELL-09` |
| `S-VIEW-035` | `5ed81e80-c410-477b-bcdd-2de9ee88ac29` | `SettingsGraphics` / Graphics | `V-UI-08` |
| `S-VIEW-036` | `601299ff-6811-493c-b0f2-30acd0fef68b` | `AlchAlchemyManage` / Manage | `V-ALCH-03`,`V-ALCH-04`,`V-ALCH-05`,`V-ALCH-06` |
| `S-VIEW-037` | `636597c9-c0b0-445a-ad5c-b7a5e4aa6fd0` | `PlayerStatsTips` / Tips | `EX-READ` |
| `S-VIEW-038` | `668a2a7a-468f-4e0e-b182-979b12a4b0ad` | `WorkshopArtifact` / Artifacts | `V-UI-01` (subview container) |
| `S-VIEW-039` | `66bfaae2-959e-4f58-b76a-7dfef4fe22d0` | `ScholarConcepts` / Concepts | `V-DISC-02`,`V-DISC-03`,`V-DISC-04`,`V-DISC-05`,`V-ALCH-03`,`V-ALCH-04`,`V-ALCH-05` |
| `S-VIEW-040` | `684ef880-4b1f-4e20-9651-582571c6c1b1` | `WorkshopCrafting` / Crafting | `V-UI-01` (subview container) |
| `S-VIEW-041` | `6a584a8d-e726-4a9a-b9d3-025e0f13e2bf` | `WorldAgromancy` / Agromancy | `V-HARV-01`,`V-HARV-02`,`V-HARV-03`,`V-HARV-04` |
| `S-VIEW-042` | `6ddbd81a-113b-40ff-9358-741115a419cb` | `TimeChallengesListView` / All | `V-CHAL-01`,`V-CHAL-02`,`V-CHAL-03`,`V-CHAL-04` |
| `S-VIEW-043` | `6f69a427-98ca-4f12-a05b-3433bdc76ea9` | `TimeRunePreferredView` / authored blank | `EX-READ` (preferred-rune presentation; no command candidate) |
| `S-VIEW-044` | `70aabd3c-7790-4771-a863-5e5c41bff493` | `AlchAlchemyDiscover` / Learn | `V-ALCH-01`,`V-ALCH-02` |
| `S-VIEW-045` | `7ed21365-99f6-447b-b110-adb6029a8764` | `ScholarResearchExpert` / Expert | `V-RES-01`,`V-RES-02`,`V-RES-03`,`V-RES-04` |
| `S-VIEW-046` | `82047a67-9d57-438c-b5b1-99377e242e32` | `TimeTimeRuneCreate` / Create | `V-DISC-01` |
| `S-VIEW-047` | `8c97ce79-28bb-453e-967e-504bdb7864c3` | `TimeChallenges` / Challenges | `V-UI-01` (subview container) |
| `S-VIEW-048` | `8ea19922-be93-4e50-a0b1-65a14fb68ea5` | `MagicGlyphs` / Augments | `V-SPELL-06`,`V-LVL-01`,`V-LVL-02` |
| `S-VIEW-049` | `9583391e-58d6-4ac7-b0fd-e9bd8b29d9d1` | `UpgradesView` / Upgrades | `V-ECO-02` |
| `S-VIEW-050` | `9746b42e-1b57-4f98-8ba4-f64b4c34d8c1` | `ScreenMagic` / Magic | `V-UI-01` (core-view container) |
| `S-VIEW-051` | `97ff402b-8d48-4454-b39a-cbd8b5b78c5f` | `PlayerStatsGroupAttr` / Group Attr. | `EX-READ` |
| `S-VIEW-052` | `9be4b78c-01e3-4ef4-ad77-a5ab735019d3` | `WorkshopStructures` / Workshop | `V-ECO-01`,`V-ECO-02`,`V-ECO-03` |
| `S-VIEW-053` | `9cfb2e96-ee2f-4001-8397-7c1680ab9573` | `ScreenRitual` / Rituals | `V-UI-01` (core-view container) |
| `S-VIEW-054` | `9ea5d6e1-739b-4dec-832b-f5f3ba3ad2ca` | `ScreenScholar` / Scholar | `V-UI-01` (core-view container) |
| `S-VIEW-055` | `a08876ae-d70d-43f6-94ef-59fbcb84e888` | `RitualBench` / Ritual Bench | `V-RIT-01`,`V-RIT-02`,`V-RIT-03`,`V-RIT-04`,`V-RIT-05` |
| `S-VIEW-056` | `a1afb6eb-bc9b-4e93-8ad8-33d642c8056e` | `MagicSpellbookManage` / Spells | `V-SPELL-03`,`V-SPELL-04`,`V-SPELL-05`,`V-SPELL-06` |
| `S-VIEW-057` | `a7d5688e-b2ec-431f-a0c7-6d52c8731245` | `MagicAugmentGlyphs` / authored blank | `V-SPELL-06` |
| `S-VIEW-058` | `accf9abb-d916-4fdf-96d7-1b1d5fdd548c` | `MagicSpellbookLoadout` / Loadout | `V-SPELL-03`,`V-SPELL-04`,`V-SPELL-05`,`V-SPELL-06`,`V-LOAD-03`,`V-LOAD-04`,`V-LOAD-05` |
| `S-VIEW-059` | `b2c274cf-af32-480a-82a0-1a2c3f79a817` | `PlayerLoadoutSpells` / Spells | `V-LOAD-01`,`V-LOAD-02` |
| `S-VIEW-060` | `b7a7a33f-2116-4f35-ba71-68b715a5c916` | `SettingsMain` / Main | `V-UI-07`,`V-UI-08`,`V-UI-10` |
| `S-VIEW-061` | `b8ebce37-ba04-42bc-b36d-63f7a7766a21` | `WorkshopArtificer` / Artificer | `V-UI-01` (subview container) |
| `S-VIEW-062` | `bab5aa00-b0e9-4886-9849-6531f0cb8639` | `AlchAlchemyLoadout` / Loadout | `V-ALCH-03`,`V-ALCH-04`,`V-ALCH-05`,`V-LOAD-03`,`V-LOAD-04`,`V-LOAD-05` |
| `S-VIEW-063` | `bc720665-aac0-4d02-a9fd-4640d4bcde50` | `AlchMaterials` / Materials | `V-ALCH-01`,`V-UI-04` |
| `S-VIEW-064` | `c0fb08cc-3ee0-42b4-b965-10e871e0fd8f` | `MagicSpellOutputLevel` / authored blank | `V-SPELL-06` |
| `S-VIEW-065` | `c26215c0-0556-48e0-8ce5-5efea5507dae` | `RitualsMystic` / Mysticism | `V-ECO-01`,`V-ECO-02` |
| `S-VIEW-066` | `c4f28ff4-0fd3-4c12-8992-2de6bc0ced30` | `WorkshopCraftingManual` / Manual | `V-CRAFT-01`,`V-CRAFT-03` |
| `S-VIEW-067` | `c5f53567-7c5e-4c27-9d06-e20c266c90e5` | `Hotbar` / authored blank | `V-SPELL-07`,`V-TGT-01`,`V-TGT-02`,`V-TGT-03` |
| `S-VIEW-068` | `c662d72a-2211-4cd6-b9d2-104071a5e6e9` | `ScreenWorkshop` / Workshop | `V-UI-01` (core-view container) |
| `S-VIEW-069` | `c7f7afc9-b698-446f-bfb9-cd0121dab86f` | `WorldPanel` / authored blank | `V-UI-01` (world subview container) |
| `S-VIEW-070` | `c821f3fd-927f-475e-ab70-365729f410dd` | `AutoAttackView` / Auto Attack View | `EX-AUTO` (automatic state/presentation; no player command surfaced) |
| `S-VIEW-071` | `ca7efd1b-10e1-41d6-b197-3e03998ecd35` | `MagicSpellbookLearnRecipes` / authored blank | `V-SPELL-01`,`V-SPELL-02` |
| `S-VIEW-072` | `ca934900-0253-4f71-93e9-733fb91132b7` | `MagicSpellbook` / Spellbook | `V-UI-01` (subview container) |
| `S-VIEW-073` | `cb8088a2-cd7c-49a7-ae3e-52c2249be18c` | `TimeReset` / Reset | `V-PREST-01` |
| `S-VIEW-074` | `cdfbbee5-7fa2-41fe-a6aa-903beb692fb2` | `MagicSpellbookLearn` / Unlock | `V-DISC-02`,`V-DISC-03`,`V-DISC-04`,`V-DISC-05`,`V-SPELL-01`,`V-SPELL-02` |
| `S-VIEW-075` | `ce207aba-b835-4455-a1b7-e10e18db9ce2` | `WorldAspects` / Aspects | `V-HARV-01`,`V-HARV-02`,`V-HARV-03`,`V-HARV-04` |
| `S-VIEW-076` | `ce6044da-15dd-408e-9524-14e163314f19` | `ScreenTime` / Time | `V-UI-01` (core-view container) |
| `S-VIEW-077` | `d17ea5ff-8118-48e9-8a52-b369460874e4` | `PinnedBarView` / authored blank | `V-UI-05` |
| `S-VIEW-078` | `d46e2bf3-7930-41bd-b2aa-4810c23f1acc` | `WorldDruidry` / Druidry | `V-HARV-01`,`V-HARV-02`,`V-HARV-03`,`V-HARV-04` |
| `S-VIEW-079` | `d50af282-9b93-41b2-9b1f-773ed82d8ad9` | `CraftPageManual` / Manual | `V-CRAFT-01`,`V-CRAFT-03` |
| `S-VIEW-080` | `d50ea5ec-a02c-47cb-b30f-50fa10157cca` | `MagicSpellBookAvailable` / authored blank | `V-SPELL-03`,`V-SPELL-04`,`V-SPELL-05`,`V-SPELL-06` |
| `S-VIEW-081` | `dc7234dc-1461-4fb9-9ccd-5e6c0bdb6f0f` | `LoadoutChangersVisible` / authored blank | `EX-GATE` |
| `S-VIEW-082` | `df7a5dfe-c75b-4961-9747-42e7953faa22` | `CraftPageAutomate` / Automate | `V-CRAFT-02`,`V-CRAFT-03` |
| `S-VIEW-083` | `e2079eb5-e640-48f1-91df-5c1fe9d24da5` | `PlayerLoadoutEquipment` / Equipment | `V-LOAD-01`,`V-LOAD-02` |
| `S-VIEW-084` | `e46ba960-d8a5-4801-985a-41c411c711c6` | `SettingsGame` / Game | `V-UI-07`,`V-UI-08` |
| `S-VIEW-085` | `e7d84178-5f4b-4e7f-8b5f-62ccd7993b75` | `WorkshopCraftingAutomated` / Automate | `V-CRAFT-02`,`V-CRAFT-03`,`V-CRAFT-04` |
| `S-VIEW-086` | `e9e3b545-0af7-4a69-a56c-38a8f6340fa4` | `PlayerLoadoutAlchemy` / Alchemy | `V-LOAD-01`,`V-LOAD-02` |
| `S-VIEW-087` | `ec07eaec-5940-4b4f-99c0-e51b326e233c` | `AlchemyCanUseResources` / authored blank | `EX-GATE` (availability gate) |
| `S-VIEW-088` | `ed944822-303e-45fa-a201-5a1be0182bab` | `LevelAllSpellsButton` / authored blank | `V-SPELL-09` |
| `S-VIEW-089` | `efd92b91-780a-4e47-b65b-4056a9d81af5` | `ScreenWorld` / World | `V-UI-01` (core-view container) |
| `S-VIEW-090` | `f0e4b59a-b510-4de3-ae3e-dfca0fd76239` | `ScholarConceptDiscover` / Discover | `V-DISC-02`,`V-DISC-03`,`V-DISC-04`,`V-DISC-05` |
| `S-VIEW-091` | `f10bf25a-050d-49a1-9f21-b0598a0caed2` | `TimeTimeRuneArchive` / Archive | `EX-READ` |
| `S-VIEW-092` | `f1392b0c-3980-486a-a346-45b88afb167f` | `ScholarResearchTech` / Technology | `V-RES-01`,`V-RES-02`,`V-RES-03`,`V-RES-04` |
| `S-VIEW-093` | `f6594b78-4aa7-49b5-8abc-4ada141ceb22` | `SpellBookInfo` / Details | `V-UI-04`,`V-SPELL-04`,`V-SPELL-06` |
| `S-VIEW-094` | `f9a453b0-fd47-4fcf-8022-a4cc5ffbf064` | `AlchAlchemist` / Alchemist | `V-UI-01` (subview container) |
| `S-VIEW-095` | `fbf9b8aa-4b0b-4e3b-b92b-486d56103537` | `AlchAlchemy` / Alchemy | `V-UI-01` (subview container) |
| `S-VIEW-096` | `fe5f2e95-ec05-4a5e-8330-a6a8e307172e` | `PersistentResetChallenges` / Challenges | `V-CHAL-01`,`V-CHAL-04`,`V-PREST-01` |
| `S-VIEW-097` | `fe83b0bd-c927-4c2e-a5c6-004d480afc23` | `TimeTimeRunes` / Time Runes | `V-UI-01` (subview container) |

### All authored global key bindings

Every row's native evidence is `KeyBindingVariable` with the listed authored
UUID/internal name, from `data/entity-display-names.tsv`; discovery source is
`TSV KeyBindingVariable + global-input assembly sweep`.

| Surface | Authored UUID | Internal / display name | Mapped verb |
|---|---|---|---|
| `S-KEY-001` | `0049189e-7c6f-483f-a86b-ca6c18bedb2d` | `GoToLearnSpells` / Go To Learn Spells | `V-UI-01` |
| `S-KEY-002` | `03700f86-4baf-4b81-a108-160d18957f15` | `CastSpell5` / Spell 5 | `V-SPELL-07` |
| `S-KEY-003` | `05c06263-25a7-4e7d-ac8c-ae83b3a92dc8` | `GoToTimeRunes` / Go To Time Runes | `V-UI-01` |
| `S-KEY-004` | `0a001250-e48d-4376-a7cb-80deffaf307a` | `GoToWorkshop` / Go To Structures | `V-UI-01` |
| `S-KEY-005` | `0b1d61e5-b0ef-4083-af03-876156d3ca36` | `CastSpell7` / Spell 7 | `V-SPELL-07` |
| `S-KEY-006` | `0dc091e1-c412-4ded-8427-6655a42af733` | `MoreInfo` / More Info | `V-UI-04` |
| `S-KEY-007` | `0f65d8ce-0911-418f-8a19-042506654b4c` | `CastSpell8` / Spell 8 | `V-SPELL-07` |
| `S-KEY-008` | `1f1bb6b4-521f-4c68-b5a0-a1aacca829ca` | `GoToAgromancy` / Go To Agromancy | `V-UI-01` |
| `S-KEY-009` | `20d29fa7-2bd1-4508-9858-03f0620dcd6a` | `GoToDruidry` / Go To Druidry | `V-UI-01` |
| `S-KEY-010` | `24820a1f-fc51-4351-bf6c-cd5fbf76f5d2` | `TabUp` / Tab Up | `V-UI-01` |
| `S-KEY-011` | `2fe23b32-c69a-4232-9b07-6e2a7a8262b2` | `Loadouts` / Open Loadouts | `V-UI-02` |
| `S-KEY-012` | `3533ba35-dda1-432e-b728-bda70ec04fe0` | `GoToScholarism` / Go To Scholarism | `V-UI-01` |
| `S-KEY-013` | `38110288-aee6-46e3-bfbd-557e1804ab2f` | `GoToLoadout` / Go To Loadout | `V-UI-01` |
| `S-KEY-014` | `3f6eff9a-fda5-4ea8-b481-6fdcb93d4184` | `GoToDimensional` / Go To Dimensional | `V-UI-01` |
| `S-KEY-015` | `47ac504b-480c-43fb-a72b-e26e7c5ebfab` | `UseConsumable2` / Consumable 2 | `V-CONS-01` |
| `S-KEY-016` | `47fd15ba-1d69-4561-ae9a-fbcdd35e5183` | `CastSpell3` / Spell 3 | `V-SPELL-07` |
| `S-KEY-017` | `49333dbb-3ba7-4816-8a89-224ee642ee1c` | `GoToCrafting` / Go To Crafting | `V-UI-01` |
| `S-KEY-018` | `4c51c4ac-9413-456b-8194-5cdfd27c0e56` | `GoToAugments` / Go To Augments | `V-UI-01` |
| `S-KEY-019` | `4c80686f-9fff-455a-8082-d4d5677b560f` | `TabDown` / Tab Down | `V-UI-01` |
| `S-KEY-020` | `4ca299cd-6ed5-4970-8bab-b20f0f30fd8f` | `GoToResearch` / Go To Research | `V-UI-01` |
| `S-KEY-021` | `4f652d05-d7fb-4bc2-ac01-2f37cfdea0db` | `GoToAlchemy` / Go To Alchemy | `V-UI-01` |
| `S-KEY-022` | `524fbb60-9d3a-4f54-8876-ece84f37b383` | `GoToScribing` / Go To Scribing | `V-UI-01` |
| `S-KEY-023` | `559fd2b0-1bcf-42bd-8788-d1f7357c0adf` | `CastSpell6` / Spell 6 | `V-SPELL-07` |
| `S-KEY-024` | `595a9725-14cb-408c-bf8c-a1700ca5f492` | `GoToMystic` / Go To Mystic | `V-UI-01` |
| `S-KEY-025` | `5c41a525-ee67-4d12-8e7b-ee44f2354c94` | `GoToCasting` / Go To Casting | `V-UI-01` |
| `S-KEY-026` | `6304d2f5-cc44-46c8-b991-1e8539fe7796` | `GoToLearnAlchemy` / Go To Learn Alchemy | `V-UI-01` |
| `S-KEY-027` | `636efb3f-e825-42a4-a61b-b34ee2f30857` | `GlobalSearchBinding` / Search | `V-UI-03` |
| `S-KEY-028` | `6ca92b42-e3c4-48b7-abc7-9cd8554c89f6` | `CastSpell2` / Spell 2 | `V-SPELL-07` |
| `S-KEY-029` | `7109f1d5-af1b-4e60-b4c9-709ddb22c866` | `Inventory` / Open Inventory | `V-UI-02` |
| `S-KEY-030` | `71135080-bd43-4b6c-8723-2233829b3aea` | `GoToAlchemist` / Go To Alchemist | `V-UI-01` |
| `S-KEY-031` | `7fd04399-007c-425c-98c0-71976856ce98` | `GoToRituals` / Go To Rituals | `V-UI-01` |
| `S-KEY-032` | `819e3d5f-cc06-42ff-be02-0870232edccf` | `GoToChallenges` / Go To Challenges | `V-UI-01` |
| `S-KEY-033` | `8546827b-5fb1-4f01-9d67-908e75dba284` | `IncreaseBuy` / Increase Buy | `V-UI-06` |
| `S-KEY-034` | `9447c976-33fd-411d-8e11-e131670d5b03` | `CastSpell4` / Spell 4 | `V-SPELL-07` |
| `S-KEY-035` | `95b6485b-0cae-4d04-bcf6-bcfcb03f933b` | `GoToDiscoverRituals` / Go To Discover Rituals | `V-UI-01` |
| `S-KEY-036` | `95d718f4-3a05-4249-90e8-bd8f6999a12b` | `UseConsumable3` / Consumable 3 | `V-CONS-01` |
| `S-KEY-037` | `9f83e5ee-ed97-4b4d-b590-f7cc3f00594b` | `TabRight` / Tab Right | `V-UI-01` |
| `S-KEY-038` | `a4b549aa-caee-49de-8bed-3fdab3bd2907` | `GoToManageSpells` / Go To Manage Spells | `V-UI-01` |
| `S-KEY-039` | `a6def858-9b59-448a-8aa8-514a503c11f4` | `InspectTooltip` / Inspect Tooltip | `V-UI-04` |
| `S-KEY-040` | `be6fd214-03b2-4f64-9a2a-60b910d3ccfd` | `GoToReset` / Go To World Reset | `V-UI-01` |
| `S-KEY-041` | `c51b7667-ebe5-4145-aabb-062cd7fd917d` | `GoToArtificer` / Go To Artificer | `V-UI-01` |
| `S-KEY-042` | `cb982440-a2a9-488d-a5a5-865d2f2f07a6` | `GoToArtifacts` / Go To Artifacts | `V-UI-01` |
| `S-KEY-043` | `ce0c058c-b674-49ca-bc18-0d48fafae8c3` | `GoToMaterials` / Go To Materials | `V-UI-01` |
| `S-KEY-044` | `d4ca295f-4795-4b0f-a9e4-13a3decc438a` | `MaxBuy` / Max Buy | `V-UI-06` |
| `S-KEY-045` | `dc8fa545-c929-49bd-99f3-b06cc74b6f22` | `UseConsumable1` / Consumable 1 | `V-CONS-01` |
| `S-KEY-046` | `dcaa65ff-ca70-4a09-a44e-fb6447d51cb2` | `GoToWizardry` / Go To Wizardry | `V-UI-01` |
| `S-KEY-047` | `df8dd1c3-e595-46d7-8ff6-6ba5041d5917` | `UseConsumable4` / Consumable 4 | `V-CONS-01` |
| `S-KEY-048` | `e16d4797-4d30-4748-9547-a1091d1efe6e` | `GoToConcepts` / Go To Concepts | `V-UI-01` |
| `S-KEY-049` | `ecae8105-3d38-485d-9c5d-5b9d0839f4a6` | `CastSpell9` / Spell 9 | `V-SPELL-07` |
| `S-KEY-050` | `f1b0ddfb-94fd-4257-ad9d-e133f92d8921` | `ToggleTooltip` / Toggle Tooltip | `V-UI-04` |
| `S-KEY-051` | `f8e013cb-19a7-43ed-b971-71ef2fc328ef` | `TabLeft` / Tab Left | `V-UI-01` |
| `S-KEY-052` | `f9e3f458-c0ad-4e2a-947d-4d52ec174609` | `CastSpell1` / Spell 1 | `V-SPELL-07` |

### Concrete interactable families

Rows came from the audited assembly candidate-method query. Generic renderers,
animation helpers, bars, images, and particles with no input handler were
discarded before normalization. `EX-INFRA` is a command router whose concrete
callback is represented by other rows. `EX-DEV` is a compiled developer-only
surface, not a normal player-reachable UI.

| Surface | Native types/members (assembly discovery source) | Disposition |
|---|---|---|
| `S-INT-001` | `UIViewList.HandleClick`, `UIViewToggle.OnClick`, `UIViewRadio`, `CoreViewManager` | `V-UI-01` |
| `S-INT-002` | `UIModalActivator`, `UIModal`, `UIModalSelectionManager`, `UIContextMenu`, `UIContentArea` | `V-UI-02`; context callbacks map to their concrete verb |
| `S-INT-003` | `UIBasicFilter`, `UIGlobalSearch`, `UIBasicDropdown`, generic paging/sort controls | `V-UI-03` |
| `S-INT-004` | `UITooltipContainer`, `UITooltipNode`, `UITooltipableList`, `UIAlertBadges` | `V-UI-04` |
| `S-INT-005` | `UIPinnedObjectList.OnPinnedClick/OnDrop/OnEndDrag` | `V-UI-05` |
| `S-INT-006` | `UIValueSelectButton`, `UINumberToggleButton`, global multi-buy input | `V-UI-06` |
| `S-INT-007` | `UIKeyBindList.OnKeyBindClick/Clear*` | `V-UI-07` |
| `S-INT-008` | `UISettingsModal.Select*` | `V-UI-08` |
| `S-INT-009` | `UIResourceDisplayList.ClickItem/OnDrop` | `V-UI-09` |
| `S-INT-010` | `UIMusicTrack.ToggleShuffleItem` | `V-UI-10` |
| `S-INT-011` | loadout editor serialized controls over `PlayerLoadout` metadata (`EG-01`) | `V-UI-11` |
| `S-INT-012` | `UIPassiveAbilityList.OnPassiveClick` -> `PassiveAbility.ToggleMuted` | `V-PASS-01` |
| `S-INT-013` | main-menu Continue serialized event -> `SaveStateManager.LoadSaveState` (`EG-01/04`) | `V-PROC-01` |
| `S-INT-014` | new-game serialized event -> `SaveStateManager.StartGame` (`EG-01/04`) | `V-PROC-02` |
| `S-INT-015` | save-slot selection/delete serialized controls -> selected-slot variable / `SaveStateManager.DeleteGameSave` (`EG-01/04`) | `V-PROC-03`,`V-PROC-09` |
| `S-INT-016` | manual-save serialized event -> `SaveStateManager.SaveGameState` (`EG-01`) | `V-PROC-04` |
| `S-INT-017` | `UIExportButton`, `UIExportSaveModal` | `V-PROC-05` |
| `S-INT-018` | `UIImportButton`, `UIImportSaveModal` | `V-PROC-06` |
| `S-INT-019` | `UIBackToMenuButton.BackToMenu` | `V-PROC-07` |
| `S-INT-020` | `UIQuitButton.QuitApplication` | `V-PROC-08` |
| `S-INT-021` | `UICostButton.OnClick` performs cost then invokes a configured callback | `EX-INFRA`; payment ordering is attributed to each concrete paid verb |
| `S-INT-022` | `UIStructureList.PurchaseStructure/ToggleDisableStructure`, `UIAttributeButton` | `V-ECO-01`,`V-ECO-03`; `UIAttributeButton` listener is `EG-01` |
| `S-INT-023` | `UIUpgradeButton.ClickUpgradeButton`, `UIUpgradeList.ClickUpgradeItem` | `V-ECO-02`; list click also changes UI selection under `V-UI-01` |
| `S-INT-024` | `UIResearchItem.DevelopResearch/PauseResearch/ResumeResearch/CancelDevelopment/AddBonusLevel`, `UIResearchList.SelectResearch` | `V-RES-01`,`V-RES-02`,`V-RES-03`,`V-RES-04`; list selection `V-UI-01` |
| `S-INT-025` | `UILevelableItem.PurchaseLevel/PurchaseFreeLevel`, `UILevelableList`/`UILevelablePage` | `V-LVL-01`,`V-LVL-02`; list selection `V-UI-01` |
| `S-INT-026` | `UIDiscoverableList`, `UIDiscoverablePage.HandleClick` | `V-DISC-01`,`V-ART-01`; list selection `V-UI-01` |
| `S-INT-027` | `UIDiscoveryTreePage.OnDiscoveryClick/OnDiscoveryItemClick/OnConfirmClick/OnRerollClick` | `V-DISC-02`,`V-DISC-03`,`V-DISC-04`,`V-DISC-05` |
| `S-INT-028` | `UIGlyphList.SelectGlyph/OnDrop` | `V-SPELL-01`,`V-SPELL-06`,`V-ALCH-01` depending owning list |
| `S-INT-029` | `UIDiscoverSpellButton.HandleClick`, `UICreateSpellButton.HandleClick` | `V-SPELL-02`,`V-SPELL-03` |
| `S-INT-030` | `UISpellList.OnSpellClick/OnSpellFire/OnDrop` | `V-SPELL-03`,`V-SPELL-04`,`V-SPELL-05`,`V-SPELL-07` depending `isForCasting` |
| `S-INT-031` | `UISpellInformation`, `UISpellRecipeButton.AttachSpell`, `UISpellRecipeItem`, `UISpellRecipeList` | `V-SPELL-02`,`V-SPELL-06`,`V-SPELL-08` |
| `S-INT-032` | `LevelAllSpellsButton` serialized event -> `SpellManager.TryLevelAllSpells` (`EG-01`) | `V-SPELL-09` |
| `S-INT-033` | `UICharacterList.ClickCharacter`, `UITargetingInterface.Randomize/Close`, `TargetingManager` | `V-TGT-01`,`V-TGT-02`,`V-TGT-03` |
| `S-INT-034` | `UIAlchemyDiscoverButton`, alchemy discovery glyph/resource controls | `V-ALCH-01`,`V-ALCH-02` |
| `S-INT-035` | `UIAlchemyRecipeList.ClickItem`, `UIAlchemyInstanceList.ClickItem/OnDrop`, `UIAlchemyRecipe` | `V-ALCH-03`,`V-ALCH-04`,`V-ALCH-05`,`V-ALCH-06` |
| `S-INT-036` | `UIAlchemyTypeList.ClickAlchemyType`, `UIAlchemyTypePlainList.ClickAlchemyType` | `V-UI-01` (selected-detail observable only in inspected IL; `EG-03`) |
| `S-INT-037` | `UIRuneStoneList.ClickRuneStone`, `UIDiscoverRitualButton` | `V-RIT-01`,`V-RIT-02` |
| `S-INT-038` | `UIRitualList.ClickRitual`, `UIRitual` activation/level/cancel controls | `V-RIT-03`,`V-RIT-04`,`V-RIT-05` |
| `S-INT-039` | `UIChallengeItem.FireActionButton/ToggleActivate/ToggleSelection` | `V-CHAL-01`,`V-CHAL-02`,`V-CHAL-03` |
| `S-INT-040` | `UITimeScreenManager.FetchNewChallenges`, `UIPersistentResetModal.FetchNewChallenges` | `V-CHAL-04` |
| `S-INT-041` | persistent-reset confirmation serialized event -> `PersistentResetManager.PersistentReset` (`EG-01`) | `V-PREST-01` |
| `S-INT-042` | `UICraftingRecipeList.ClickCraft`, `UICraftingPage.QueueCraft/ContextRecipeClick`, `UICraftingInstanceList.OnClickInstance` | `V-CRAFT-01`,`V-CRAFT-02`,`V-CRAFT-03` |
| `S-INT-043` | `UIBrewingStation.SetIngredient/SetOutput/ToggleBrewing`, `UITypeElementDropdown` | `V-CRAFT-04` |
| `S-INT-044` | `UIConsumableRefList.ClickConsumable/CancelConsumable/DiscardConsumable/OnDrop`, `UIConsumableRefItem` randomization | `V-CONS-01`,`V-CONS-02`,`V-CONS-03`,`V-CONS-04`,`V-CONS-05` |
| `S-INT-045` | `UIEquipmentList.SelectEquipment`, `UIEquipmentItem`, `EquipmentManager.ToggleItem/EquipItem/UnEquipItem` | `V-EQ-01`,`V-EQ-02`; serialized listener `EG-01` |
| `S-INT-046` | `UILoadoutList.OnLoadoutClick`, `UILoadoutItem`, `UILoadoutPage`, `UILoadoutSection` | `V-LOAD-01`,`V-LOAD-02`,`V-UI-11` |
| `S-INT-047` | `UISnapshotLoadout.SaveLoadout/LoadLoadout/ClearLoadout`, `UISnapshotLoadoutList` | `V-LOAD-03`,`V-LOAD-04`,`V-LOAD-05` |
| `S-INT-048` | `UIHarvestList.OnHarvestClick`, `UIHarvestActionList.OnActionClick` | `V-HARV-01`,`V-HARV-02`,`V-HARV-03`,`V-HARV-04` |
| `S-INT-049` | `UIHarvestTypeList.OnHarvestTypeClick` | `V-UI-01` (selected-detail observable only; `EG-03`) |
| `S-INT-050` | `UIPlotNodeList.OnNodeClick`, `UIPlotNodeActionList.OnActionClick` | `V-UI-01`,`V-PLOT-01`,`V-PLOT-02` |
| `S-INT-051` | `UIRasteredThought.Open/Close` | `V-UI-02` |
| `S-INT-052` | `UIStringList`, `UIToggleButton`, `UIDropdownList` | `EX-INFRA`; callback is attributed to settings/filter/recipe-specific row |
| `S-INT-053` | `UIDragDropItem`, `UIGenericPlainList.OnDrop/OnEndDrag` | `EX-INFRA`; concrete list reorder is mapped by specialized rows |
| `S-INT-054` | `UIDevConsole.SubmitConsoleCommand` | `EX-DEV` (developer-only arbitrary command surface; intentionally outside normal player playthrough and never exposed by suite) |
| `S-INT-055` | `UISpellTypeList.ClickSpellType`, `UISpellTypeListPlain.ClickSpellType` | `V-UI-01` (selected-detail observable only; `EG-03`) |

<!-- SURFACE-MAP-END -->

## Unresolved static evidence gaps

These gaps constrain implementation and the later disposable-save checklist:

1. **`EG-01`, serialized listeners.** Assembly IL cannot prove the prefab/event
   connection for spell discover/create, artifact/equipment selection, main-menu
   save controls, reset confirmation, loadout application, and level-all. Before
   binding an action, inspect the audited prefab/asset if obtainable; otherwise
   bind the named terminal manager/domain method and validate the UI equivalence
   on a disposable save.
2. **`EG-02`, cancellation disposition.** Research, crafting, consumable,
   ritual, challenge-abandon, and plot cancellation terminals are known, but the
   exact refund/lost-progress boundary is not promoted as fact. Tests must model
   the IL-visible calls; disposable validation must record exact deltas at
   multiple progress points.
3. **`EG-03`, selection persistence.** Several selected-detail, pin, order,
   muted, randomization, and preference variables may be serialized. They are
   classified by their gameplay effect, not claimed transient; reload checks
   decide persistence.
4. **`EG-04`, scene/platform availability.** Main-menu slot widgets, display
   modes, clipboard, and scene-transition confirmations require prefab/platform
   evidence not present in `Assembly-CSharp.dll`.
5. **`EG-05`, payment ordering.** Generic `UICostButton` pays before its
   callback, while `SpellManager.DiscoverSpell` mutates discovery before its own
   payment call. An action must follow the audited pipeline, make payment/commit
   last where a safe native preflight permits, and quarantine any observed
   post-commit divergence; it must not silently copy unsafe UI ordering.
6. **`EG-06`, automatic attack exclusion.** `AutoAttackView` exists as authored
   presentation state, but the UI candidate sweep found no command-bearing
   `UIAutoAttack*` type or terminal toggle. Prefab inspection can falsify
   `EX-AUTO`; until then no automation contract is invented.
7. **`EG-07`, interface breadth.** The verb is closed over `ILevelable` and
   `IDiscoverable`, but action binding must enumerate `InterfaceImplementation`
   rows in the installed assembly and either bind each expected native type or
   return `ContractUnavailable`. The concrete types named above are evidence,
   not permission to fall back on runtime reflection.

## Totally ordered Milestone 3 build plan

The first stop line is after `B-022`: it yields the gameplay mutation surface
needed for an end-to-end progression/reset loop. Later entries are UI
convenience, preferences, and hazardous process/save operations. Each row is a
separate bisectable action or UI-command increment; grouping is used only for a
shared manager/domain lifecycle whose members must be bound and tested as a set.
Every gameplay mutation uses a GameAction. UI-only commands retain honest
mutation-audit classification without pretending to be read-only.

<!-- M3-PLAN-START -->
| Order | Exact partial/none verb IDs | Increment/family and ordering reason |
|---|---|---|
| `B-001` | `V-DISC-02`, `V-DISC-03`, `V-DISC-04`, `V-DISC-05` | Discovery-tree offer lifecycle (`DiscoveryTreeSO`): initiate, select, confirm, reroll. First because it directly unlocks the named buy/pick/reroll playthrough and supplies stable offered UUIDs to MCP. |
| `B-002` | `V-SPELL-01`, `V-SPELL-02`, `V-SPELL-03` | **Complete:** `game_spell_workbench` and `SpellWorkbenchGameAction` select an authored base recipe, discover it, and create/equip a stable spell instance; the shared spell-recipe row publishes every decision and post-state fact. This is the shortest route from an offer/unlock to an MCP-castable spell. |
| `B-003` | `V-SPELL-06` | **Complete:** `game_spell_composition` and `SpellCompositionGameAction` set the global spell output selector or replace one exact runtime spell's augment stacks; newer-world results include named options, holdings, derived levels, cast/drain costs, and affordability. |
| `B-004` | `V-SPELL-04`, `V-SPELL-05` | **Complete:** `game_spell_loadout` and `SpellLoadoutGameAction` remove one exact removable runtime spell or reorder it through the native list swap/notification path; `spell-slots` and every committed response carry the complete named loadout and next decisions. |
| `B-005` | `V-TGT-01`, `V-TGT-02`, `V-TGT-03` | Generic targeting lifecycle: specific submit, randomize, cancel. Promotes targeting beyond the existing cast-specific shortcut. |
| `B-006` | `V-CONS-01`, `V-CONS-02`, `V-CONS-03`, `V-CONS-04`, `V-CONS-05` | `ConsumableSO`/consumable-reference lifecycle. Reuse the audited Auto Items action, add MCP, then bind cancellation/discard/randomization/order without a second use implementation. |
| `B-007` | `V-CRAFT-01` | **Complete:** `game_craft` and the shared `AutoScribeOneShotCraftGameAction` execute every concrete recipe through its exact direct/stack/new/instant native route; crafting rows and committed results include named exact next costs, holdings, affordability, queue state, and blockers. This unlocks Craft Sigils without duplicating Scribe semantics. |
| `B-008` | `V-DISC-01` | **Complete:** `game_discover` and `GenericDiscoveryGameAction` discover one exact alchemy recipe, equipment asset, glyph, ritual, or time rune through the native payment-then-callback route. All six discoverable category rows publish named costs, holdings, affordability, and native verdicts; spell recipes remain owned by the already-complete workbench action. |
| `B-009` | `V-ART-01`, `V-EQ-01`, `V-EQ-02` | **Complete:** artifact creation remains the single shared `game_discover` capability, while equipment rows now expose exact native loadout pre-decisions and `game_equipment` applies equip/increase or unequip/decrease through `EquipmentManager` with live main-thread revalidation and complete newer named post-state. |
| `B-010` | `V-CHAL-01`, `V-CHAL-02`, `V-CHAL-03`, `V-CHAL-04` | Challenge selection/queue/abandon/offer family. Complete it before prestige so reset preflight can prove the exact selected/queued challenges. |
| `B-011` | `V-PREST-01` | Persistent-reset GameAction. High priority for the full loop, but only after challenge receipts exist; maximum quarantine and disposable-save ceremony. |
| `B-012` | `V-RES-01`, `V-RES-02`, `V-RES-03`, `V-RES-04` | `ResearchSO` development lifecycle including pause/resume/cancel/bonus, bound together because queue identity and investment receipts must agree. |
| `B-013` | `V-ALCH-01`, `V-ALCH-02`, `V-ALCH-03`, `V-ALCH-04`, `V-ALCH-05`, `V-ALCH-06` | `AlchemyManager`/recipe/instance family. Preserve the existing concept action as the one definition for concept-classified engage/disengage while adding ordinary recipes, discovery, order, and max level. |
| `B-014` | `V-RIT-01`, `V-RIT-02`, `V-RIT-03`, `V-RIT-04`, `V-RIT-05` | Ritual selection/discovery/active lifecycle, after generic target and exact-cost patterns exist. |
| `B-015` | `V-LVL-01`, `V-LVL-02` | Complete generic `ILevelable`/free-level binding matrix, fail closed per installed concrete type. |
| `B-016` | `V-CRAFT-02`, `V-CRAFT-03` | Crafting-instance automation/cancellation lifecycle, extending `B-007` receipts to queued/continuous work. |
| `B-017` | `V-CRAFT-04` | Crafting-station configuration/activation; separate because it mutates `CraftingStructureSO`, not recipe instances. |
| `B-018` | `V-LOAD-01`, `V-LOAD-02`, `V-LOAD-03`, `V-LOAD-04`, `V-LOAD-05`, `V-UI-11` | Loadout/snapshot save/load/clear family and its metadata. Built after component actions so apply can revalidate every referenced spell/equipment/alchemy entry through shared capabilities. |
| `B-019` | `V-HARV-01`, `V-HARV-02`, `V-HARV-03`, `V-HARV-04` | Harvest element/action instance lifecycle. |
| `B-020` | `V-PLOT-01`, `V-PLOT-02` | Generalize the existing audited `game_harvest` plot/action pair to a complete installed binding catalog without changing its one action definition. |
| `B-021` | `V-ECO-03` | Structure enable/disable lifecycle, after production/allocation receipt conventions are established. |
| `B-022` | `V-SPELL-09` | Native level-all batch action with ordered per-recipe receipts and honest multi-commit evidence; single-level coverage already exists, so the batch convenience follows core verbs. |
| `B-023` | `V-UI-06` | Explicit selector/multi-buy UI state command; action amounts remain explicit and never depend on hidden UI selector state. |
| `B-024` | `V-UI-04` | Tooltip inspect/follow UI-state command after M2 supplies the complete typed read model. |
| `B-025` | `V-UI-02` | Modal/context/sidebar UI-state command; context gameplay callbacks continue to call their concrete GameAction. |
| `B-026` | `V-UI-03` | UI filter/search/sort/page command family. |
| `B-027` | `V-UI-05` | Pinned-object list state family. |
| `B-028` | `V-UI-09` | Resource display selection/order family. |
| `B-029` | `V-PASS-01` | Passive mute preference. It changes a saved/native flag but not gameplay effects in inspected IL. |
| `B-030` | `V-UI-07` | Key-binding editor family, after all action invocations are independent of physical keys. |
| `B-031` | `V-UI-08` | Game/graphics settings family with platform-specific refusal evidence. |
| `B-032` | `V-UI-10` | Music shuffle preference. |
| `B-033` | `V-PROC-02` | New-game process action, only after full gameplay actions exist; disposable empty slot only. |
| `B-034` | `V-PROC-03`, `V-PROC-09` | Save-slot select/delete family. Delete remains confirmation-gated and below the destructive stop line. |
| `B-035` | `V-PROC-04`, `V-PROC-05`, `V-PROC-06`, `V-PROC-07` | `SaveStateManager` manual-save/export/import/menu-transition family. Import and transition require complete save/partial-write receipts; no live test in this mission. |
| `B-036` | `V-PROC-08` | Quit remains last and may be declared permanently unsuitable for inline MCP because a post-termination receipt cannot be returned. |
<!-- M3-PLAN-END -->

The full-coverage verbs deliberately absent from this build list are exactly
`V-UI-01`, `V-PROC-01`, `V-ECO-01`, `V-ECO-02`, `V-SPELL-07`, and
`V-SPELL-08`. Their existing shared MCP actions remain the one definitions.

### Mechanical completeness checks

The milestone boundary runs three comparisons over this committed file:

1. Count exact rows: 97 `S-VIEW`, 52 `S-KEY`, 55 `S-INT`, and 86 catalog
   verbs; every surface row's disposition column is nonempty.
2. Extract the 86 catalog IDs and all verb IDs between `SURFACE-MAP-START/END`;
   set subtraction in both directions must leave no catalog verb without a
   surface/global-input mapping. Extra surface occurrences are expected;
   unknown IDs are not.
3. Extract catalog rows whose coverage cell is `**partial**` or `**none**` and
   compare them to verb IDs between `M3-PLAN-START/END`. Both set subtractions
   must be empty, every plan ID count must be exactly one, and the six full IDs
   above must occur zero times in the plan.

This proves closure over the finite authored view/key universes and the
normalized concrete interactable families. It does not erase `EG-01` through
`EG-07`; those are explicit falsifiable boundaries for action dossiers and
disposable validation.
