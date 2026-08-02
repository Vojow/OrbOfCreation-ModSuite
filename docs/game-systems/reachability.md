# Reachability

Where a purchasable appears in the interface is authored, and a candidate can belong to more than one
route. A route is a list that some screen shows; the candidate is reachable when **any** screen
carrying **any** of its lists is currently available.

Most content has two routes, usually in one of these shapes:

- **Parent tab plus subtab.** E.g., the *Witchcraft* attribute sits in a single Wizardry list, and
  that list is shown by both the Magic tab and the Wizardry subtab.
- **Aggregate list plus screen list.** A Wizardry upgrade can sit both in the all-upgrades aggregate
  on the Upgrades panel and in the Magic screen's own upgrade list. Aggregate screens are legitimate
  routes, not summaries.

**Single-route content is where things go missing.** If the one screen carrying a candidate is
unavailable, the candidate is simply not reachable, even though everything about it is otherwise
satisfied. E.g., *Life Weaver* is reachable only through World > Druidry, because World does not
co-reference Druidry the way Magic co-references Wizardry.

The persistent right-panel Upgrades / Inventory strip is visible on every tab and is genuinely
global, not a per-tab strip.
