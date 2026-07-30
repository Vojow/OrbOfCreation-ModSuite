# Auto Scribe

Auto Scribe keeps the six audited producible Scroll roles supplied at the highest currently
craftable Scribe level. It is disabled by default and runs only while Auto Items Scroll use is
active and healthy.

## Responsibility boundaries

- `Identity` owns the baseline-specific UUID plus expected-type facade. Policy, configuration, and
  normal UI use stable semantic `ScrollRoleKey` values. Worker-visible roles are copied into
  Common's audited immutable publication table.
- `Coverage` owns the native-free plan shared by Auto Scribe production and Auto Items Scroll-use
  admission.
- shared world readers publish levelled inventory, pending uses, recipes, target evidence,
  enchantments, and active/automatic Scribe work. They never retain Unity objects in the snapshot.
- `ServiceCycle/AutoScribeWorker` selects at most one enabled deficit from one immutable world
  generation. It retains no delegate or other main-thread capability; dependency readiness is
  checked by the service start boundary and revalidated by the native action adapter.
- `ServiceCycle/AutoScribeNativeAdapter` owns main-thread revalidation, the bounded one-shot craft,
  postconditions, and lifecycle quarantine.
- `ServiceCycle/AutoScribeServiceCycleFeature` owns registration and lifecycle wiring;
  `AutoScribeServiceCycleDiagnosticsBridge` owns feature-health projection.
- `Ui/AutoScribeRolePickerView` edits semantic roles with All, None, and Default controls. UUIDs are
  never shown or persisted. The consolidated Mods rail uses the already-audited native Scholar
  top-bar icon, matching Scribe's native parent view.

## Safety behavior

Unknown baseline identity, incomplete target evidence, dependency loss, action-family conflict,
stale lifecycle, missing queue room, failed affordability, or ambiguous postconditions reject work.
Auto Scribe does not write enchantments, invoke Scroll consumption, edit persistent automatic
Scribe entries, change the player's selected Scribe level, discard inventory, or edit saves.

Auto Items remains the only Scroll consumer. Its shared-plan admission and immediate native target
preflight block a Scroll when no useful target remains and reopen it after a later world generation
finds a candidate.

See the [active validation plan](../../docs/plans/auto-scribe.md) and
[native evidence](../../docs/reverse-engineering/auto-scribe-native-pipeline.md).
