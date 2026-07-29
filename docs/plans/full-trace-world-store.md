# Full trace: the world store

## Status

Open decision. Not built, deliberately. It needs an owner's ruling on volume and on how a
world payload is written before anyone writes a line of it.

## What is missing

The north star's full-trace mandate names four streams: raw capture data, configuration
publications, strategy publications, action outcomes. Manual full trace carries three. The
configuration and strategy publications get a store because a settings tree is small and
changes rarely; raw capture data has no store at all, so a trace answers "what did the
service decide" and not "what did it see".

## Why it is not one more store

Volume. The world republishes four times a second, and its payload is the entire raw
reading of the game rather than a settings tree — roughly a megabyte serialized. An armed
session therefore writes on the order of 240 MB a minute, against a suite whose whole
on-disk mandate is ~100 MB. The derived `GameWorldState` is the same magnitude, so
recording the derived shape instead buys nothing.

Codec. Nothing can be borrowed from the
[publication stores](../runtime-architecture/observability.md): their reflected sorted-text form
suits a settings tree, and the world is neither small nor stable. A hand-written codec is roughly
1,200 lines that silently stops recording whatever was added to the world last; a reflection-driven
one is roughly 400 lines that puts reflection on the capture path and the schema in the artifact.
Both are real costs and the choice between them is the actual decision.

## Options

- **Accept the volume, take the reflection codec.** Honest, cheapest to write, and the
  session becomes short by nature — an armed minute is a large artifact.
- **Delta-encode against the previous generation.** Most of the world does not change four
  times a second; this trades capture-path CPU for size and needs a base-generation policy.
- **Store only the generations a decision referenced.** A trace is read backwards from a
  decision, so the worlds nothing decided against may not be worth keeping.
- **Record a category subset.** Keep the categories a bug report actually needs and say in
  the artifact which were dropped, rather than claiming a complete world.
- **Leave it open.** The other three streams already answer most bug reports;
  the raw stream can stay an explicitly open mandate rather than a rushed one.

## Resume pointers

- The gap and its reasoning are stated in
  `../runtime-architecture/observability.md` (mode 1's store section).
- Existing store shape: `src/Common/Runtime/ServiceCycle/Observation/FullTrace/Stores/`
  (`PublicationStoreWriter`, `PublicationValueFormat`).
- The publication the store would follow: the world collector's frame, republished per
  generation.
