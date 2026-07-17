# Orb Quiet Reflection

Lifecycle: experimental; implementation complete, interactive validation pending.

Orb Quiet Reflection suppresses the frequent popup entries produced when Reflective learning passives enter or leave cooldown. Those entries commonly contain Splash resource effects.

The mod changes only the result of the native `PassiveAbilitySO.IsQuiet()` query for passives carrying the audited Reflective passive-type UUID. It does not mute audio globally, change passive cooldowns, skip effects, alter resource gains, or write additional save data. Existing native mute choices for every other passive remain authoritative.

## Configuration

- `General.Enabled` enables the plugin.
- `Notifications.SuppressReflectiveSplashNotifications` enables the Reflective-only filter.

Both settings default to `true`. Changing either setting takes effect immediately.

## Safety and validation

If the native `PassiveAbilitySO.IsQuiet()` signature is missing or changed, the plugin fails closed and leaves notifications untouched. Runtime validation must confirm that all Reflective learning popups disappear while their Splash resource effects and cooldown transitions remain unchanged.
