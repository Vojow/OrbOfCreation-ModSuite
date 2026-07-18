# Source layout

Build plugin projects with `OOC_GAME_DIR` set to the Orb Of Creation install root.

Expected install layout:

```text
$OOC_GAME_DIR/
  BepInEx/core/BepInEx.dll
  BepInEx/core/0Harmony.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll
  Orb Of Creation_Data/Managed/UnityEngine.CoreModule.dll
```

Each plugin is a separate BepInEx 5 DLL. `OrbModding.Common` stays intentionally small and must not grow into a shared gameplay framework until duplicated implementation pressure proves it is worth extracting.

Tracked supported projects on this branch are `OrbAutomata`, `OrbMentor`, `OrbModConfig`, and `OrbModding.Common`. Orb Insights and Orb Toolbox remain design-only. Orb Chronomancer and Orb Achievement Resonance source lives only on `codex/experimental-chronomancer-resonance` and must not be inferred from old build-output directories.

## Shared gameplay controls

Queue-adjacent suite buttons register with `OrbModding.Common.StatusControlGroup`. Add a unique named assignment to `StatusControlOrder` and call `RegisterControl` before `Reflow`; lower values are closer to the native Auto Buy toggle. Current assignments are Auto Buy `100`, Auto Cast `200`, Auto Concept `300`, and Mentor `400`, leaving space for insertion. Do not add object names or a fixed button count to the layout helper. `StatusControlGroupTests` covers priority uniqueness, reordered creation, ignored non-controls, invalid indexes, the exact native anchor, and strips longer than the current button set.

## Shared alchemy gameplay-domain classifier

`OrbModding.Common.AlchemyGameplayDomainClassifier` distinguishes ordinary alchemy from Scholar Concepts without reading internal or display names. Initialize it once per lifecycle after `IdScriptableObject.RuntimeLookup` contains the exact `ConceptRecipes` UUID/type asset. Recipe classification then requires exact `AlchemyRecipeSO`, stable recipe UUID, concept-registry membership or verified exclusion, exact `AlchemyTypeSO`, and one of the audited ordinary or Scholar type UUIDs. Missing or contradictory evidence returns `Unknown` with a shared evidence level, named sources, detailed flags, and a diagnostic reason. Active consumers require `IsMutationGrade` rather than trusting the domain label alone.

The classifier caches the verified registry snapshot and per-recipe results. Call `InvalidateLifecycle()` on scene, save-load, reset, and NG+ changes; do not initialize or reflect inside a per-frame or native-XP hook. See [Alchemy gameplay-domain classification](../docs/reverse-engineering/alchemy-domain-classification.md) for the evidence matrix and adoption contract.

## Shared typed registry resolver

`OrbModding.Common.TypedRegistryResolver` is the suite boundary for `IdScriptableObject.RuntimeLookup`. Resolve by non-empty UUID plus exact expected native type, retain the returned lifecycle generation with cached native references, and use `IsRetryable` rather than parsing reason strings. `ResolveMember` distinguishes verified inclusion from verified exclusion; malformed list evidence never proves absence. Names are diagnostics only. See the [typed resolver plan](../docs/plans/typed-registry-resolver.md).

## Orb Mentor

`OrbMentor` shares mastery in three independent domains: spells, artifacts, and alchemy. Spells use the native `SpellRecipeSO.GainMasteryExp(BigDouble)` boundary; the optional artifact and alchemy domains use their separately audited native hooks and grant paths. Each domain fails closed independently on contract or lifecycle errors.

## Test build

Run the game-independent suite with:

```bash
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

The test property replaces external game references with `tests/OrbModding.GameStubs`. It is not used for production builds or runtime validation.
