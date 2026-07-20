# Shared lifecycle readiness and generation tracking

> **Lifecycle: Implemented for the next beta; interactive validation pending.** Automata, Mentor, and Mod Config consume the shared Common lifecycle monitor. This is not part of the frozen 0.3.2 release candidate.

[Back to plans](README.md) · [Runtime validation](../testing/runtime-validation.md)

## Contract

`GameLifecycleMonitor` owns one process-wide, main-thread lifecycle snapshot:

- `NoGame`
- `Initializing`
- `Playing`
- `Resetting`
- `SceneExit`

Every accepted scene, save-load, runtime-ready, reset/NG+, or registry-rebuild observation advances a monotonically increasing generation. Equivalent observations from different suite plugins in the same Unity frame are coalesced. A plugin that initializes later and observes the same still-live scene does not manufacture a new generation.

Consumers capture a `GameLifecycleLease` with prepared work or delayed events and reject it when its generation no longer matches. The gameplay plugins also cancel their existing prepared work synchronously when the shared transition event advances. Progression unlocks remain feature/domain state and never make a globally ready lifecycle unavailable by themselves.

## Safety and boundedness

- The first observation captures the owning thread; later off-thread transitions fail closed.
- Invalid frames are rejected.
- Structured diagnostics retain only the latest 32 accepted transitions.
- Runtime identity is diagnostic/coalescing evidence only and is never retained as gameplay ownership across generations.
- Scene recreation is represented by exit followed by entry even when both scenes have the same name.

## Verification

Portable tests cover late leases, repeated initialization, interleaved cross-plugin callbacks, save switching, reset, NG+, rapid scene changes, subscriber isolation, off-thread rejection, and bounded diagnostics. The audited runtime bridge enters resetting before `SaveStateManager.ImplementLoadedJson` and `PersistentResetManager.PersistentResetLogic`, then declares global readiness only after `GameManager.InitGame` has rebuilt registries, started all managers and elements, and marked the game started. Interactive validation must still confirm those hooks retain the expected order in an actual save switch and persistent reset.
