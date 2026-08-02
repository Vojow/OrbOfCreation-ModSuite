# Toxicity

Using items fills a **Toxicity meter**, and items cannot be used while it is full. Toxicity is a real
resource with a cap, and the game's unusual growth levers are pointed in reverse on it:

- Each use adds the item's Toxicity cost to the meter (e.g., food fruits observed at `8`).
- The meter **drains back down over time**, following a missing-percent shape in reverse: the fuller
  the meter, the faster it empties.
- It also carries a **resting rate**: go a while without using any items and recovery speeds up
  further.
- Research nodes modify these aspects separately.

Toxicity is therefore a **rate limiter on item usage**: the meter, not the stockpile, decides how
often items can be spent. See [growth-levers.md](growth-levers.md).
