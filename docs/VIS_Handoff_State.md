# VIS (Visual Inventory System) — Handoff State

**Supersedes the July 2026 handoff (stopped at Pass 4) and the Pass 9 handoff below it.**
Current as of the 2026-08-07 Claude Code session (Passes 10–15). I went live against
my real copied-in database during this window, then followed up the same stretch with
data cleanup, an access-control pass, the Branches/Lines redesign, a round of UI/data
polish, and finally mandatory-Line registration + Line-scoped log visibility — all
below. The Pass 13/14 db (with all its data work) has now actually been copied to the
host and is what I'm testing against going forward.

I'm the owner (Rheem). Built on ASP.NET Core MVC (`net10.0`), EF Core + SQLite, Razor,
Bootstrap 5 dark theme. Session-based name-only "identify" (no password) plus a numeric
`AccessLevel` (1 Viewer – 5 Admin) via `[RequireLevel(x)]`.

**Delivery model changed starting with Pass 10.** Every pass before that shipped as a
folder-structured zip I copy-merged into my own VS project and compiled/migrated/tested
myself — Claude never ran the app. From Pass 10 on I've been running Claude Code (a CLI
agent with real Bash/PowerShell, a `dotnet` toolchain, and a browser-automation tool)
directly in my repo:
- Edits files in place (no zip).
- Runs `dotnet build` after every real chunk of work, not just at the end.
- Generates every migration with `dotnet ef migrations add` — the old "no dotnet SDK in
  sandbox, hand-author every migration" constraint no longer applies in this environment.
- Starts the actual app and drives it through a real browser tab — clicking, filling
  forms, reading the DOM, checking console for errors, reading the server's live SQL
  log — against my real, copied-in `inventory.db`, not a fixture.
- Commits/pushes only once I've explicitly said to.
See "What was actually verified live" near the end of this file before trusting any
claim above that a feature "works" — some things were confirmed this way, some were
only confirmed by static reading and a clean build, and the difference is spelled out.

---

## Deployed state

```
Migrations       33   (latest: 20260807043630_AddBranchesAndLines)
Items           487   (compressor ownership reconciled against real claim sheets;
                 leftover test-fixture rows removed; one exact duplicate merged;
                 63 Residential OD compressors re-minted with an RCR- prefix and
                 Group flipped to Residential -- see Pass 14)
Compressor units count evolving -- On Hand roster is directly editable, not just
                 a pickup-time log
Motor units      TC only, seeded from real pickups
Locations        18   Parent/Major/Sub, managed
Map zones         4   seeded, editable
Teams             8   Samurai, Ninja, Falcon, Polaris, Hurricane, T-Rex, Spartan,
                 Sustaining (all 7 real names now exist as rows, plus Sustaining)
Branches          3   Residential Air, Commercial Air, Sustaining -- now managed
                 in Settings, not hardcoded (see Pass 13)
Lines            10   managed alongside Branches, same place
Users            51   (9 added in Pass 14 -- see Pass log)
```

Publishes **self-contained** (`-r win-x64 --self-contained`) to `C:\VIS_Publish` on the
host laptop, started with `--urls "http://0.0.0.0:5000"`. Database lives at
`C:\VIS_Inventory\inventory.db` — an absolute path in `appsettings.json`, so it is
**not** carried by publish and must be moved deliberately.

**Migrations apply automatically on startup** (`Program.cs` calls `Migrate()`) — no
manual step needed. All migrations through Pass 13 (`AddOrderItemStatus`,
`RenameLineVocabulary`, `AddTeamLine`, `AddMotorUnits`, `AddUserBranch`,
`AddTransactionLogItemName`, `AddBranchesAndLines`) were confirmed to apply cleanly
against my actual copied-in database, with no `PendingModelChangesWarning`.

---

## Working conventions

- Read every file fully before editing. No blind edits.
- **Claude Code sessions (Pass 10 on):** edit in place, `dotnet build` after every real
  chunk, `dotnet ef migrations add` for schema changes, test live against the real app
  and real db when possible, `git commit`/`push` only when I explicitly say to.
- **Older chat-based sessions (Pass 9 and earlier):** delivered as folder-structured
  zip (path-preserving), listed changed/new files one line each, hand-authored EF
  migrations by mirroring existing migration file structure and regenerating
  `AppDbContextModelSnapshot.cs`/`*.Designer.cs` by hand. If a session ever loses shell
  access, that constraint is back — use this pattern.
- Verify brace/paren balance and diff against prior delivered state before packaging,
  if hand-authoring.
- Caution before schema migrations / breaking changes; proceed on routine work.
- **"Spiterate before building"** — my own term for how I want a genuinely new mechanic
  handled: write back the complete scope in prose (every field, every gate, every edge
  case) and wait for my explicit confirmation before opening a file. Used successfully
  for the short-pull flow, Team.Line, MotorUnit, the Sustaining branch, Delete Item,
  and the Branches/Lines redesign.
- Tone: direct, low-formatting, no unneeded explanation.

---

## Architecture — load-bearing facts

**`InventoryItem` = family identity. `ItemVariant` = per-location physical stock.**
`InventoryItem.Quantity` and the location fields are `[NotMapped]` pass-throughs over
variants — **never usable inside raw EF LINQ queries**, only after materialization.
18 models legitimately have two variants (stocked at both New Test Cells and the Lean-To).

**Visibility is Line, not Team.** Signed-in user sees items where
`item.Line == "" || item.Line == user.Line` (case-insensitive). **Level 5 bypasses
entirely.** A blank *item* Line fails OPEN for everyone. A blank *user* Line is a
separate, stronger case: it skips the filter entirely and that user sees the whole org,
not just blank-Line items — worth knowing before assuming "unassigned" means "scoped to
nothing." (Pass 13 added a middle option — see `User.Branch` below.)
`InventoryService.ApplyLineVisibility()` filters `GetAll`/`Search`/`ExportToCsv` only;
`GetById`/`FindByRheemPart`/quantity math stay unfiltered on purpose so org-wide dedup
and cross-Line reassignment keep working.

**`OrgStructure.cs` is managed now, not hardcoded (Pass 13).** Branches and Lines live
in `Branches`/`OrgLines` tables, editable from Settings the same way Teams and Locations
already were. `OrgStructure` itself stays a static class every other file reads from —
it's a DB-backed snapshot refreshed at startup (`Program.cs`) and after any Settings
Branch/Line edit (`SettingsController.RefreshOrgStructure()`), same pattern
`LocationCodec` already used for Locations. `BranchFor`/`GroupFor`/`IsValidLine`/
`AllLines`/`BranchLines` are all unchanged as a public surface — nothing that reads them
had to change. Branch is still **derived**, never stored on an item — see Group below.
(While rebuilding this I found `LocationCodec` itself never actually got a startup
refresh despite the comment claiming it did — added a real one for both.)

**Group is derived and frozen at creation.** `OrgStructure.GroupFor(user.Line)` maps a
Branch to its Group word by stripping a trailing `" Air"` (`Commercial Air → Commercial`,
`Residential Air → Residential`); a Branch that doesn't end in `" Air"` (like
`Sustaining`) passes through as-is. Blank/unrecognized Line falls back to `Commercial`.
It exists only to mint the `ItemId` prefix, and records *who registered the item*. Not a
form field, not editable afterwards — changing it would put an item's Group at odds with
its own id.

**`LocationCodec.Encode()` is the only thing that turns a name into a code** — 1st, 3rd,
5th and last alphanumeric. Codes are **derived, never stored**. `Decode` reads a volatile
map pushed in by `LocationCodec.Refresh()` at startup (`Program.cs`) and after any
Settings edit.

**`CompressorUnit` is a roster, not a pickup log** (changed in 6A). Holds On Hand stock as
well as units that have left. **Quantity remains authoritative; units are a partial
overlay** — most units don't have serials, so unit rows must never drive counts. Unique
index is `(ItemId, SerialNumber)`, blanks exempt: LG reuses serials *across* models, so
serial alone can't be unique.

**Superuser gate** is separate from `AccessLevel`: session name matches the configured
username **and** the passcode from `appsettings.json`. "Master key not stored in any
lockbox it opens." Admin (5) alone does **not** unlock Settings — the two are
deliberately independent, since Settings is what edits AccessLevel itself.

**Pickup:** `OrderService.PickUpOrder()` (not "FulfillOrder"). Pulls from the
**lowest-numbered variant first**, which has physical consequences — an order can send
someone to two buildings.

**A short pull refuses, it doesn't silently under-fulfill (Pass 10).** `PickUpOrder`
checks real shelf stock per line before pulling anything; a line that can't be honored
gets `OrderItem.Status = "Cancelled"` and is returned to the caller as a `ShortPullLine`
instead of pulling what's there. `ReportShortPull` is the correction: the picker reports
the true count per location, stock gets corrected (logged as `"Stock Adjustment"`, not a
pickup), and a fresh order is issued for the corrected qty and picked up in the same
action.

**`CompressorUnit` and `MotorUnit` are two entrances now, not one (Pass 11–12).** Both
used to be created only at pickup. Both modals now also let a human log/correct a unit
directly — `InventoryService.LogCompressorUnits`/`LogMotorUnits` — for stock that's on
the shelf and was simply never recorded. `MotorUnit` is `CompressorUnit`'s sibling, not a
generalization of it (deliberately kept separate): no `SerialNumber`, just an optional
`LabNumber`, and — unlike `CompressorUnit` — a Picked-Up `MotorUnit` row stays editable,
since there's no serial to fall back on for identity.

**`Team.Line` and `User.Branch` are pure metadata, never a visibility gate (Pass 11,
13).** A team can carry a home Branch/Line; a user can carry either a specific `Line` or
an entire `Branch` (new in Pass 13, for someone who needs broader-than-one-Line
visibility without the full Admin bypass — e.g. a director who oversees a whole Branch).
Both only *suggest* — Line on the item/user is what actually governs visibility, and
neither of these fields touches that.

**`ModifyStock`'s Adjustment action can reclassify existing stock as TC (Pass 12)** —
"take my current N units, mark X of those as thermocoupled," independent of whatever
quantity delta the same adjustment makes. Distinct from Add's "these NEW units include X
TC."

**Delete Item is the app's first true hard-delete (Pass 13).** Available only once an
item's total quantity is 0 and nothing is still outstanding — no unit still `Picked Up`
on a `CompressorUnit`/`MotorUnit`, no line on a `Pending` order. Removes the
`InventoryItem` and its `ItemVariants`. Deliberately does **not** touch `TransactionLogs`
or already-`Returned`/`Scrapped` unit history rows — those keep reading under the
now-defunct `ItemId`, same soft-hide philosophy as Team/User, just applied to a real
delete for the first time. Re-registering the same model later mints a fresh `ItemId`;
numbers are never reused. Gated Admin (5) — the only action in the app that removes a
catalog row outright, not just its stock.

**`TransactionLog.ItemName` is a point-in-time snapshot (Pass 13).** The Activity Feed's
primary lookup is still a live join against `InventoryItems` (so a rename shows up
retroactively), but that join returns nothing for a deleted item. This column is the
fallback for exactly that case — populated at every item-transaction log site going
forward; logs written before this existed just fall back further, to "Unknown item."

**The Activity Feed reads a blank `TransactionLog.ItemId` as the real discriminator for
"this isn't about an inventory item" (Pass 14).** Every Settings/admin action (users,
teams, branches, lines, locations, zones, notifications — 20 log sites) is logged with
`ItemId = ""` on purpose, since none of them are about one. The feed used to assume
every log row was an item action and tried to look one up regardless, which rendered as
`": Unknown item"` for all of these. Fixed by branching on whether `ItemId` is blank:
item actions keep the existing item-name-lookup path, non-item actions just show their
own `ActionType` as the title and `Details` as the subtitle — the same thing View Logs
already did correctly, just brought over. This is deliberately **not** a per-ActionType
ternary — a brand new Settings action reads correctly here automatically the moment it's
added, with nothing else to update.

**A user's effective Branch, for UI purposes, is: their `Line`'s Branch if set, else
their `Branch` field directly, else blank (Pass 14).** `ViewBag.MyBranch` in
`HomeController.Index` computes this once per request. It currently only drives graying
out the two Quick Filter branch buttons a user isn't on (Admin sees all three, same as
Luis/Derek/me) — it is **not** a visibility mechanism, `ApplyLineVisibility` is still the
only thing that actually gates data.

**Line is mandatory on new registrations as of Pass 15 — but only new ones.** Both
`CreateItem` and `SubmitIntake`/Intake now reject a blank `Line` server-side (and the
form fields carry `required` client-side too). This does **not** retroactively touch the
~180+ items already sitting blank across Motors/EEVs/Coils/etc. — the "blank Line fails
open" default from Pass 3 is still exactly as true for everything already in the system.
This just closes the door going forward.

**`InventoryService.ApplyLogVisibility()` extends the same Line rule to `TransactionLog`
rows (Pass 15).** View Logs and the dashboard Activity Feed used to show every log entry
to every signed-in user regardless of Line — a strictly wider audience than could even
see those items in Search. Built on top of the existing (private) `ApplyLineVisibility`
rather than duplicating its logic: a log row is visible if it isn't about an item at all
(blank `ItemId` — every Settings/admin action), if the item it names has since been
deleted (nothing left to check a Line against — same "keeps reading sensibly" treatment
Delete Item already gives these), or if the item it names is one the viewer can currently
see. Verified live: Admin (me) saw 578 rows on View Logs, a user scoped to a single Line
(Cedric Martis, Residential Coils/AH) saw 349. **Deliberately does not touch direct item
actions** (Modify Stock, Delete Item, Ownership, etc.) — those still use the unfiltered
`GetById`, unchanged, so someone who knows an `ItemId` can still act on it even if they
can no longer see it in Search or Logs.

---

## Pass log

**Passes 1–4** — see git history. Superuser Settings, add/remove users,
`NotificationSubscription`, Rheem PN `N/A` as first-class, Branch/Line org structure,
compressor serial capture at pickup.

**Pass 5 — the three stacked bugs.** Migrations had silently failed three times.
Root cause was `PendingModelChangesWarning`: `CompressorUnit.ItemId`'s index was in the
snapshot but not configured in `OnModelCreating`, so `Migrate()` aborted before running
any SQL. Behind it, `AllowNARheemPartNumber` folded blanks to `N/A` before dropping the
old unique index — `UNIQUE constraint failed` on the second row. Both were swallowed by
`Program.cs`. Fixed the model drift, reordered the migration, and normalised five
placeholder PNs that only existed as workarounds for the index that never got relaxed.

**Compressor rebuild (SQL, not a code pass).** Wiped 75 compressors / 84 variants / 76
logs / Order 3. Loaded 85 models / 189 units at New Test Cells, then 177 models / 636
units at the Lean-To. Reconciliation of five notebooks plus an intern's PDF used
max-on-overlap within the bottom-row group rather than summing — my own "the two halves
should be even" test put the halves at 320 vs 313.

**Pass 6A — CompressorUnit roster.** `+ItemVariantId`, `+Status`, `+RecordedAt/By`;
`OrderId`/`PickedUpAt`/`PickedUpBy` nullable. Unique `(ItemId, SerialNumber)`.
`PickUpOrder` became match-or-create — mandatory, not optional.

**Pass 6B — Done Using.** Compressors became loanable. `LoanableQuantity` was dead code
duplicated inline in `PickUpOrder`. `LoanOutstanding` is reused for compressors meaning
"not yet dispositioned" — deliberate naming debt, one counter that can't drift beats two
that must be kept in sync. Reason field added to Return and Scrap for all loanable types.

**Pass 7A — managed Teams.** `Teams` table, CRUD in Settings, Team optional with `N/A`.
Killed a ternary that gave every future team Samurai's project code. Group removed from
the registration form.

**Pass 7B — Transfer means Line.** Deleted a flattened, wrong copy of `OrgStructure` that
lived in the "New Group" picker. Branch/Line moved out of Edit Details into Transfer,
where ownership belongs. Edit Details is identity only again.

**Pass 7C — managed Locations.** The vocabulary lived in four hand-kept copies and had
drifted. One `Locations` table now feeds all of them.

**Pass 8 — map zones.** `LocationZones` table with normalised 0–1 coordinates. Drag-to-draw
editor in Settings.

**Pass 9 — bulk intake.** `Intake` screen, Standard+. `IntakeBatches`/`IntakeRows` hold
batches whose location isn't recognised. `CreateItem` and the five ordering actions
moved Engineer → Standard so interns can register and order.

**Pass 10 — short-pull flow, compressor filter, ItemId branch fix.** See "A short pull
refuses" above. Compressor modal got its first filter bar. Fixed `ItemId`'s Branch
letter defaulting to Commercial regardless of what was picked — it was deriving from the
registrant's own account Line rather than the Branch/Line actually selected on the form.

**Pass 11 — compressor on-hand logging, Team.Line, Line vocabulary rename, shared
cascade helper.** See "Two entrances now" and "`Team.Line`" above. Extracted a
Branch→Line cascade that had been hand-copied three times into one shared helper set.
Fixed a real bug I caught myself before testing found it: moving the Ownership pane's
cascade wiring to run at page load instead of lazily inside an event handler put it
ahead of `const orgStructure`'s own declaration further down the same script — a JS
temporal-dead-zone error that silently killed every initializer after it.

**Pass 12 — TC motor tracking, Adjustment TC reclassification, filter/UX follow-ups.**
See "Two entrances now" and "Adjustment action" above. Motors modal gets the same filter
bar as Compressors; New Item Registry gained a live, non-gating name-match dropdown;
Order Details gained a Brand column.

**Pass 13 (2026-08-06/07) — go-live data reconciliation, access-control pass,
Delete Item, Branches/Lines redesign.** The big one. In order:

- **Compressor Team/Line reconciliation against real claim sheets.** Cross-referenced
  the live db against an export wizard file (Residential OD claims by name) and a
  prefix/suffix reference sheet (Ninja vs. Samurai model families) covering 249
  compressor items. Assigned 63 to Residential OD, 8 to Ninja by exact model match, 53
  confirmed Samurai by base-model-prefix match, and 55 more that share the Ninja-shaped
  prefix pattern but aren't on the exact reference list — those got the correct Line
  with Team deliberately left unassigned rather than guessed, since some may belong to
  a third team (Spartan) that also sits on the same Line. ~70 items in genuinely
  different brand families were left untouched entirely — no guesses where the data
  didn't support one.
- **Data cleanup.** Removed 4 leftover test-fixture items (`TESTCOMP-A/B/C`, "screen
  test coil") along with their full dependent chain — variants, compressor units,
  transaction logs, and the now-empty test orders that existed only for them. Found and
  merged the app's one exact duplicate registration (`YRM083TAA`, registered twice by
  accident, both halves zeroed out mid-fix) back into a single record with the correct
  combined count.
- **Sustaining branch added.** A third Branch beyond the original fixed two, with 4
  Lines (each carrying its own project code in the name, since Lines have no separate
  code field) and 4 matching Teams.
- **Access-control audit and fixes.** Catalogued what every AccessLevel tier actually
  unlocks, end to end, including inline checks that don't match their route attribute.
  Found and fixed a real bug: `CreateItem`/`AddToCart`/`StartOrder` were server-gated at
  Standard (intentionally, since Pass 9) but the UI hid their buttons until Engineer —
  interns were authorized but had no way to actually use it. Renamed the confusingly-
  named `SystemReset` action to `ClearCart` and dropped its gate from Admin to Standard
  to match what it's always actually done (wipe the calling user's own session cart,
  nothing system-wide).
- **`User.Branch` added.** A user can now be scoped to an entire Branch instead of one
  Line, for people who need broader visibility than a single Line but shouldn't have
  full Admin bypass.
- **Delete Item shipped.** See Architecture above.
- **`TransactionLog.ItemName` added.** See Architecture above — this was found and
  fixed as a direct consequence of Delete Item breaking the Activity Feed's live-lookup
  assumption.
- **Branches and Lines are managed vocabulary now, not hardcoded.** See Architecture
  above. Verified live: added a real test Branch and Line through the running Settings
  UI, confirmed it appeared in every picker across the app immediately with no restart,
  then removed it.

**Pass 14 (2026-08-07) — RCR rename, Quick Filter/access polish, Activity Feed fix,
roster growth.**

- **63 Residential OD compressors re-minted `CCR-` → `RCR-0001`...`RCR-0063`,
  `Group` flipped `Commercial` → `Residential`.** These were the compressors reconciled
  onto Residential OD in Pass 13 but still carrying a `CCR-` id and `Group="Commercial"`
  frozen from their original (wrong) registration — a real rename, not a delete/re-add:
  cascaded to the 63 `TransactionLogs` rows and 3 `CompressorUnits` rows that referenced
  the old id, zero stale references left anywhere afterward. This is the one deliberate
  exception to "Group is frozen forever" in this project's history — done because these
  63 were never actually Commercial to begin with, not because a later reassignment
  should retroactively rewrite history in general.
- **Sustaining added as a third Quick Filter button**, alongside the existing
  Commercial/Residential (still Omni-Search-based, matching the existing mechanism —
  worth knowing this reads `Group`/other text fields, not `Line`, same caveat as those
  two already had).
- **Quick Filter branch buttons gray out for the two Branches a user isn't on**, via
  `ViewBag.MyBranch` — see Architecture above. Admin bypasses.
- **"Pick Up Orders" dashboard widget renamed to "Available Tasks."** Flagged, not
  renamed, for a later pass: the `PickupQueue` action/route/view filename, the
  `pickup-box` CSS class, and — worth resolving — that the destination page's own
  heading already reads "Tasks Available" (reverse word order from the widget's new
  "Available Tasks").
- **Activity Feed now formats every Settings/admin action correctly.** See
  Architecture above.
- **9 users added:** Cedric Martis (Engineer, Residential Coils/AH); Nathan Gibson
  (Standard), Jacob Moffett, Chase Binz, Mohamed Elrifae, Ethan Phan, Luis Fragoso,
  Marco Balcazar (Engineer), Travis Gregory (Management) — all eight to International
  (Commercial Air).

**Pass 15 (2026-08-07) — mandatory Line at registration, Line-scoped log visibility.**

- **Line is now required to register an item**, both single-item (`CreateItem`) and
  bulk (`Intake`/`SubmitIntake`) — server-side rejection plus a client-side `required`
  field on both forms. See Architecture above for exactly what this does and doesn't
  cover (new registrations only, nothing retroactive).
- **View Logs and the Activity Feed are now Line-scoped**, via the new
  `InventoryService.ApplyLogVisibility()`. See Architecture above for the full rule.
  Verified live with a real before/after comparison across an Admin and a Line-scoped
  user, not just a clean build.

---

## Traps — read before editing

**`@` is a Razor transition inside `<script>` blocks and JS comments.** Writing
`@RenderBody()` in a JS comment invoked it for real and took the dashboard down. There is
no such thing as a safe comment in a `.cshtml`.

**`PendingModelChangesWarning` aborts `Migrate()` before any SQL runs.** If migrations
"silently don't apply," this is the first thing to check — not the migration itself.

**`Program.cs` swallows DB-init failures** to console and continues. Offered hardening
twice, declined twice; it has cost three debugging cycles. The app runs but nothing works.

**Look for hardcoded vocabulary in more shapes than you expect.** Eight copies of the
location list were found across four passes because each stored something different.
The same lesson applied to Branches/Lines in Pass 13, though that one only had the one
hardcoded dictionary to replace.

**`site.js` loads after `@RenderBody()`**, so a view's inline script runs before it. Moving
shared JS there breaks any IIFE that calls it at parse time.

**A `<script>` block's top-level data `const`s must load before ANY code that might
reference them synchronously** — not just before code that merely defines a function
using them (Pass 11). If a page-load bug takes out a seemingly unrelated set of buttons,
check whether something new is being called unconditionally at top level rather than
deferred into a handler.

**A copied scratchpad/backup `.db` file needs its `-wal`/`-shm` sidecars copied as one
atomic set, or not at all (Pass 13).** A stale `-wal`/`-shm` pair left over from an
earlier backup, sitting next to a freshly-copied newer `.db`, produces
`database disk image is malformed` — even in strict read-only mode, since the corruption
is in the file bytes, not a locking issue. If that error shows up on a copied db, check
whether the wal/shm actually belong to that exact copy of the main file before assuming
real corruption; the fix is usually just re-copying as a clean set (or deleting the
stale sidecars if the source is known to be a standalone file). Also: force-killing a
running app instance leaves the same kind of stray `-wal`/`-shm` behind — checkpoint
(`PRAGMA wal_checkpoint(TRUNCATE)`) and switch to `DELETE` journal mode before handing a
test copy back off.

---

## Known issues — accepted as-is

- **`AlertThreshold` is 0 on all compressors** — no low-stock warning ever fires.
  `SetDefaultThreshold(team, threshold)` exists to bulk-fix it.
- **Superuser passcode is plaintext in `appsettings.json`**, tracked in git and now on the
  host laptop. Git history keeps the old value even after a change, so rotating matters more
  than removing the line.
- **`encodeLoc()` is duplicated** in `Index.cshtml` and `Intake.cshtml`, plus the C# original.
- **`Type` is free text** — no vocabulary. `IsControlType` matches only items literally typed
  `Control`; EEV, VFD and Valve are uncovered.
- **`newGroup`** is a dead parameter on `ModifyStock`, accepted and ignored.
- Build warnings: `NU1903` (SQLitePCLRaw advisory), `CS0114` (`SignOut` hides base member),
  a few `CS8602`. Unchanged count/shape through Pass 13 — nothing this session introduced
  a new one.
- **`<select>` imbalance in `Index.cshtml` is a false positive** — `<select>` inside a JS
  comment. Pre-existing, harmless, don't chase it.
- **27 skipped negative-quantity Ninja rows from the original go-live import** — accepted
  as data debt, not being chased. I asked the original team about it; the honest answer
  was "we'll count better going forward," and that's fine — imperfect starting data that
  improves through normal use beats spending more time reconstructing numbers nobody
  can verify anymore.
- **Motor loan return/scrap auto-selects the oldest outstanding units for that order
  line** (no unit-picker checkboxes, unlike compressors — nothing for a human to
  distinguish between since motors have no serial). Confirmed live for pickup; a real
  Return or Scrap against a TC motor loan specifically hasn't been separately verified.
- **~70 compressor items from the Pass 13 reconciliation are still unclaimed** — genuinely
  different brand families (Highly, GMCC's odd tonnages, the Copeland `ZP` scroll line,
  a few small LG models) that don't match either team's reference sheet. Left untouched
  on purpose rather than guessed.
- **The letter-family hypothesis from Pass 13 is unconfirmed.** The pattern that seemed
  to separate Samurai's compressor family (`YA`/`YRH`/`YGH`-prefix) from Ninja's
  (`YAS`/`YAT`/`YRM`-prefix, plus `YRH067` specifically) held for everything checkable,
  but whether it generalizes across the board is still an open question I'm running down
  separately.

---

## Backlog

- **8C — location requests.** Deferred: the Pass 9 pending queue covers the case that
  actually mattered.
- **9D — CSV import** for volume intake. In-app entry covers small lists; I handle large
  ones by SQL.
- **Rack/Row as managed levels.** The `Locations` table already supports arbitrary depth
  via `ParentId`.
- **Locations tree view** with add-in-place, replacing the flat table + level picker.
- **6C — pickup selects a serial** from a dropdown instead of typing. Optional now that
  match-or-create puts correctness in the service.
- **Team / Location rename** — not offered anywhere; items store names as plain strings, so
  a rename orphans them unless it cascades. Add-and-hide is the workaround. Same is now
  true of Branch/Line rename.
- Notification categories: table is multi-category, only `PickupRequested` exists.
- No backup story — `inventory.db` is hand-copied.
- **Motors: only the TC subset is tracked (deliberate).**
- **Compressor/Motor filter: Team→Branch/Line is one-way on purpose.**
- **`MyOrders.cshtml` was not extended for motor-unit selection.**
- **Add User form in Settings doesn't support whole-Branch assignment at creation time**
  — only the per-row picker on an already-existing user does. Easy to add if it turns
  out to matter.

---

## Carried-forward items from prior review sessions

Nine items were flagged across earlier threads and never confirmed closed. Each was
re-checked against the deployed code.

### DONE — Pass 10

**1 · Short-pull logs the ordered quantity, not the pulled quantity.** Fixed in Pass 10,
and taken further than the original ask — a short pull now refuses outright instead of
silently under-fulfilling.

### STILL PRESENT — layout, cosmetic

**2 · `.map-stats-bar` / `.map-live-bar` are `position: absolute`.** Wanted in normal
flex-column flow. Unchanged.

### CLOSED on inspection — no action

**3 · "Fresh database seeds every user as Viewer."** Does not reproduce. `Program.cs`
seeds users with explicit levels, me as Admin (5). A fresh database is administrable
immediately.

**4 · "Leftover test scaffolding comments in `Program.cs`."** None present.

**5 · "`syncHeaderOffset()` not executing."** Wired correctly.

**6 · "`.holo-viewer` collapsing without explicit width."** Both now carry one.

### UNVERIFIED — cannot be checked from code alone

**7 · TC counts not reserved across concurrent pending orders.** No reservation logic
exists, so the behaviour is plausible; confirming needs two concurrent orders against
the same TC stock.

**8 · Disposed-transaction rollback in a `catch`.** Practically unreachable. Lowest
priority.

**9 · Role-aware gating from an old artefact I no longer have visibility into.** The
Pass 13 access-control audit covered what each level actually unlocks end to end
(catalogued in full — ask if a copy of that catalog is useful), which should supersede
whatever the original concern was, but I haven't gone back to reconcile the two
line-by-line.

---

## What was actually verified live (Pass 10 onward)

Read this before trusting any "works" claim above. Everything here was confirmed by
actually running the app against my real, copied-in database — not a fixture — checking
the DOM, the browser console, and the server's live SQL/exception log after each action.

**Passes 10–12:**
- Migrations apply cleanly, no `PendingModelChangesWarning`.
- Handle Stock, Add to Cart, and the search boxes all confirmed working after the Pass
  11 TDZ fix.
- Ownership pane, New Item Registry, Compressor/Motor filters, Compressor On Hand
  roster, and `PickUpOrder`'s MotorUnit creation — confirmed by me personally on a real
  order (Order 10, CMR-0078).
- Order Details Brand column confirmed rendering a real brand.

**Pass 13:**
- All migrations through `AddBranchesAndLines` applied cleanly against the real db.
- The full compressor reconciliation batch applied and spot-checked against specific
  items discussed by name.
- Test-fixture removal and the duplicate merge both verified with before/after queries
  and a post-write integrity check.
- The three UI/access-level fixes (VIEW CART, Add to Cart, New Item Registry now
  showing at Standard) confirmed rendering correctly for the signed-in test session.
- Delete Item: confirmed the button only appears at quantity 0 and Admin level, and the
  confirmation modal renders with the exact intended wording, against a real zero-stock
  item — stopped short of completing the actual delete so I could test that step myself.
- Branches & Lines: added a real test Branch and Line through the live Settings UI,
  confirmed it appeared in every picker across the app immediately, then removed it.

**Pass 14:**
- The Sustaining Quick Filter, the "Available Tasks" text, and the Activity Feed fix
  were all confirmed live in the running app in a single pass — signed in, checked the
  dashboard, toggled a couple of real Settings actions, and watched them render
  correctly in the feed for the first time.
- The RCR rename was verified with before/after queries and a post-write integrity
  check, plus a stale-reference sweep across `TransactionLogs`/`CompressorUnits`
  confirming zero old `CCR-` ids left anywhere.
- The branch-graying logic was verified for the Admin case (all three buttons enabled
  for my own session) by direct inspection of the rendered button classes; the non-Admin
  graying path was verified by code review, not by actually signing in as a non-Admin
  user.

**Pass 15:**
- `ApplyLogVisibility` verified with a real before/after comparison: signed in as
  Admin (me) and counted 578 rows on View Logs, then signed in as Cedric Martis
  (Engineer, scoped to Residential Coils/AH) and counted 349 on the same page. Real
  reduction confirmed, not just "should filter."
- The mandatory-Line client-side `required` attribute confirmed present and correctly
  labeled on both the New Item Registry and Intake forms by direct DOM inspection. The
  server-side rejection path was confirmed by code/build, not by actually submitting a
  blank-Line registration and watching it bounce.

**Not verified live:** `ReturnLoan`/`ScrapLoan`'s auto-select logic for `MotorUnit`
specifically (pickup was confirmed on Order 10, but a Return or Scrap on that same loan
wasn't separately exercised). The non-Admin case of the Pass 14 branch-button graying.
The actual server-side rejection of a blank-Line submission (Pass 15) end to end.

---

## Current state

Went live for real starting Pass 10. Pass 13 closed out the data-quality and
access-control gaps found along the way and added Branches/Lines as managed vocabulary;
Pass 14 followed up with the RCR rename, a round of dashboard/access polish, and 9 more
real users; Pass 15 made Line mandatory going forward and closed the log-visibility gap.
**The Pass 13/14 database has now been copied to the host** — this is no longer just
sitting in a scratchpad copy, it's what I'm actually testing against. Pass 15 is code
only, no new data, so nothing further needs copying for it specifically.
Remaining before I'd call it fully settled: do one real Return or Scrap on a TC motor
loan, resolve the letter-family hypothesis for the still-unclaimed compressor items,
sign in as a non-Admin user to confirm the branch-button graying looks right in
practice, and actually submit a blank-Line registration to watch Pass 15's rejection
fire for real. Everything else on the scaling list (SQL Server/Azure SQL move, SSO,
backup story) is expansion work, not a blocker.
