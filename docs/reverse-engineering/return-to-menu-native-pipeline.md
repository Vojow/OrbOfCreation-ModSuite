# Back to Menu native pipeline

[Back to reverse engineering](README.md)

The playable-scene Back to Menu button is a save-and-scene lifecycle boundary. The MCP invokes the
button callback itself; calling `SaveStateManager.BackToMainMenu` directly would skip the authored
manual-save event.

## Audited v1.0.5 route

```text
UIBackToMenuButton.BackToMenu()                       0x0600280D
  manualSave                                          0x0400148C
  → VoidEventChannel.Raise()                          0x06000BB2
  → SaveStateManager.instance                         0x0400047D
  → SaveStateManager.BackToMainMenu()                 0x060006FF
      ldstr "Start"
      → AnimateChangeScene(string)                    0x06000700
          → UIScreenFlash.instance                    0x0400130A
          → FadeIn(float, float)                      0x060026A3
              writes UIScreenFlash.isActive = true    0x04001308
          → SetLoadingAnim(bool)                      0x060026A0
          → OnAnimComplete(Action)                    0x060026A5
```

The callback raises `manualSave` before it asks the manager to return. `BackToMainMenu` is exactly
the 12-byte sequence `ldarg.0; ldstr "Start"; call AnimateChangeScene; ret`. The fade registers the
later scene-load callback, but writes `isActive` synchronously before `AnimateAlpha`. That flag is
the one outcome sentinel available while an MCP response can still be delivered.

## Action boundary

`ReturnToMenuGameAction` binds `UIBackToMenuButton.BackToMenu`, `UIScreenFlash.instance`, and the
private `isActive` field once per lifecycle. On Unity's main thread it requires the current scene to
be `Main`, no active screen transition, and exactly one loaded Back to Menu control. It then takes
the `RunTransition` family permit, invokes the button, and verifies only that `isActive` became
true. Missing or ambiguous controls fail closed. No save data, file, timer, scene coroutine, or
secondary receipt is inspected.

Success is returned before scene teardown as `status: committed, scene: Start`. The later scene
transition advances the shared lifecycle generation; that boundary clears the identity catalog,
world references, action bindings, leases, and every other lifecycle-retained object through the
ordinary scene-transition observer. The MCP does not wait for a post-transition world because the
operation that owns the response is itself destroyed by that transition.

## Supervised validation

1. Start in `Main` with no screen fade active and note the current save timestamp in the UI only.
2. Invoke `game_return_to_menu` once and require one complete terminal response before the screen
   changes.
3. Require the response to contain only `status: committed` and `scene: Start`.
4. Observe the native fade/loading presentation and arrival on `Start`.
5. Confirm a newly connected MCP read reports `Start` and no retained Main-world publication.
6. Continue the selected save and confirm the manual-save event preserved a harmless disposable
   UI-state change.
7. Invoke from `Start` and require the player-language wrong-scene refusal.
8. Invoke during an already active scene fade and require `transition_in_progress` with no second
   transition.

No game or save was touched while deriving this contract.
