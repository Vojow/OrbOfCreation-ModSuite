# Orb Quiet Reflection

Lifecycle: experimental implementation; interactive validation pending.

[Back to plans](README.md) · [Module reference](../../src/OrbQuietReflection/README.md)

## Goal

Suppress the high-frequency popup entries produced by Reflective learning passives without changing their triggers, cooldowns, Splash resource effects, progression, or save representation.

## Audited native path

The installed game build uses this notification path:

1. `PassiveAbility.EnterCooldownEffects(ScalingInfo)` and `ExitCooldownEffects(ScalingInfo)` execute the native effects.
2. Before execution, each method queries `PassiveAbilitySO.IsQuiet()`.
3. A non-quiet passive adds effect tooltip nodes to its `LogBook`.
4. `UIPassiveAbilityItem` reads that log book and creates the visible popups.

The native effect execution occurs outside the quiet check, so forcing only the query result to `true` suppresses log-book entries without skipping gameplay effects.

## Identity and filter

The filter applies only when a passive carries `ReflectivePassiveType` with UUID `95a27ac0-751c-4972-922c-cc6b8c0949da`. Names are diagnostics only. Other passives retain the native `silent` and player-controlled `muted` behavior.

The Harmony postfix targets the parameterless Boolean `PassiveAbilitySO.IsQuiet()` contract. If that signature cannot be validated, patch installation is rejected and native behavior remains unchanged. Runtime identity failures also preserve the original method result and emit one warning.

## Release boundary

Orb Quiet Reflection is a standalone experimental plugin and is not part of the supported suite allowlist. Promotion requires installed-game contract tests plus interactive evidence that:

- all ten mapped Reflective learning passives stop creating popup entries;
- their enter/exit effects and cooldowns continue normally;
- expected Splash resource quantities match an unmodded control;
- manual mute behavior for unrelated passives is unchanged;
- disabling either config setting restores native Reflective popups immediately;
- save/load and reset do not change suppression scope.
