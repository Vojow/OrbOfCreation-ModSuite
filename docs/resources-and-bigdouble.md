# Resources and BigDouble

[Back to index](README.md)

## ResourceSO state

Verified saved fields include:

```csharp
bool visible;
BigDouble quantity;
BigDouble lifetimeQuantity;
BigDouble discoveryTime;
BigDouble appliedMaxQuantity;
long appliedLevels;
List<QuantityTimer> replenishTimers;
List<QuantityTimer> decayTimers;
```

Resources also contain modifiers for rate, capacity, gain, drain, loss, reservation, decay, replenishment and overflow.

## Important methods

| Method | Role |
|---|---|
| `GetQuantity()` | Current stored quantity |
| `GetTrueQuantity()` | Quantity after game-specific interpretation |
| `Gain(...)` | Normal gain path with modifiers, notifications and reverberation |
| `GainInternal(...)` | Low-level capped/overflow addition |
| `Spend(...)` | Normal spending path |
| `SetQuantity(BigDouble)` | Direct assignment clamped to zero and max capacity |
| `SetToCap()` | Fill to capacity |
| `GetLifeTimeQuantity()` | Lifetime accumulated quantity |
| `GetQuantityObservable()` | UI/system notification source |

### SetQuantity behavior

Inspected IL confirms:

```text
if resource has a maximum:
    quantity = Max(0, Min(requested, maxQuantity))
else:
    quantity = requested
```

Therefore, `SetQuantity` cannot exceed capacity for a capped resource. `GainInternal` has separate overflow behavior governed by `canOverflow`.

### Gain behavior

`Gain`:

1. Rejects a zero amount.
2. Optionally resets loss state.
3. Applies `gainRate` unless `isRaw` is true.
4. Registers lifetime gain unless it is a splash gain.
5. Calls `GainInternal`.
6. Updates quantity and channel observables when requested.
7. Optionally accumulates reverberation.

For a cheat/debug action, `Gain` is semantically safer than directly assigning the private field. For an exact quantity, `SetQuantity` is simpler but still capacity-clamped.

## BigDouble

`BigDouble` is defined in `Assembly-CSharp-firstpass.dll` as a value type with:

```csharp
double mantissa;
long exponent;
```

It represents approximately:

```text
mantissa × 10^exponent
```

For example, `[1.446, 23]` represents approximately `1.446 × 10²³`.

Verified constructors and helpers include:

```csharp
new BigDouble(double mantissa, long exponent)
BigDouble.Normalize(double mantissa, long exponent)
BigDouble.Pow10(long exponent)
BigDouble.Parse(string value)
```

It supports arithmetic and comparison operators. Prefer constructors/operators over modifying `Ma`/`Ex` properties independently so normalization is preserved.

## Resource containers

- `ResourceListVariable` exposes `List<ResourceSO> GetAll()`.
- `ResourceCostList` represents spend/gain lists and performs costs, refunds and generation.
- `ResourceFillList` tracks partial investment into costs.
- `ResourceManager` owns `allResources` and a generated-resource cache.

