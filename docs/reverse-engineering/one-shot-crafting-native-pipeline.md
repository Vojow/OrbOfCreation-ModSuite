# One-shot crafting native pipeline

## Scope and verdict

B-007 completes `V-CRAFT-01`: execute one visible `CraftingRecipeSO` through the same direct,
stacking, queued, or instant route the player's manual crafting UI selects. The existing
`AutoScribeOneShotCraftGameAction` is the single mutation boundary for both Auto Scribe and MCP;
the player overload widens that boundary to every concrete recipe without changing Auto Scribe's
planner or accounting semantics.

Installed metadata and IL prove the pinned game's route, ordering, and complete member set.
Portable tests prove main-thread/lifecycle admission, exact target and queue outcomes, quarantine,
world projection, MCP schema, names, and post-state. Live Unity and save behavior remain unpromoted
until the supervised checklist below passes.

## Audited native routes

`UICraftingRecipeList.ClickCraft` (`0x060022E1`) invokes the page callback when one is installed and
otherwise invokes `CraftingRecipeSO.Execute` (`0x06000A32`). The latter is the synchronous direct
composite. Its exact IL sequence is:

1. `CraftingRecipeSO.CanBuy` (`0x06000A2A`);
2. `CraftingRecipeSO.GetPurchaseQuantity` (`0x06000A30`);
3. `CraftingRecipeSO.recipeCost` (`0x040005F1`);
4. `ResourceCostList.Multiply` (`0x06001E3B`);
5. `ResourceCostList.PerformCost` (`0x06001E19`);
6. `PassiveObservable.Channel.Update` (`0x06003A9F`).

`UICraftingPage.UIStart` installs `ContextRecipeClick` and `ContextRecipeInteraction` and owns the
authored recipe-list, queue, mode, and main-type relations. `ContextRecipeClick` reads the queue's
current recipe quantity, obtains `GetPurchaseQuantity(previous)`, then calls
`UICraftingPage.QueueCraft` (`0x060022F0`). `QueueCraft` orders its native work as:

1. read the exact recipe's current queue quantity;
2. call `CraftingRecipeSO.PurchaseQuantity(purchase, previous)`;
3. read native craft mode;
4. in stack mode, call `CraftingInstance.AddQuantity` (`0x06000DE2`) on the exact existing recipe
   instance; otherwise construct `CraftingInstance(recipe, purchase)`;
5. call `CheckInstantCraft`; instant work calls `InstantCraft`, while timed work calls `Initiate`
   and adds the instance to the authored queue.

`HasSpaceForCraft` permits a full stack-mode queue when the exact recipe already has an instance;
otherwise it requires native list room. The action reproduces that branch, not a blanket capacity
check.

## Shared GameAction and boundary order

The lifecycle binding set resolves every member before submission. A missing new or reused member
makes the entire player surface `contract_unavailable`; execution never searches by name or binds
reflection. Each call then checks, in order:

1. captured Unity main thread;
2. family quarantine and lifecycle epoch;
3. exact UUID plus expected `CraftingRecipeSO` type through `TypedRegistryResolver`;
4. live `IsVisible`;
5. exactly zero or one authored `UICraftingPage` containing the same recipe object;
6. direct time or page main-type/mode relation;
7. native purchase quantity, `CanBuy`/`CanBuyAt`, exact `GetTotalCost(...).HasEnough`, and queue
   room;
8. shared one-shot-crafting mutation permit, last;
9. the audited native direct or page composite.

A timed recipe without a stable page relation refuses rather than guessing a queue. A recipe on
multiple pages refuses as ambiguous. Direct recipes invoke the game's complete `Execute` composite.
Page recipes preserve payment-before-construction because that is the game-owned `QueueCraft`
transaction; failures after payment retain the observed stage and state rather than pretending a
rollback occurred.

Queue success gates only identity and requested outcome: the exact recipe's quantity increases by
the native purchase amount and the exact existing/new instance is present. Payment deltas,
resource ledgers, maximum-level changes, observables, sound, and downstream effects are not gates.
Direct and instant routes commit only after their exact native terminal method returns; their
generic output graphs have no stable cross-recipe receipt to reconstruct. A native throw after a
requested queued outcome is already observable still commits. Wrong or absent target outcome, or
an exception after mutation without that outcome, quarantines the shared one-shot family until
lifecycle replacement. Preflight refusal never quarantines.

Auto Scribe continues to use its existing role planner, strongest-affordable-level search, target
proof, and automation receipt semantics. MCP supplies an exact player recipe UUID to the player
overload on the same object. This is planner symmetry without a second mutation implementation.

## Pre-decision world and MCP surface

The crafting-decision reader discovers the authored page relations twice at the first capture of a
lifecycle and publishes them only when both scans contain the same object references. Later worlds
reuse that immutable routing set and re-evaluate live recipe, queue, cost, holding, and native
admission facts on Unity's main thread.

For direct recipes, next cost is the exact `recipeCost.Multiply(purchaseAmount)` lineage used by
`Execute`. For page recipes, it is the exact `GetTotalCost(previous, purchaseAmount)` lineage used
by `QueueCraft`. Every `crafting-recipes` row therefore adds:

- `execution`: `direct`, `queue_stack`, `queue_new`, or unknown when a timed page is absent;
- native `purchaseAmount` and current `queuedAmount` when applicable;
- named queue identity, used slots, and maximum;
- named exact `nextCosts`, each with `cost`, canonical spendable `amount`, and affordability;
- `canStart` plus decision-local blockers.

All magnitudes use the one scientific-string formatter. Every recipe, queue, resource, type,
input, output, and consumable reference is named by the shared live identity catalog. No mutation
is needed to learn affordability or queue room.

`game_craft(recipeUuid=...)` accepts one published recipe UUID and optional exact native-type
assertion. It has no generation, amount, receipt, or payment argument. Success waits for a newer
published world and returns that complete named recipe decision row inline, without world
generation or a required read-back. Refusal contains the named reason; a fault after native work
retains decomposed before/after execution and queue evidence.

## Native contract delta

B-007 adds 26 manifest rows: 14 action bindings and 12 capture bindings. The new bindings cover
Unity's page scan, all four authored page relations, direct `CanBuy`/purchase/execute/time,
queue quantity and room, exact instance identity/quantity/addition, queue maximum, direct cost
`Multiply`, and page exact-cost/main-type evaluation. The action completeness set also names every
reused visibility, `CanBuyAt`, purchase, construction, initiation, instant, list, cost, integer,
and stable-identity contract. The installed manifest loop proves member-exact resolution; focused
installed tests additionally pin tokens and both direct/page IL routes.

## Supervised disposable-save checklist

1. List several direct and timed recipes before opening a crafting screen; compare visibility,
   execution, purchase amount, costs, holdings, affordability, and blockers with the UI.
2. Open each manual crafting page and verify its recipes move to the exact authored queue route,
   named queue, occupancy, and mode without duplicate or stale relations.
3. Craft a direct recipe and compare the returned named next decision and visible output; confirm no
   receipt, payment stanza, world generation, or follow-up read is needed.
4. Craft a stack-mode recipe already in a full queue and confirm only that exact stack increases.
5. Craft a new timed instance and confirm recipe identity, amount, initiation, queue placement, and
   returned occupancy.
6. Craft an instant page recipe and confirm its output appears while no queued instance remains.
7. Exercise a quantity-as-level recipe and compare native purchase amount and scaled direct/page
   cost with the UI.
8. Exercise Craft Sigils and each of the six Scribe recipes; compare Auto Scribe and MCP use of the
   same queue and confirm automation policy is unchanged.
9. Attempt hidden, unaffordable, invalid-quantity, full-new-instance, and timed-without-page cases;
   each must refuse before payment and without quarantine.
10. If a recipe can appear on two loaded page objects, confirm the action refuses the ambiguous
    relation instead of choosing one.
11. Compare exact named post-state after each action to visible queue and holdings, including a
    cost whose floating-point balance delta is noisy or unrepresentable; accounting must never
    fault a correct outcome.
12. Cross a scene/save lifecycle and confirm page routing, native bindings, references, and any
    crafting quarantine are discarded before the next call.
