# The game's own auto-buy

The game ships a native auto-purchase, and its trigger is deliberately narrow. It fires only when
three things hold at once:

- the development queue is **empty**,
- a **five-second** timer has elapsed, and
- the cost is **trivial — under 0.1 %** of your current stock of the pricing resource.

Anything costing a meaningful fraction of stock is never bought automatically. That threshold is why
the feature seems to act only on purchases your economy has absurdly outgrown: it is designed to
sweep up exactly those and nothing else.
