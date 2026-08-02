# Architecture

[Back to index](README.md)

## Lifecycle

`GameManager` is the main coordinator. It holds the major asset lists, the managers, the
save/settings managers, active effects, and the iteration caches.

Its phase enum is:

```text
Empty → Validate → Bind → Initialize → Start → Increment / SlowIncrement
```

`GameManager.GameElementIterator` discovers objects implementing the lifecycle interfaces and
calls `Validate()`, `Bind()`, `Initialize()`, `Start()`, `Increment(float)`, and
`SlowIncrement(float)` on them. `GameManager.FixedUpdate()` passes `Time.fixedDeltaTime` into the
increment loop and then updates the managers.

Gameplay systems are data-driven ScriptableObjects advanced by these centralized loops, not
independent Unity components. That is why the registry, not the scene graph, is the integration
surface — see [identity-and-registries.md](identity-and-registries.md).

## Managers

Managers inherit `AbstractManager` or `MonoBehaviour`. `AbstractManager` exposes `ManagerStart()`
and `ManagerUpdate(float deltaTime)`.

| Manager | Responsibility |
|---|---|
| `GameManager` | initialization, iteration, effects, save boundaries |
| `ResourceManager` | resource initialization and checking, rarity, global progress |
| `SaveStateManager` | collection, encoding, loading, backup and slot operations |
| `AlchemyManager` | selected glyphs and resources, recipes, active alchemy |
| `CraftingManager` | crafting pages |
| `AutoBuyManager` | the game's own automated purchase queue |
| `InputManager` | key bindings, modals, developer-console activation |
| `ActionManager` | the shared action queue and its remaining room |

`Player.ManagerStart()` builds its observers before applying persistent effects;
`ManagerUpdate()` reapplies them when an observer updates. A persistent effect that appears not to
have landed is usually being read before that second pass.

## The four boundaries that invalidate a cached reference

These are the points at which a native object reference you are holding may stop being the object
the game is using. A same-UUID replacement is a new object, not the same one.

| Boundary | What happened |
|---|---|
| `SaveStateManager.ImplementLoadedJson()` | a save is being loaded, then has loaded |
| `PersistentResetManager.PersistentResetLogic()` | NG+ started |
| `GameManager.ResetGameState()` | a reset started |
| `GameManager.InitGame()` | the registry has been rebuilt and the runtime is ready |

Anything cached from before one of these — resolved objects, cost values, queue capacity,
prerequisite cache state — has to be dropped and re-resolved from the stable UUID afterwards. The
earliest Unity point at which every registry is complete is a runtime fact, not a compile-time
one, which is why re-resolving at each boundary beats assuming one permanent startup snapshot.

Queue capacity in particular changes without any of these firing: it grows through ordinary
progression, so it must be re-read rather than latched.

## Save boundaries

`GameManager` exposes `TriggerBeforeSave`, `BeforeSave`, `AfterSave`, and `TriggerAfterSave`.
These are the integration points for state a mod owns that must be synchronized with a normal
save. See [save-system.md](save-system.md) for the pipeline they bracket.
