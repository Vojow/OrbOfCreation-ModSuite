# The three growth levers

Three growth terms reward opposite behaviour, and which one dominates decides how a resource should
be handled: **missing-percent pays you for being empty, interest pays you for being full, and
resting rate pays you for not transacting.**

## Missing-percent fills

Some effects fill a fraction of what is *missing*, measured against **capacity** rather than current
stock, so raising capacity first and filling afterwards produces very large bursts. The target is a
resource **type**, so two effects whose types overlap on a dual-typed resource both land their fills
on it.

E.g., one observed food fruit tooltip reads `+65.5 % Mental Missing / min` for its duration.

## Interest

Interest is gain proportional to the stock you are holding, so it compounds, and it appears late.
E.g., one observed carrier is Soul Shards, whose Interest Rate of `16.8/min` produced income roughly
26 orders of magnitude above that resource's production rate.

## Resting rate

Some resources regenerate faster the longer they go **untouched**. A constant dribble of automatic
spending suppresses the bonus permanently. Toxicity is a confirmed carrier: its recovery speeds up
when items go unused for a while (see [toxicity.md](toxicity.md)).

Which resources carry interest or a resting rate, and the exact shape of the resting acceleration,
are open; see [open-questions.md](open-questions.md).
