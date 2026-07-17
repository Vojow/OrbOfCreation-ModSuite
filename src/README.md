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

## Orb Mentor

`OrbMentor` shares mastery in three independent domains: spells, artifacts, and alchemy. Spells use the native `SpellRecipeSO.GainMasteryExp(BigDouble)` boundary; the optional artifact and alchemy domains use their separately audited native hooks and grant paths. Each domain fails closed independently on contract or lifecycle errors.

## Test build

Run the game-independent suite with:

```bash
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

The test property replaces external game references with `tests/OrbModding.GameStubs`. It is not used for production builds or runtime validation.
