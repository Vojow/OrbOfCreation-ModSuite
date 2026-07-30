# Auto Scribe testing

[Automata test map](README.md) ·
[Native pipeline](../../reverse-engineering/auto-scribe-native-pipeline.md)

Auto Scribe portable coverage is under
`tests/OrbModding.Tests/Services/AutoScribe`.

Run the focused scope with:

```sh
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj \
  -p:UseGameStubs=true --filter "FullyQualifiedName~AutoScribe"
```

The required defect cases are semantic cost-rank selection, cycle-pinned role narrowing, whole
publication blocking when one enabled role has unknown evidence, player-owned automatic production
pressure, exact recipe/Scroll mismatch, queue-full and no-target preflights, exact queue and
instant-stock verification, duplicate resource-cost rows, lifecycle quarantine reset, and
post-payment injected failure at payment, construction, initiation, and final admission.

The stubs make `PurchaseQuantity` deduct the exact `GetTotalCost` resources and advance
`maxStartingLevel`; a test that only counts method calls does not prove this boundary.

Run the complete portable gate after the focused scope. The Scribe world reader and GameAction
binding set also require the portable contract gate and the installed contract project against the
accepted game baseline. Portable target fakes inject the candidate set and therefore do not prove
the game's complete structure condition, enchantment ranking, durable Scroll effect, or lifecycle
behavior inside Unity. Those remain runtime-validation evidence.
