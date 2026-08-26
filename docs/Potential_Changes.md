# VIS — Potential Changes

Ideas I've scoped out but haven't committed to building. Nothing in here is
planned or scheduled — it's a place to park the thinking so it doesn't only
live in a chat thread, and so a fresh session (mine or Claude's) doesn't have
to re-derive it if I decide to move on one of these later. Each entry gets
pulled out into its own real spec once I actually decide to build it — until
then, this is deliberately unfinished.

---

## Borrow feature (scoped 2026-08-23, not started)

**The idea:** a Borrow section, browsable by Branch → Line → Type (mirroring
the org structure Search Center's Quick Filters already use), showing another
Line's items read-only — no Add to Cart, no Modify Stock, just one action:
"Request to Borrow," with a duration in days or weeks.

**Why this is bigger than it sounds:** every existing page in this app
enforces Line-scoped visibility (`ApplyLineVisibility`, since Pass 15) — a
user can't currently see an item outside their own Line unless they're
Branch-scoped or Admin. A Borrow section is the first place that would need
to deliberately show items belonging to Lines a user doesn't own. That's not
a small carve-out; it's a second, parallel visibility rule sitting next to
the one every other page already trusts.

**What's actually cheap here:** the Branch → Line → Type browsing structure
itself. `OrgStructure.BranchLines` already holds exactly this shape (the same
dictionary the Quick Filter fix from 2026-08-22 uses for real), and
`InventoryItem.Type` is already filterable everywhere. The read-only rendering
is just Search Center's item card with the action buttons stripped down to one.

**What isn't cheap — real open questions, not implementation details:**

- **Visibility scope.** Does Borrow show every item app-wide, or only items
  within the requester's own Branch (a Commercial Air user can see other
  Commercial Air Lines but not Residential)? Changes the query, not just the UI.
- **Who is "the owner" for approval.** An `InventoryItem` has a Line (and
  optionally a Team), not a single owning person. Candidates: anyone
  Management+ scoped to that Line; the item's registering Team specifically;
  or a broadcast-claim board, same shape as the Delivery feature's Open/
  Claimed/Done pattern (Pass 25) — which already solves almost exactly this
  routing problem and is the one I'd lean toward reusing.
- **Does approval touch real stock?** Compressor/motor pickups already track
  `OrderItem.LoanOutstanding` so a picked-up unit can't also be double-counted
  as available — but that only exists in the context of a normal Order the
  same person placed. A cross-Line borrow has no equivalent today. If an
  approved borrow should reduce the owning Line's own `Available` count the
  same way, Borrow becomes a second consumer of that math, not an independent
  feature. If it shouldn't, this is a much simpler build, but nothing stops
  the same item being promised to two people.
- **Duration: label or enforcement.** A number + Days/Weeks dropdown is easy
  to capture. Whether it becomes a real `DueDate` that feeds an "Overdue
  Borrows" signal (same shape as the existing `AlertThreshold` pattern) or
  just sits there as text is a decision that changes the DB shape now, not
  later, if I want the enforcement path open.

**Rough DB shape, not final:** a new `BorrowRequest` table —
`ItemId`, `RequesterId`, `Quantity`, `DurationValue`/`DurationUnit` (or a
computed `DueDate`), `Status` (Requested → Approved/Denied → Returned, maybe
Overdue), `RequestedAt`, `DecidedBy`/`DecidedAt`, `ReturnedAt`, an optional
note. No separate owner column — that's derived from `Item.Line` through
`OrgStructure`, same as everything else in this app already does it.

**Rough build order, if I move on this:**
1. `BorrowRequest` model + migration, no UI — prerequisite plumbing first,
   same shape Pass 28 (2a) used before touching anything visible.
2. The Borrow browse page itself — the visibility carve-out is the actual
   hard part, not the page.
3. Request modal (Quantity + Duration + optional note) → submit.
4. Approval queue for owners, reusing the Delivery claim-board shape if I go
   that route.
5. Return flow + whatever overdue handling I decide I want, if any.
6. Only if I answer "yes" above: wire into the `Available` math.

**Before this becomes a real plan, not just a scope:** I need to actually
decide the three questions above — Branch-scoped vs. app-wide visibility, who
counts as "the owner," and whether approval touches real stock. Those change
the schema, not just the UI, so they're not something to leave open once code
starts getting written.

---

## Per-team quantity ownership on a single item (scoped 2026-08-26, not started)

**The idea:** one `InventoryItem` today carries a single `Team` value for its
whole stock. Some items — Commercial Packaged/Splits was the real example
that surfaced this — actually belong to more than one team at once, each with
a real claim on a specific slice of the quantity, not just "whoever gets
there first." Team A can order up to what Team A owns; Team B up to what Team
B owns; neither eats into the other's share even though it's the same
`ItemId`. Needs to degrade cleanly to today's behavior when an item only has
one team on it.

**Why this is bigger than it sounds:** `Team` plays zero role today in how
much of an item someone can actually order — `GetAvailableQuantity`/
`GetAvailableForOrder` sum every active `ItemVariant` for the item with no
team-awareness at all, and `FulfillOrderItem`'s pull loop spills across every
one of those variants the same way. That's the exact same shape of gap Pass
29 just closed for location — a pickup used to silently cross a location
boundary it shouldn't have, entirely because the matching/spill logic had no
concept of "which one am I actually allowed to touch." Adding team-scoped
quantity without also teaching that same spill loop to stay inside the
ordering team's own variants just reopens the identical bug on a different
axis.

**What's actually cheap here:** `ItemVariant` already is "a distinguishable
pile of this item's stock" — the exact pattern already used for the 18
models split across two shelf locations. Team ownership fits the same shape:
give `ItemVariant` its own `Team` (defaulting to the parent item's `Team` at
creation, so every existing single-team item keeps behaving exactly as it
does today with zero migration risk) and a second team's claim becomes a
second variant, not a new table. Two variants don't even have to sit at
different physical locations — nothing today enforces that — so Team A's and
Team B's piles can share the same shelf if that's the real-world case.

**What isn't cheap — real open questions, not implementation details:**

- **Ordering-team resolution — decided 2026-08-26.** An order is arranged to
  whoever's ordering and their own Team by default. If that user belongs to
  more than one Team (`UserTeams` is already many-to-many) and the specific
  item actually has more than one team's claim on it, they get asked which
  team they're ordering for — per item, only when it's genuinely ambiguous.
  No prompt when the item has only one team on it, or the user only belongs
  to one team.
- **Pull-loop team boundary.** `FulfillOrderItem`'s spill has to stay inside
  the resolved team's own variants once this exists — Pass 29's
  location-scoping fix, mirrored onto a Team axis. Not optional; this is the
  part that makes the whole feature actually hold instead of just moving the
  same silent-crossing bug somewhere new.
- **What "no Team on either side" means.** An item with no team-split at all,
  or a user with no team assigned, needs a defined fallback — probably
  "everything's visible/orderable, same as today," the fails-open convention
  `Line` already uses — rather than being left as an undefined edge case.
- **Compressor pickup's serial cascade (Pass 29) intersects with this.** If a
  variant carries a Team as well as a location, the serial picker built this
  session needs to also respect team boundaries when it decides which on-hand
  units to actually offer. Worth re-checking once this lands — not
  re-scoping now, just flagging the dependency.

**Rough DB shape, not final:** add `Team` to `ItemVariant` (defaulting to the
parent `InventoryItem.Team` at creation time). `InventoryItem.Team` itself
stays exactly as it is — still the family-level default/legacy field feeding
ID generation and email routing, per its existing doc comment — it just stops
being the last word on ownership the moment a variant diverges from it.

**Rough build order, if I move on this:**
1. `ItemVariant.Team` + migration, backfilled from each item's current
   `Team` — no visible behavior change yet.
2. Team-scoped availability: `GetAvailableQuantity`/`GetAvailableForOrder`
   filter to the ordering user's resolved team's variants.
3. The "which team are you ordering for" resolution step — only surfaces
   when a user's teams and an item's variant-teams both have more than one
   option.
4. Close the pull-loop gap: scope `FulfillOrderItem`'s spill to the resolved
   team's variants only, mirroring Pass 29's location fix exactly.
5. Re-check the compressor serial cascade against team boundaries.

**Before this becomes a real plan, not just a scope:** the ordering-team
resolution is decided, so that part doesn't need to be revisited. What's
still open is the DB-shape call (variant-level `Team` vs. something else) and
whether the pull-loop fix ships in the same pass as the availability change
or right behind it — neither blocks writing this down, only building it.
