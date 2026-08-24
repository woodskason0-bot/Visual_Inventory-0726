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
