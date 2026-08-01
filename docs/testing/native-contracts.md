# Native contract workflow

[Testing doctrine](README.md) ·
[Reverse-engineering audit](../reverse-engineering/audit.md)

[`data/native-contracts.json`](../../data/native-contracts.json) is the audited
compatibility boundary for reflection and Harmony. It admits complete
`Assembly-CSharp.dll`/`Assembly-CSharp-firstpass.dll` hash pairs and records each
target's exact metadata, owner, use, source tokens, and place: `capture`,
`action`, or `patch`.

The manifest proves native shape, not runtime behavior. Adapters still resolve
and validate their complete binding sets and fail closed. The source audit asks
whether every literal selector is declared somewhere; it intentionally does not
couple contracts to source-file paths. Exact-path exemptions are reserved for
generic framework or UI-navigation reflection with a reason, never mixed
gameplay adapters.

## Add or change a native target

1. Inspect the installed assembly and record the exact declaring type, member
   kind, overload, visibility, staticness, return/value type, inheritance, and
   ordered parameters.
2. Change the manifest with the source. Record all owners, reflection or Harmony
   use, boundary place, and every literal source token.
3. Use an exemption only when the selector is deliberately framework-generic;
   keep it to one exact path and explain why it is not gameplay authority.
4. Run the contract project without `OOC_GAME_DIR` to prove schema and source
   coverage: `dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj -p:UseGameStubs=true`.
5. Point `OOC_GAME_DIR` at the audited installation and run the same project to
   verify the admitted pair and every metadata contract. Build production
   projects against those references when their generated bindings changed.
6. Use the runtime protocol for lifecycle safety and native side effects;
   metadata cannot prove either.

## Audit a game update

Treat the update as a reviewed manifest diff:

1. Add one complete platform pair with audit date, build description, and
   platform-relative provenance. Never admit independent hashes that can form an
   untested mixed pair.
2. Run the installed audit even when the hash is unknown so identity and all
   structural differences appear together.
3. Update changed signatures in place, add genuinely new targets, and remove
   contracts no supported selector names. Reconcile source exemptions and
   boundary places in both directions.
4. Compile against the candidate references, then validate affected behavior in
   the game. Hash acceptance does not replace adapter validation or verified
   postconditions.
5. Keep unknown complete pairs in compatibility quarantine. An incomplete or
   undiscoverable pair remains a total refusal; explicit acceptance of one
   unknown pair does not generalize to another.
