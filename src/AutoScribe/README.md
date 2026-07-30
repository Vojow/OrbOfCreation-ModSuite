# Auto Scribe

Auto Scribe keeps the six audited producible Scroll roles supplied at the strongest currently
affordable Scribe level, replacing weaker stock until the game's native per-item carry limit is
filled. Each required craft first probes above the current unlocked ceiling so native Scribe
progression can advance. It is disabled by default and runs only while Auto Items Scroll use is
active and healthy.

## Responsibility boundaries

- `Identity` owns the baseline-specific UUID plus expected-type facade and the audited semantic
  craft-cost order. Policy, configuration, and normal UI use stable `ScrollRoleKey` values.
  Worker-visible roles are copied into Common's audited immutable publication table.
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
- The gameplay quick button uses that same audited Scholar icon and the same committed mode command
  as the Mods header. Configured intent remains visible while dependency, lifecycle, or emergency
  state is reported separately in its tooltip.

## Safety behavior

Unknown baseline identity, incomplete target evidence, dependency loss, action-family conflict,
stale lifecycle, missing queue room, failed affordability, or ambiguous postconditions reject work.
Locked recipe roles are dormant rather than degraded and are reconsidered from every fresh world
publication. The native adapter re-reads the live unlocked maximum, probes monotonically for the
highest affordable level above it, and falls back below the frontier when progression is not yet
affordable. The cheapest enabled visible recipe is tried first. This advances native
`maxStartingLevel` without changing the player's manual starting-level selector.
Auto Scribe does not write enchantments, invoke Scroll consumption, edit persistent automatic
Scribe entries, change the player's selected Scribe level, discard inventory, or edit saves.

Auto Items remains the only Scroll consumer. Its shared-plan admission and immediate native target
preflight block a Scroll when no useful target remains and reopen it after a later world generation
finds a candidate.

Manual one-shot Scribe queue entries reserve matching deficits as queued supply. Player-owned
automatic entries are classified separately as external production pressure: they suppress a
competing suite craft without being misreported as a completed coverage reservation.

See the [active validation plan](../../docs/plans/auto-scribe.md) and
[native evidence](../../docs/reverse-engineering/auto-scribe-native-pipeline.md). The maintained
[Auto Items and Auto Scribe test pyramid](../../docs/testing/automata/auto-items-scribe.md) maps
portable, installed-contract, journal, and Unity evidence.
