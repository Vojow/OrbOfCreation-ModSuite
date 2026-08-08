# Pool unlockers

Unlocker glyphs — the game also calls them Recipe Books — are option-space expanders. They contribute
no rate and no power of their own; they change what the discovery pools *contain*. A rollable enters
a pool only once **all** of its glyphs are unlocked.

E.g., observed unlockers: Learn Insight grants the Manifestation glyph and unlocks the Insight
resource; Learn Psionic grants the Elemental glyph and opens a new spell and augment pool; Learn
Storm grants the Storm and Spark books.

Because a rollable needs every one of its glyphs, unlockers control *when* it is worth paying to
roll: a pool enriched before you draw from it gives better options for the same price.

Glyph Discoveries is its own discovery tree with its own price ladder, separate from Spell
Discoveries; see [discovery-pricing.md](discovery-pricing.md).

The socketable augment is called a glyph too. The reliable test is what it does — unlockers expand
what you can later roll, augments change a spell you already own. See
[vocabulary.md](vocabulary.md).

**Code shape:** the Spellcraft picker asks `GlyphSO.IsAvailable()`. Discoverable glyphs answer
from their discovery state; non-discoverable pool unlockers answer from their authored `Learn X`
prerequisite. The raw glyph discovery field is therefore not a universal learned-state signal.
