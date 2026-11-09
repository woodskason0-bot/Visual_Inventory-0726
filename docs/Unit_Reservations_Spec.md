# Reservations — Build Spec

Scoped 2026-11-08 with Kason, from the mockup artifact
(https://claude.ai/artifact/2ZzFZwHTYNwpFEajeYqXJb). **Approved to build** the same
night; **built and committed as `3c08bd6` on 2026-11-09** (what was checked: the
handoff's "Reservations" entry in Current state). One pass. Schema change: two new tables and one new column on each unit
table, one migration (43, `AddReservations`). Also fixes BC-01 and BC-07 from
`docs/Bug_Checks.md`, because holds change the same availability math those two
bugs live in.

---

## 1. Kason's rulings (2026-11-08)

1. **Hard hold.** Nobody but the reserver can order, pick up, transfer or scrap held
   stock. If the reserving engineer orders it, **anyone can pick that order up**: the
   gate keys on who placed the order (`Order.RequestedBy`), never on who is standing
   at the shelf.
2. **The hold starts the moment it's made.** From is when it's needed.
3. **Who:** Standard and up reserve items on their own Line (Admin anywhere, same as
   ordering). The reserver, Engineer and up on the item's Line, and Admin can edit or
   release.
4. **One reservation can hold several units of one item.** Later, people will want to
   gather holds for one build type ("3P build") in one place. Asked for: a way to keep
   holds for one purpose together, and a clickable option to reuse it when reserving a
   different item.
5. **Anything can be held by count.** A hold reduces **Available**, never On hand
   ("Available to you" is always the same as or less than On hand).
6. **Motor lab # is free text** and never required to hold a motor; people decide at
   the time whether to put a lab # on it.
7. **A dated hold ends on its own** after its Until date, and **the reserver is
   notified.**
8. The Reserved Items page groups by the **item's** Line.
9. Reserve only offers stock beyond what **pending orders already claim**, so a new
   hold can't break an order already accepted.
10. **Fix BC-01 and BC-07** in the same pass.

## 2. Calls made where the words left room

**Confirmed by Kason 2026-11-09: all five stand.**

- **"Anything" = every item type**, not just compressors and motors. Compressors and
  TC motors additionally let you name specific units; everything else is count only.
- **Purpose groups = the reason text.** No separate table. The Reserve dialog shows
  your own open reasons as buttons ("Reserved for 3P build"); clicking one fills the
  reason. On Reserved Items, your own holds are grouped under one header per reason.
  A shared, cross-person "specification" table can come later without migrating
  anything typed now.
- **Holds are per stack.** A reservation holds N (and, for motors, T thermocoupled)
  on each stack it names, so pickers and Scrap know which shelf is spoken for.
- **A short-pull correction is a physical recount**, so it is never refused for a
  hold. If the corrected count drops below what's held on that stack, the hold shrinks
  to fit and the reserver is notified.
- **Notifications** go to the reserver when a hold ends today (once), when it ends on
  its own, when someone else releases or edits it, and when a recount shrinks it.

## 3. Data

`Reservations`

| Column | Notes |
|---|---|
| `Id` | |
| `ItemId` | business id, loose reference like every other table |
| `Reason` | required, ≤ 200, also the purpose-group label |
| `ReservedBy` | `First.Last` of whoever made it (always the signed-in person) |
| `StartDate` | local date; informational (rule 2) |
| `EndDate` | local date, nullable = until notice; the hold lasts through this day |
| `CreatedAt` | UTC |
| `Status` | `Active`, `Released`, `Expired`, `Picked Up` |
| `EndedAt`, `EndedBy`, `EndNote` | set when it stops being Active |
| `EndingNoticeSent` | the "ends today" notification went out |

`ReservationLines` — `Id`, `ReservationId` (FK, cascade), `ItemVariantId`, `Quantity`,
`TcQuantity`. One row per stack held.

`CompressorUnits.ReservationId`, `MotorUnits.ReservationId` — nullable. A named
(pinned) unit belongs to one Active reservation and counts inside that
reservation's quantity on its stack. Cleared when the unit leaves or the hold ends.

A reservation is **live** when `Status == Active` and (`EndDate` is null or
`EndDate >= today`). Every query uses that test, so an expired hold stops holding at
midnight even before the sweep marks it.

## 4. Availability (the shared claims engine; fixes BC-01 / BC-07)

One engine (`StockClaims`) answers every "how many can this person take" question:
Item Card, Search Center, Submit, Pickup Queue, transfers and Reserve.

- **Supply:** each active stack's quantity and TC count, grouped by its Team (a blank
  Team is its own group).
- **Claims:** every live hold (pinned to its stack) and every Pending line on a
  Pending order (scoped to its Team, or unscoped).
- **Coverage:** a reserver's own pending lines draw on their own holds first (oldest
  line first, compatible stacks only), so a held unit that its reserver has ordered is
  not counted twice.
- **Feasibility (Hall's condition for this shape):** every team group's held + scoped
  claims fit its supply, every stack's holds fit the stack, and the unscoped claims
  fit what the groups have left. The same for TC.
- **Available for a new claim** = the most that can be added without making any of
  that worse. A viewer's own holds are available to them (coverage does it).

BC-01 was team-scoped availability ignoring unscoped lines; the group/free split above
counts them. BC-07 was TC never reserved across orders; TC is now its own dimension
in the same check, at Submit as well as in the display.

## 5. Gates

- **Pickup** (`FulfillOrderItem`, `PickUpPartialAndSplit`): each stack can give the
  order's requester at most its quantity minus what other people hold there (TC the
  same). The requester's own held stacks are pulled first and their hold goes down by
  what left; a hold that reaches 0 ends as `Picked Up`. If other people's holds are
  what makes the line short, pickup is refused with their names, not sent down the
  short-pull path (the shelf count isn't wrong).
- **Named units:** a serial pinned to someone else's hold can't be picked for another
  person's order; the "No serial" and oldest-first fallbacks skip them, and the
  requester's own pinned units go first. TC motors the same.
- **Transfers:** the requester is always on another Line, so every hold counts.
- **Scrap, downward Adjustment, Location Transfer:** a stack can't drop below what's
  held on it, and a named held unit can't be ticked as leaving or moving. A whole-stack
  merge moves the hold with the stock.
- **Short-pull correction:** never refused; holds shrink to fit (§2).
- **Delete Stack / Delete Item:** refused while a live hold names it.

## 6. Screens

- **Item Card:** Reserved tile (when anything is held), the Reserved section (one block
  per reservation in Kason's sentence: reserved for *reason* by *person* from
  *MM-DD-YYYY* to *MM-DD-YYYY* / until notice), a Reserved column on Stacks and on the
  unit list, and a Reserve button beside Handle Stock.
- **Reserve dialog** (Search Center): reason with your open reasons as buttons, From,
  Until notice / a date, and per stack a quantity (and TC for motors) capped at what's
  holdable, tick boxes for recorded units, and boxes to record a new compressor serial
  or motor lab # and hold it.
- **Reserved Items page** (sidebar, under Search Center): your holds grouped by reason;
  then your Line, the rest of your Branch, then other branches greyed. A Branch-wide
  viewer sees yours, your Branch, other branches; a viewer with no Line or Branch sees
  yours, then everything, nothing greyed. Inside a group the soonest end comes first,
  until-notice last; an amber chip when a hold ends within 7 days. Rows open the Item
  Card on Search Center.
- **Search Center** rows show "N reserved" when something is held.
- **Pickup Queue** lists a held serial as not pickable for anyone else's order.

## 7. Logs and notifications

Log rows (ItemId-keyed, `QuantityChange` 0): `Reserved`, `Reservation Changed`,
`Reservation Released`, `Reservation Ended`. Notification category `Reservation`, link
to Reserved Items. A background sweep every 10 minutes marks expired holds, logs them
and sends the notifications; it also runs once at startup.

## 8. Out of scope

Shared purpose groups across people, a reservation spanning several items, holds on
the Unit Tracker's whole AC units, email, and BC-02 (pickup can fill a TC request
with a non-TC motor) beyond what the TC caps above already prevent.
