# Orb Chronomancer plan

> **Lifecycle: Experimental.** An implementation exists but is not included in the Automata release archive.

[Back to roadmap](roadmap.md)

## Goal

Coordinated implementation iterations, timing-strategy experiments, and the pre-worktree gate are defined in [Three-mod iteration plan](three-mod-iteration.md).

Provide safe simulation-speed control without making menus, input, saving, or animation systems unusable.

## User experience

Default controls, configurable through BepInEx:

| Action | Proposed default |
|---|---|
| Increase speed | `Alt+Equals` |
| Decrease speed | `Alt+Minus` |
| Reset to 1× | `Alt+0` |
| Pause/resume | Unbound by default |

MVP presets: `1×`, `2×`, `4×`, and `8×`. `0.5×`, pause, and `16×` are post-MVP options.

The active multiplier should appear briefly after a change. A persistent corner indicator is optional.

## Candidate implementations

### A. Unity-wide time scale

Set `Time.timeScale` and adjust `Time.fixedDeltaTime` deliberately.

Advantages:

- Minimal patching.
- Naturally affects most scaled timers and coroutines.

Risks:

- May accelerate animations and physics unnecessarily.
- Does not affect code using unscaled or wall-clock time.
- Very high values can cause fixed-update bursts.

### B. Simulation delta multiplier

Patch the delta passed through `GameManager` manager and game-element increment loops.

Advantages:

- UI and input remain at normal speed.
- More precise control of progression simulation.

Risks:

- Multiple increment paths may require coordination.
- Some subsystems may use independent timers.
- Multiplying at more than one layer would compound speed accidentally.

### C. Hybrid

Use modest `Time.timeScale` values and a separate simulation multiplier at high presets. This is a fallback, not the preferred starting point, because it is harder to reason about.

## Timing probe requirements

Verified timing surfaces explain why this probe is a worktree blocker:

- `GameManager.FixedUpdate()` passes `Time.fixedDeltaTime` to both gameplay increments and manager updates.
- Slow increments use scaled `InvokeRepeating()` cadence but pass a hard-coded `0.2f` delta.
- `SaveStateManager.Update()` advances played time and autosave using scaled `Time.deltaTime`.
- A progression-only multiplier at only `GameManager.FixedUpdate()` would therefore miss slow increments, autosave, and other `MonoBehaviour.Update()` timers.

The probe must compare whole-game Unity scaling with a CPU-limited fixed-step variant before the MVP strategy is selected.

The probe must log samples, not every frame. Capture:

```text
scene
game phase
Time.timeScale
Time.deltaTime
Time.unscaledDeltaTime
Time.fixedDeltaTime
fixed updates per real second
manager updates per real second
increment calls per real second
representative resource gain over real and simulated time
```

Test representative systems:

- Passive resource generation
- Resource loss/drain
- Crafting and research timers
- Alchemy
- Combat
- Animations and popups
- Autosave timer
- Scene transitions
- Automata intervals and purchase rate

## MVP requirements

- BepInEx configuration for enabled state, presets, keybinds, maximum multiplier, and indicator.
- Automatic return to 1× outside the `Main` scene.
- Automatic return to 1× during save/load transitions if testing shows risk.
- Clamp invalid or extreme configuration values.
- Restore original Unity timing values when the plugin unloads.
- Log every multiplier change and its source.
- No permanent patches to game files.

## MVP implementation status

The current `src/OrbChronomancer` skeleton implements the Unity-wide strategy:

- Captures `Time.timeScale` and `Time.fixedDeltaTime` on plugin startup.
- Applies configurable presets through BepInEx keybinds.
- Defaults to a `4×` maximum; the `8×` preset is present but ignored until `AllowExperimentalEightX=true`.
- Uses `ScaleWithMultiplier` as the default fixed-step policy to limit fixed-update CPU growth.
- Restores captured timing values on unload, application quit, unsupported scene transitions, save/load safety hooks, and apply errors.
- Attempts low-risk Harmony hooks for `SaveStateManager.CollectJsonData`, `SaveStateManager.ImplementLoadedJson`, and `SaveStateManager.WriteFileAndBackupAsync`.
- Logs multiplier changes and periodic diagnostic timing samples while accelerated.

Before `8×` can be enabled by default, runtime evidence must show that save/load, autosave, scene transitions, fixed-update rate, input responsiveness, Automata coexistence, and representative progression systems remain correct at `8×` with backed-up saves. Record those results under `tests/` with the game assembly hashes and fixed-step policy used.

## Safety policy

- Default maximum is `4×` until runtime evidence justifies enabling `8×` by default.
- `8×` remains available behind explicit configuration for backed-up-save testing.
- If added after MVP, 16× requires explicit advanced configuration.
- Never increase automation work merely because simulation speed increased.
- Avoid running thousands of fixed updates to “catch up.”
- Prefer dropping visual effects over blocking the main thread.

## Definition of done for v0.1

- Speed presets behave consistently for core progression.
- UI and key input remain responsive.
- Saving and reloading at each supported speed produces a valid save.
- Returning to the title screen restores 1×.
- Clean game and Automata compatibility tests pass.
- Release contains only the plugin DLL, README, changelog, and license.
