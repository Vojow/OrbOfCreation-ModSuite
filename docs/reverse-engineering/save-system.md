# Save system

[Back to index](README.md)

## Format

A save file is Base64 text. Decoded and decompressed it is UTF-8 JSON whose top level is
`SaveInfo`:

```csharp
int v;                          // format version; 6 on the audited build
List<List<JToken>> dataArray;   // the packed per-object payloads
List<JsonSaveData> savedData;   // GuidContainer → serialized object data
double timePlayed;
string saveDate;
```

`JsonSaveData` associates a `GuidContainer` with one object's serialized data, which is what makes
the file readable at all: every row is addressed by the same persistent UUID the runtime registry
uses.

## Collection pipeline

`SaveStateManager.CollectJsonData()`:

```text
registered IdScriptableObjects
  → keep the saveable ones
  → cast to ISaveable
  → CollectSaveData()
  → drop empty JsonSaveData
  → build SaveInfo / dataArray
  → serialize to JSON
  → compress
  → Base64
  → ooc_save_N.sav
```

Loading runs it backwards: `SaveStateManager.ImplementLoadedJson()` resolves each saved UUID
through `IdScriptableObject.GetInstance(Guid)` and calls `ISaveable.LoadSaveData()` on the
registered runtime object. A UUID with no registered object has nowhere to land — this is why the
registry must be populated before a load boundary, and why that boundary invalidates cached
references (see [architecture.md](architecture.md)).

Resources are typical: `ResourceSO.CollectSaveData()` produces a `ResourceSO.ResourceSaveData`
whose fields are the saved state listed in
[resources-and-bigdouble.md](resources-and-bigdouble.md); both load overloads deserialize it,
apply it, then call `ClearNans()`.

## Reading one by hand

1. Copy the `.sav` out of the save directory. Never work on the live file.
2. Base64-decode it, then decompress; the result is plain JSON and is recognizable immediately if
   you got both steps right.
3. Pretty-print it and look at `savedData`: each entry's `GuidContainer` gives you a UUID.
4. Resolve those UUIDs against [`data/entity-mappings.tsv`](../../data/entity-mappings.tsv) —
   `tools/find-entity.py` takes UUIDs as well as names — to learn which asset and managed type each
   row belongs to. The save itself carries no type information.
5. Read the row's fields against that type's `*SaveData` shape in IL. Quantities are `BigDouble`
   pairs, so a value that looks like two unrelated numbers is one number.

Diffing two saves taken either side of an in-game action is the cheapest way to find which asset
owns a piece of state you cannot locate in IL.

## Never write saves yourself

`SaveStateManager` owns explicit file and backup paths and an asynchronous
`WriteFileAndBackupAsync`. A mod that writes a save file concurrently races that operation and can
lose or corrupt the player's progress.

The rule is: mutate runtime objects on the Unity thread and let the game save them normally. If a
mod owns state that must ride along with a save, hook the boundaries in
[architecture.md](architecture.md) rather than touching the file.
