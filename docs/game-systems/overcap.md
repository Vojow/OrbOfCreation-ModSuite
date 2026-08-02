# Overcap and the loss timer

Resources can go **above** their capacity. What happens next is a one-shot three-second timer
followed by a rubber band.

Once the timer has engaged and quantity `Q` exceeds capacity `C`, the loss rate is

```
0.85 × (Q − C) + 0.5   units per second
```

evaluated on discrete updates until quantity reaches capacity, where it stops. It pulls on the
*excess* plus a fixed tail rather than on a percentage of the total, so a large overcap drains fast
and a small one lingers.

## What resets the three-second timer

| Event | Resets the timer |
|---|---|
| Any nonzero gain or spend through the normal path — purchases included | Yes |
| An active modifier-backed rate, or an active drain, each tick | Yes |
| A plain authored base rate | **No** |

That distinction is the whole mechanic: a resource fed only by its plain base rate decays back to
cap, while a resource being touched by discrete events or by a modifier-backed rate holds its overcap
indefinitely.

The loss constants above were measured on **advancement** resources (explicit loss 0, base loss 0.5,
overflow-loss modifier 100 %). Whether ordinary resources share them, and whether a purely rate-fed
resource can overcap at all, are open; see [open-questions.md](open-questions.md).
