# Per-level scaling

An effect shown as `×1.074/level` states how the next level compares to the last. **Whether the
levels of one effect stack additively or multiplicatively is a property of that effect**, and both
exist in the game. Two levels of an additive effect worth +80 and +90 give +170; two levels of a
multiplicative effect worth ×2 and ×3 give ×6. Nothing in the display distinguishes them, so read the
effect rather than generalising from another one.

## The generator shape

The common shape for an attribute that adds a flat rate is additive-on-base with a cumulative
per-level multiplier:

```
total ≈ base × (1 + m + m² + m³ + …)
```

where `m` is the per-level factor. With `m = 1.05` the second level does not add 5 % — it roughly
**doubles** output, because it contributes its own full base on top of the first. E.g., one observed
aura produced `3.17e-2` at level 2 against `1.54e-2` at level 1, exactly `1.54e-2 × (1 + 1.05)`.

Early generator levels are therefore approximately doublings. The per-level factor only starts to
look like a small percentage much later, once the sum has many terms.
