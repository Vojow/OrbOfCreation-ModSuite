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

## Orb Mentor

`OrbMentor` is the spells-only mastery-sharing plugin. Its pure engine is covered by the portable suite; production builds hook the native `SpellRecipeSO.GainMasteryExp(BigDouble)` boundary and fail closed on contract or lifecycle errors.

## Achievement Resonance

Native mutation is guarded by `General.ApplyNativeEffectBlocks=false`. The exact target, script-list, `BigDouble`, stable-GUID, and capped-refresh contracts are now verified statically and by tests. Use the default mode for the read-only load probe, then enable only the global-speed slice for isolated gameplay validation.

## Test build

Run the game-independent suite with:

```bash
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

The test property replaces external game references with `tests/OrbModding.GameStubs`. It is not used for production builds or runtime validation.
