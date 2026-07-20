# Sanitized runtime replay fixtures

[Back to testing hub](README.md) · [Headless E2E](headless-e2e.md) · [Repository strategy](strategy.md) · [Runtime UAT](runtime-validation.md)

## Purpose and boundary

Runtime replay fixtures reproduce ordering-sensitive lifecycle, invalidation,
and scheduling observations without launching Unity. They drive the reusable
lifecycle scenario kernel and the production Auto Buy engine through the same
headless native boundary as other E2E journeys.

Version 1 is intentionally not a log format, save parser, or runtime recorder.
It accepts only reviewed typed setup data and already-sanitized observations.
It has no arbitrary metadata, free-text payload, player identity, filesystem
path, save content, or private game field. A fixture must never be generated
directly from an active save.

## Version 1 document

A document has exactly five root members in canonical order:

```json
{
  "schema": "orb-of-creation/runtime-replay",
  "schemaVersion": 1,
  "replayId": "queue-refill-v1",
  "setup": {
    "queueCapacity": 6,
    "primaryResource": {
      "uuid": "77777777-7777-4777-8777-777777777777",
      "expectedNativeType": "ResourceSO",
      "initialQuantity": 1000
    },
    "candidates": [
      {
        "uuid": "11111111-1111-4111-8111-111111111111",
        "expectedNativeType": "StructureSO",
        "baseCost": 1,
        "costScaling": 1,
        "available": true,
        "maximumLevel": 4
      }
    ]
  },
  "events": []
}
```

The exact schema identifier and version are both required. Identities are
always a lowercase canonical UUID plus an exact, unqualified native type.
Candidate types are limited to `StructureSO` and `UpgradeSO`. Frames and
microseconds are non-negative integers. Event sequence numbers start at zero,
are contiguous, and frames and timestamps never move backward.

Candidate UUIDs are globally unique within setup. The expected native type is
still required and checked on every targeted operation, but V1 rejects a
cross-type UUID collision before dispatch because the production candidate
index is UUID-keyed and treats that collision as invalid.

V1 frames are capped at 100,000 and timestamps at 86,400,000,000 microseconds
(24 hours) so a reviewed fixture cannot request an unbounded dispatch loop or
an unsafe clock conversion. Every timestamp gap must divide exactly across its
frame gap. Events sharing a frame must also share the exact microsecond
timestamp. Array/sequence order is authoritative within that frame; the
dispatcher advances the kernel clock and frame once, then delivers each
lifecycle and native observation without an implicit extra frame.

Setup owns one exact `ResourceSO` identity and its initial quantity. Every
candidate cost is constructed against that UUID, and a resource event must
target the same UUID/type pair. The scalar quantity is therefore never detached
from the identity used by the simulated native economy.

V1 amounts use the deliberately reduced .NET `decimal` domain. They must be
finite, non-negative ordinary JSON decimal tokens; exponent notation is
rejected. This is sufficient for the current reduced queue fixtures but does
not claim to encode the game's full `BigAmount` range. A future typed
mantissa/exponent amount needs a reviewed schema version rather than an opaque
field added to V1.

The codec rejects unknown or duplicate members, unknown schema versions,
comments, trailing commas, excessive nesting, unknown event kinds, unsafe scene
names, unsupported native types, and open-ended configuration keys. Canonical
output uses stable property order, UTF-8 without a byte-order mark, and LF
newlines.

## Events

Every event begins with `sequence`, `atFrame`, `atMicroseconds`, and `kind`.
Version 1 permits exactly these typed variants:

| Kind | Additional members | Replay effect |
|---|---|---|
| `lifecycle` | `transition`, identifier-safe `sceneName`, `nativeIdentityToken` | Sends an observation to the production lifecycle monitor. A token creates deterministic object identity only within the replay. |
| `resource` | `ResourceSO` identity, decimal `quantity` | Updates the simulated authoritative resource boundary, notifies the production catalog with exact previous/current amounts so cached affordability is invalidated, and publishes resource invalidation. |
| `queue` | integer `manualActions` | Preflights the complete count against native queue room, adds all actions atomically, and publishes queue invalidation. Zero is a valid observation-only signal. |
| `progression` | Structure/Upgrade identity, boolean `available` | Changes authoritative candidate availability and publishes progression/registry invalidation. |
| `inventory` | reviewed `ArtifactSO`, `SpellSO`, or `AlchemyRecipeSO` identity, integer `quantity` | Publishes a typed inventory observation. V1 does not synthesize inventory mutation. |
| `configuration` | `setting`, boolean `enabled` | Applies the reviewed `AutoBuyEnabled` switch. No arbitrary configuration key is accepted. |
| `completion` | Structure/Upgrade identity, integer `count` | Preflights every requested queue-front entry against the exact UUID/type, then completes atomically through the simulated native queue, notifies the production engine, and publishes queue/progression invalidation. Manual or mismatched front entries reject before mutation. |

The dispatcher accepts exactly the lifecycle transitions defined by the
production lifecycle monitor. Recorded frame gaps must reproduce integer
microseconds exactly. Time cannot advance while the frame stays unchanged, and
a timestamp gap must divide exactly across its frame gap. Candidate lookups use
an exact UUID/type index constructed once from setup rather than scanning the
bounded catalog per event. These rules keep runs identical and bounded across
developer machines and CI rather than depending on wall-clock scheduling.

## Reviewed conversion workflow

The converter combines two inputs:

1. A reviewed setup JSON object containing only `queueCapacity`, one exact typed
   `primaryResource`, and typed candidates.
2. UTF-8 JSONL observations with one compact V1 event per nonblank line.

```powershell
dotnet run --project tools/OrbModding.ReplayConverter/OrbModding.ReplayConverter.csproj -- `
  --setup reviewed-setup.json `
  --observations sanitized-observations.jsonl `
  --output tests/fixtures/replays/new-regression-v1.json `
  --replay-id new-regression-v1
```

All input is parsed and the complete document is validated before publication.
Output is written to a same-directory temporary file and atomically moved into
place. A parsing, validation, or publication failure leaves no partial output
and does not replace a pre-existing reviewed fixture. Review the resulting diff
for identities, quantities, and ordering before committing it.

The converter deliberately does not ingest BepInEx free-text logs, saved games,
cloud data, arbitrary JSON objects, or private-shaped fields. If an observation
cannot be expressed by the seven V1 events, extend and review the schema and
dispatcher together rather than embedding an opaque payload.

## Fixtures and tests

Canonical fixtures live in `tests/fixtures/replays/` and are copied to the test
output. `queue-refill-v1.json` covers completion-driven refill after initial
saturation. `chained-progression-v1.json` covers sequential unlock, purchase,
completion, and the next unlock while also exercising all seven event kinds.

Run the replay scope with:

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~RuntimeReplayTests"
```

These tests prove strict parsing, canonical round trips, converter failure
containment, exact dispatch ordering, repeated deterministic results, stale
generation rejection, queue refill, and chained progression. They do not prove
the installed game contract, Unity callback wiring, the actual save format,
visual behavior, or subjective responsiveness; those remain installed-contract
and UAT gates.
