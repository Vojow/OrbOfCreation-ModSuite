# Orb Chronomancer

Orb Chronomancer is a BepInEx 5 plugin for safe Orb Of Creation speed presets.

## Build

Set `OOC_GAME_DIR` to the Orb Of Creation install root, then build only this lane:

```bash
dotnet build src/OrbChronomancer/OrbChronomancer.csproj
```

Expected install layout:

```text
$OOC_GAME_DIR/
  BepInEx/core/BepInEx.dll
  BepInEx/core/0Harmony.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll
  Orb Of Creation_Data/Managed/UnityEngine.dll
  Orb Of Creation_Data/Managed/UnityEngine.CoreModule.dll
  Orb Of Creation_Data/Managed/UnityEngine.IMGUIModule.dll
```

## Configuration

The plugin writes BepInEx config under `BepInEx/config/dev.vojow.orbofcreation.chronomancer.cfg`.

Default controls:

| Action | Default |
|---|---|
| Increase speed | `LeftAlt + Equals` |
| Decrease speed | `LeftAlt + Minus` |
| Reset to 1x | `LeftAlt + Alpha0` |

Default presets are `1x`, `2x`, `4x`, and `8x`, but the runtime maximum defaults to `4x`. The `8x` preset is ignored until both of these are true:

- `Timing.MaximumMultiplier` is greater than `4`.
- `Timing.AllowExperimentalEightX` is `true`.

The default fixed-update policy is `ScaleWithMultiplier`, which sets `Time.fixedDeltaTime` to the captured baseline multiplied by the active speed. This is intended to preserve fixed-update calls per real second while making each fixed update represent more simulated time.

## Safety behavior

Chronomancer captures `Time.timeScale` and `Time.fixedDeltaTime` at startup and restores both on unload, application quit, unsupported scene transitions, and errors while applying a multiplier.

The plugin only allows acceleration in the configured gameplay scene, currently `Main`. It attempts to patch these `SaveStateManager` methods when present and falls back to `1x` as they run:

- `CollectJsonData`
- `ImplementLoadedJson`
- `WriteFileAndBackupAsync`

If a method is missing in a future game build, the plugin logs a warning and continues without that hook.

## Timing probes required before enabling 8x by default

Record probe results for clean BepInEx, Automata, and combined project-mod installs using a backed-up save. Each run should include `1x`, `2x`, `4x`, and opt-in `8x` samples.

Required evidence:

- Passive resources, drains, crafting, research, alchemy, combat, animations, popups, and autosave all scale as expected.
- Save/load and title return from every multiplier leave `Time.timeScale` and `Time.fixedDeltaTime` at the captured baseline.
- Fixed-update calls per real second and CPU cost remain acceptable with `ScaleWithMultiplier`.
- `PreserveOriginal` is compared as a probe-only policy so the CPU/correctness tradeoff is documented.
- No NaN resource values, stuck loading state, duplicate save records, or input loss appear during extended `4x` and `8x` sessions.
- Automata purchase/evaluation rates do not grow beyond the intended game-time acceleration.
