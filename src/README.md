# Source layout

Build the suite with `OOC_GAME_DIR` set to the Orb Of Creation install root.

Expected build-reference layout (use a gitignored staged tree on non-Windows platforms):

```text
$OOC_GAME_DIR/
  BepInEx/core/BepInEx.dll
  BepInEx/core/0Harmony.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll
  Orb Of Creation_Data/Managed/UnityEngine.CoreModule.dll
```

The suite ships as one BepInEx 5 DLL built by the single project `OrbModSuite.csproj` at this directory's root, which compiles every feature folder below it. `Common` owns gameplay-neutral safety and runtime-orchestration contracts shared by the features. It must not own domain policy, retain native game objects, or become a gameplay feature merely because several services use its scheduler.

The feature folders are `AutoBuy`, `AutoCast`, `AutoConcept`, `AutoHarvest`, `AutoItems`,
`AutoScribe`, `Automata`, `Common`, `Mentor`, `ModConfig`, and `SpellLeveling`; `Plugin.cs` and
`SuiteConfiguration.cs` at this root are the one `BaseUnityPlugin` and the one configuration
transaction that bind them together. Orb Insights and Orb Toolbox remain design-only.

## Automatic save backup

`Plugin.Awake` runs the automatic save-backup gate before the assembly audit, configuration bind,
Harmony patches, lifecycle subscriptions, feature controls, or ServiceCycle composition. The save
root is Unity's runtime `Application.persistentDataPath`; no platform-specific user path is
embedded. The gate reads only top-level `*.sav` files and `steam_autocloud.vdf`, matching the
supported installer, and opens each file for reading without write sharing. It reads each source
twice, verifies the copied bytes, rechecks the complete source set and contents, and publishes the
backup directory only after every file agrees.

Completed backups are `<save root>/backups/auto-modsuite-backup-yyyyMMddTHHmmssZ`. Incomplete work
uses a distinct `.partial-*` staging name and cannot be mistaken for a completed backup. Retention
keeps five completed directories and deletes only names that match that exact automatic-backup
grammar; installer, manual, malformed, and partial directories are never candidates. A pruning
failure is a visible degraded-health warning but does not disarm automation because the new backup
already exists.

The last successful suite version, normalized save root, verified backup path, and file count are
stored in a strict stamp beside the BepInEx suite configuration. A missing, unreadable, malformed,
wrong-version, wrong-root, or missing-backup stamp triggers another backup. Copy or stamp failure
leaves automation disarmed for the launch and does not publish a success stamp, so the next launch
retries. The release Start card gives one calm automatic-backup guarantee; the performance-debug
card keeps only the created/ready status and verified file count. Runtime diagnostics retain the
completed path and count. Both Start shapes and Runtime health show the exact blocking failure;
accepting an unverified game build and clearing STOP cannot bypass this gate.

## Shared configuration schemas

`OrbModding.Common.ConfigurationSchemaTransaction` is the supported pre-bind configuration boundary. A plugin declares ordered one-version steps and a hidden `[OrbModding] ConfigurationSchemaVersion`; the transaction snapshots the original file, creates a non-overwriting sibling backup, disables `SaveOnConfigSet`, consumes only reviewed source keys, runs normal typed binding, writes the marker last, and saves once. Current schema files bypass mutation. Malformed, negative, future, bind, and save failures return no usable configuration and restore the original file exactly before publishing a sanitized exact-GUID status.

Migration code must use explicit known-key maps and closed safe failure codes. It must not surface arbitrary exception messages, file paths, serialized values, or infer meaning from unknown settings.

## Deterministic runtime foundation

`OrbModding.Common.Runtime` holds monotonic time, catalogs, configuration and strategy publications, the shared world collection, diagnostics, and tracing. The contracts contain no Unity objects or gameplay policy, and the configuration UI consumes their typed projections. The earlier R0 scheduler it once carried was deleted at the Auto Harvest cutover.

`OrbModding.Common.Runtime.ServiceCycle` is the accepted foundation and is production-composed: it
drives world collection, Auto Harvest, Auto Buy, Spell Leveling, Auto Cast, Auto Concept, Auto
Items, and Auto Scribe. The ordinary runner through semantic export, snapshot export, and schema-v7
recording is verified by the portable suite. Replay was retired as a runtime system rather than
rebuilt. See the [runtime architecture dossier](../docs/runtime-architecture/README.md).

## Shared automation decisions

`OrbModding.Common.AutomationDecision` is the suite-wide automation diagnostics boundary. Producers use explicit codes, dispositions, retry triggers, stable identity plus expected native type, validated queue snapshots, structured native state, and normalized resource constraints. `ConditionKey` owns deduplication; English `TechnicalDetail` and display names are evidence for people only. Logs and tooltips render with `AutomationDecisionPresenter`, while future Insights consumers subscribe through `AutomationDecisionPublisher` and must tolerate missed/coalesced equivalent conditions rather than influencing automation.

Auto Buy is the first production adopter. Auto Cast, Auto Concept, Mentor, feature-health state, and configuration transaction results remain outside this contract for now.

## Shared gameplay controls

`AutomationFeatureControlRegistry` is the one ordered roster for the Mods feature headers and the
gameplay quick controls. The closed surface is one native-width compound control under the native
top-left gear and character buttons: a full-size emergency stop followed by an attached,
separately framed disclosure footer. The square's size, alignment, and vertical stack step are
captured from the adjacent native buttons instead of guessed; its icon is the exact shipped
`power-lightning` Sprite, enlarged inside that unchanged square rather than enlarging the control.
The disclosure enumerates the
registry into a transient, native-framed four-column panel below the compound control; it never
maintains a second feature list. The closed disclosure carries a separate exclamation marker plus
red color whenever a contained feature is faulted or blocked. The surface anchors through the
declared, scene-bound `UIContentArea.canvas` contract and exact `Canvas/HelpButtons` structure. The
32-pixel footer and panel reuse the audited recessed `UIViewRadioButton` frame as sliced border
dressing;
the panel surrounds an opaque suite-owned background.
Every suite-created UI node requests `RectTransform` through
`GameObject(string, Type[])`; a plain `GameObject(string)` exposes only the declared
`Transform` contract and is never cast to a UI transform.

Each live feature control requires its audited native glyph plus both
`UIViewRadioButton.baseImage` and `activeImage`. OFF uses the recessed inactive frame; configured ON
uses the raised active frame. Gray, green, red, and orange remain secondary health channels, and
tooltips carry the same joined feature status as Mods Runtime. A missing anchor, glyph, or frame
pair creates no corresponding live control and publishes the exact failure. STOP is separately
visible and uses the exact audited `power-lightning` Sprite with immediate press-to-stop and
press-to-resume `Safety/EmergencyDisable` semantics. Its native frame and the attached disclosure
footer use deep green while clear and deep red while stopped; frame structure, glyphs, and tooltips
keep state from depending on color alone. General uses that same command instead of staging the
safety switch behind Apply. Capture/construction failures name the member plus expected
and actual types, log that installation will retry on the shared five-second UI cadence, and become
terminal diagnostics only on the third failed attempt, matching the Mods rail.

## Shared alchemy gameplay-domain classifier

`OrbModding.Common.AlchemyGameplayDomainClassifier` distinguishes ordinary alchemy from Scholar Concepts without reading internal or display names. Initialize it once per lifecycle after `IdScriptableObject.RuntimeLookup` contains the exact `ConceptRecipes` UUID/type asset. Recipe classification then requires exact `AlchemyRecipeSO`, stable recipe UUID, concept-registry membership or verified exclusion, exact `AlchemyTypeSO`, and one of the audited ordinary or Scholar type UUIDs. Missing or contradictory evidence returns `Unknown` with a shared evidence level, named sources, detailed flags, and a diagnostic reason. Active consumers require `IsMutationGrade` rather than trusting the domain label alone.

The classifier caches the verified registry snapshot and per-recipe results. Call `InvalidateLifecycle()` on scene, save-load, reset, and NG+ changes; do not initialize or reflect inside a per-frame or native-XP hook. See [Alchemy gameplay-domain classification](../docs/reverse-engineering/alchemy-domain-classification.md) for the evidence matrix and adoption contract.

## Shared typed registry resolver

`OrbModding.Common.TypedRegistryResolver` is the suite boundary for `IdScriptableObject.RuntimeLookup`. Resolve by non-empty UUID plus exact expected native type, retain the returned lifecycle generation with cached native references, and use `IsRetryable` rather than parsing reason strings. `ResolveMember` distinguishes verified inclusion from verified exclusion; malformed list evidence never proves absence. Names are diagnostics only.

Common's suite-internal `KnownEntities` is a checked-in generated declaration set for the small, explicitly selected supported-domain subset in `data/known-entities.tsv`. Each `KnownEntity<TContract>` has a suite-owned type marker plus UUID, expected managed type name, and diagnostic asset name; generated signatures never embed fragile game types. The build verifies it against the canonical 2,792-row mapping, while runtime consumers still resolve through `TypedRegistryResolver` and validate the installed game.

`OrbModding.Common.GameplayInvalidationBus` coordinates bounded cache and scheduling invalidation across the suite's features. Publishers use lifecycle generation, completed Unity-frame bursts, domains, stable UUIDs, and expected native types; the bus never retains native objects. Its callbacks only dirty existing resumable work. Immediate lifecycle cancellation, queue safety, Mentor XP capture, and native mutation validation remain direct.

`OrbModding.Common.ActionFamilyOwnershipRegistry` is a small process-local safety boundary, not a gameplay framework. Suite features atomically claim only the native mutation families they own, release them with configuration and lifecycle teardown, and recheck their lease immediately before mutation. Exact known external conflicts revoke overlaps; unknown unregistered callers remain an explicit limitation.

## Mentor

`OrbMentor` shares mastery in three independent domains: spells, artifacts, and alchemy. Spells use the native `SpellRecipeSO.GainMasteryExp(BigDouble)` boundary; the optional artifact and alchemy domains use their separately audited native hooks and grant paths. Each domain fails closed independently on contract or lifecycle errors.

## Test build

Run the game-independent suite with:

```bash
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true
```

The test property replaces external game references with `tests/OrbModding.GameStubs`. It is not used for production builds or runtime validation.
