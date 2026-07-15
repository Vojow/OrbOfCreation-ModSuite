# Orb Toolbox plan

[Back to roadmap](roadmap.md)

## Goal

Provide controlled runtime editing and debugging utilities for experimentation, testing, and optional cheat play without directly editing active save files.

## Positioning

Toolbox is intentionally explicit rather than automatic. Every state-changing action is initiated by the user, previewed, logged, and limited to the selected scope.

## MVP interface

- Toggleable window, proposed default `F2`.
- Searchable list of currently registered resources.
- Display name, UUID, quantity, lifetime quantity, and capacity.
- Value input accepting scientific notation and `BigDouble` mantissa/exponent form.
- Operations: add, subtract, set, multiply, fill capacity, and set zero.
- Convenience multipliers: `×2`, `×8`, `×10`, and `×1e6`.
- Bulk operation: multiply all currently owned resources.
- Dry-run preview showing affected resources and before/after values.

## Runtime APIs

Prefer the verified APIs:

```text
IdScriptableObject.GetAllInstances()
IdScriptableObject.GetInstance<ResourceSO>(Guid)
ResourceSO.GetQuantity()
ResourceSO.Gain(...)
ResourceSO.Spend(...)
ResourceSO.SetQuantity(BigDouble)
```

`ResourceSO.MakeVisible()` is private in the current assembly. Visibility changes are excluded from the normal public-API path and require a separately audited, explicitly advanced implementation.

`SetQuantity` clamps capped resources. The UI must clearly report the applied value rather than claiming the requested value succeeded. Direct private-field mutation is an advanced operation and is excluded from v0.1.

## Safety levels

### Safe

- Read and inspect values.
- Add through normal `Gain` behavior.
- Spend through normal `Spend` behavior.
- Set within normal capacity.
- Copy UUID or formatted value.

### Advanced

- Make an undiscovered resource visible.
- Bulk operations.
- Set zero.
- Large exponents above a configured threshold.

### Unsafe — deferred

- Bypass capacity.
- Modify lifetime quantities.
- Change applied levels or timers.
- Alter persistent-reset progression.
- Directly invoke save import/load while in a running scene.

Unsafe features should not be included until recovery and invariant testing exist.

## Audit trail

Every mutation records:

```text
timestamp
game version
plugin version
resource name and UUID
operation
requested value
old value
applied value
```

The log must contain no personal paths or unrelated save content.

## Snapshot strategy

Optional pre-change snapshots should use the game's export/collection pipeline when safe. If runtime export cannot be proven safe at the action point, instruct the user to save normally and copy the existing save outside the plugin.

Toolbox must never race the game's asynchronous save writer.

## Insights integration

If Orb Insights is installed:

- A tooltip/context action can open the target in Toolbox.
- Toolbox can reuse exact-number presentation conventions.
- Recent edits can appear as an optional tooltip line.

Both plugins remain independently usable.

## Definition of done for v0.1

- Search finds all currently registered `ResourceSO` objects.
- Add, set, and multiply work for capped and uncapped test resources.
- Requested and applied values are distinguished.
- Dry-run produces no state changes.
- Bulk operations affect only explicitly eligible resources.
- Audit entries match actual post-operation values.
- Normal game saving persists edits and reloads successfully.
- No direct `.sav` write occurs.
